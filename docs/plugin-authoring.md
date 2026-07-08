# Plugin Authoring (stub)

This doc will become the guide for writing PromptOps provider plugins. It's a stub until Phase 5 fills it in with a worked example (`SonarMetricCollector`) and Phase 4b adds the hook contract for the separate, per-repo Claude Code plugin. For now, here's the distinction that matters:

## Two different things are called "plugin" in this project — don't confuse them

1. **Daemon-side provider plugins** (this doc's actual subject). A separate .NET assembly that implements `IPromptOpsPlugin` (defined in `plugins/PromptOps.Plugin.Sdk`) and registers one or more of the provider interfaces from ADR-0003 (`IMetricCollector`, `IContextProvider`, `IAIExecutionProvider`, etc.) into the daemon's DI container. This is how PromptOps integrates with Sonar, Jira, GitHub, Claude Code, and so on, without the core (`Domain`/`Application`) ever knowing those tools exist (ADR-0002). Loaded from the daemon's plugins directory — real discovery/loading lands in Phase 5 (ADR-0004); it's a hardcoded empty list today.

2. **The per-repo Claude Code plugin** (a different artifact entirely — see ADR-0009). This is what actually gets installed into a target repository: a `plugin.json` manifest, shell hooks, and skills, with no compiled code of its own. It talks to the daemon over `localhost`. Built in Phase 4b.

If you're looking for "how do I get PromptOps working in my repo," that's #2 (`docs/installing-promptops.md`, once it exists). If you're extending what the daemon can measure or connect to, that's #1 — this doc.

## `IPromptOpsPlugin` (current shape)

```csharp
public interface IPromptOpsPlugin
{
    string Name { get; }
    string Version { get; }
    void Register(IServiceCollection services, IConfiguration configuration);
}
```

`Register` is where a plugin adds its provider implementations (e.g. `services.AddSingleton<IMetricCollector, SonarMetricCollector>()`) using whatever configuration section the daemon hands it. There's nothing to build against yet beyond this interface — the provider interfaces themselves (`docs/architecture.md` ADR-0003) are still empty contracts, filled in phase by phase as the use cases that need them are built.
