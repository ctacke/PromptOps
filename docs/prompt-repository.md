# Prompt Repository (Phase 2)

Persistence for the `Prompt` aggregate: EF Core over SQLite, one database shared across every repo on the daemon's machine (ADR-0005). This doc covers the schema, the domain/persistence mapping, and how to use it from `PromptService`.

## Schema

```
┌─────────────────────┐        ┌──────────────────────────┐
│ Prompts              │        │ PromptMetadata            │
│ ─────────────────────│ 1    1 │ ──────────────────────────│
│ Id (PK)               │◄──────│ PromptId (PK, FK)          │
│ Name                  │        │ Description                │
│ CreatedAt              │        │ Tags          (JSON array) │
└──────────┬────────────┘        │ Categories    (JSON array) │
           │ 1                    │ Owners        (JSON array) │
           │                      │ ExternalRefs  (JSON array) │
           │ *                    └──────────────────────────┘
┌──────────▼────────────────────┐
│ PromptVersions                  │
│ ────────────────────────────────│
│ Id (PK)                          │
│ PromptId (FK → Prompts)          │
│ VersionNumber   (unique per Prompt)│
│ Content                          │
│ CreatedBy                        │
│ ParentVersionId (lineage, nullable)│
│ ChangelogEntry                   │
│ TemplateVariables (JSON array)   │
│ Status  (Draft|Active|Deprecated)│
│ CreatedAt                        │
└──────────┬────────────────────┘
           │ 1
           │
           │ *
┌──────────▼────────────────────┐
│ PromptDependencies               │
│ ────────────────────────────────│
│ Id (PK)                          │
│ PromptVersionId (FK → PromptVersions, cascade) │
│ TargetPromptVersionId  (plain value — NOT an FK)│
│ Relationship (References|ExtendsFrom|Requires)  │
└──────────────────────────────────┘
```

Two deliberate departures from "obvious" relational design, both load-bearing:

- **`PromptMetadata` is a separate table from `PromptVersions`**, not extra columns on `Prompts`. `IPromptRepository.GetMetadataAsync` queries `Prompts` + `PromptMetadata` only — it never touches `PromptVersions`, which is the literal mechanism behind "metadata is queryable independently of content" (Phase 2's headline acceptance criterion). See `PromptRepository.GetMetadataAsync` and the integration test `Metadata_is_queryable_without_the_query_touching_version_content`, which captures the generated SQL and asserts `PromptVersions` never appears in it.
- **`PromptDependencies.TargetPromptVersionId` is a plain value, not a foreign key/navigation.** A dependency's target can belong to a *different* `Prompt` aggregate entirely — cross-prompt references are expected (a version of "write a test" prompt might `Require` a version of "understand the test framework" prompt from an unrelated `Prompt`). Enforcing an FK here would mean the two aggregates could no longer be persisted independently.

`Tags`/`Categories`/`Owners`/`ExternalRefs`/`TemplateVariables` are stored as JSON-array text columns via an explicit `ValueConverter` (`StringListValueConverter`), not EF's implicit primitive-collection support — chosen so the on-disk shape doesn't depend on provider-version behavior.

## Domain ↔ persistence mapping

`Domain` has zero EF Core dependency (ADR-0002) and its aggregate (`Prompt`/`PromptVersion`) is intentionally shaped for invariants, not for an ORM: private constructors, immutable versions, encapsulated collections. Infrastructure keeps its own plain, mutable persistence records (`PromptRecord`, `PromptVersionRecord`, `PromptMetadataRecord`, `PromptDependencyRecord`) and converts between the two via `PromptMapper`:

- `ToNewRecord(Prompt)` — builds a fresh record graph for insertion.
- `ToDomain(PromptRecord)` — reconstructs the domain aggregate via `Prompt.Rehydrate`/`PromptVersion.Rehydrate` (the exact factories Phase 1 built for this purpose).
- `ApplyChanges(PromptRecord, Prompt)` — reconciles an *existing* tracked record against the aggregate's current state (new versions/dependencies appended, existing version status updated, metadata fields overwritten) rather than replacing the graph wholesale.

`PromptRepository` acts as an identity map for the lifetime of one unit of work: the tracked `PromptRecord` behind a loaded aggregate is cached and reused across `GetByIdAsync`/`UpdateAsync` calls within the same scope, rather than re-querying. Re-querying an already-tracked aggregate turned out to corrupt EF's tracking of the separate-table owned `Metadata` entity and produce a spurious `DbUpdateConcurrencyException` — caching sidesteps it and is also just fewer round trips.

One EF Core gotcha worth flagging for anyone extending this: all four `Id` columns are configured `ValueGeneratedNever()`. Domain generates its own GUIDs; without this, EF's default `ValueGeneratedOnAdd` heuristic sees a *new* child entity (e.g. a freshly-created `PromptVersion`) appended to an *already-tracked* parent's collection, notices the child's key is already non-default, and wrongly concludes the row must already exist — producing an UPDATE instead of an INSERT, which then fails with "0 rows affected" since no such row exists yet.

## Usage

```csharp
var service = new PromptService(promptRepository); // IPromptRepository, DI-registered

var prompt = await service.CreatePromptAsync(
    "Fix a failing test",
    new PromptMetadata { Tags = ["bugfix", "testing"], Owners = ["alice"] });

var v1 = await service.CreateVersionAsync(prompt.Id, "...prompt text...", createdBy: "alice");
var v2 = await service.CreateVersionAsync(prompt.Id, "...tightened wording...", createdBy: "alice",
    changelogEntry: "clarified acceptance criteria");

await service.TagPromptAsync(prompt.Id, ["regression"]);          // merges into existing tags
await service.DeprecatePromptVersionAsync(prompt.Id, v1.Id);      // v1 → Deprecated, v2 unaffected
await service.AddPromptDependencyAsync(prompt.Id, v2.Id, otherVersionId, PromptDependencyRelationship.Requires);
```

Metadata-only read, independent of content:

```csharp
var view = await promptRepository.GetMetadataAsync(prompt.Id); // PromptMetadataView — no version content loaded
```

## Migrations

The initial schema is `src/PromptOps.Infrastructure/Persistence/Migrations/20260708223935_InitialCreate`. To add a new migration after changing the model:

```
dotnet ef migrations add <Name> --project src/PromptOps.Infrastructure --output-dir Persistence/Migrations
```

`PromptOpsDbContextFactory` (an `IDesignTimeDbContextFactory`) lets this run without the Host — no `--startup-project` needed. The Host applies pending migrations automatically at startup (`Database.MigrateAsync()` in `Program.cs`); there's no separate manual migration step for local development.

## Testing

`tests/PromptOps.Infrastructure.Tests` runs against a real, migrated, on-disk SQLite file per test class (`SqliteFixture`) — not `:memory:` and not mocks. This is deliberate: the point of Phase 2 is proving the aggregate round-trips through actual SQLite the way the daemon will use it, including the identity-map and `ValueGeneratedNever` behavior above, which an in-memory provider wouldn't have caught (EF's InMemory provider doesn't generate real SQL or enforce real concurrency semantics).
