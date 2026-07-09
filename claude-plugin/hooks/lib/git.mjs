import { execFileSync } from "node:child_process";
import { readdirSync } from "node:fs";
import { basename } from "node:path";
import { userInfo } from "node:os";

function git(args, cwd) {
  try {
    return execFileSync("git", args, { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim();
  } catch {
    return null;
  }
}

export function getRepository(cwd) {
  const remote = git(["remote", "get-url", "origin"], cwd);
  if (remote) return remote.replace(/\.git$/, "").replace(/^https?:\/\/[^@]+@/, "https://");
  return basename(cwd);
}

export function getBranch(cwd) {
  return git(["rev-parse", "--abbrev-ref", "HEAD"], cwd) || null;
}

export function getCommit(cwd) {
  return git(["rev-parse", "HEAD"], cwd) || null;
}

export function getDeveloperId(cwd) {
  return git(["config", "user.email"], cwd) || userInfo().username;
}

const MANIFEST_LANGUAGES = [
  [/\.csproj$|\.sln$|\.slnx$/, "csharp"],
  [/^package\.json$/, "javascript"],
  [/^tsconfig(\..+)?\.json$/, "typescript"],
  [/^pyproject\.toml$|^requirements.*\.txt$|^setup\.py$/, "python"],
  [/^go\.mod$/, "go"],
  [/^Cargo\.toml$/, "rust"],
  [/^pom\.xml$/, "java"],
  [/^build\.gradle(\.kts)?$/, "kotlin"],
  [/\.gemspec$|^Gemfile$/, "ruby"]
];

/** Best-effort, top-level-only manifest scan — good enough for a hint, not meant to be exhaustive. */
export function detectLanguages(cwd) {
  let entries;
  try {
    entries = readdirSync(cwd);
  } catch {
    return [];
  }
  const languages = new Set();
  for (const entry of entries) {
    for (const [pattern, language] of MANIFEST_LANGUAGES) {
      if (pattern.test(entry)) languages.add(language);
    }
  }
  return [...languages];
}

/** Diff between `fromCommit` (repo state at SessionStart) and the current working tree. */
export function diffStats(cwd, fromCommit) {
  const empty = { filesChanged: [], linesAdded: 0, linesDeleted: 0 };
  if (!fromCommit) return empty;

  const numstat = git(["diff", "--numstat", fromCommit, "--"], cwd);
  if (numstat === null) return empty;

  const filesChanged = [];
  let linesAdded = 0;
  let linesDeleted = 0;
  for (const line of numstat.split("\n").filter(Boolean)) {
    const [added, deleted, path] = line.split("\t");
    filesChanged.push(path);
    // Binary files report "-" instead of a number; skip them from the line counts.
    if (added !== "-") linesAdded += Number(added);
    if (deleted !== "-") linesDeleted += Number(deleted);
  }
  return { filesChanged, linesAdded, linesDeleted };
}
