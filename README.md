# SkillRadar

A one-person side project: a daily .NET 8 pipeline that scans GitHub for popular, useful Claude
Skills (`SKILL.md` files), diffs them against what it's seen before, ranks them by "heat", and
writes a Markdown digest.

```
Discovery → Harvest → StateStore.Diff → Scorer → StateStore.Persist → Reporter → Deliverer
```

Each stage is one interface (`SkillRadar.Core/Abstractions`), registered via DI. `Program.cs` only
wires things together and runs `PipelineRunner`; it contains no business logic.

## What's implemented (v1)

- **Discovery** (`GitHubDiscoverySource`) — GitHub Search Repositories by topic (`claude-skills`,
  `claude-code-skills`, `agent-skills` by default) plus a configurable seed repo list.
- **Harvest** (`GitHubHarvester`) — lists each candidate repo's tree (Git Trees API, recursive),
  finds every `SKILL.md`, and reads only its YAML frontmatter (`name`, `description`) via the
  Contents API. **Full repos are never cloned or downloaded.** A failure on one repo is logged and
  skipped; it never aborts the batch.
- **State / diff** (`SqliteStateStore`) — EF Core + SQLite. Tracks every skill by `repo:path`, its
  content hash, first-seen date, and last score. `DiffAsync` is read-only; `PersistAsync` is the
  only method that writes.
- **Scoring** (`HeatScorer`) — pure arithmetic: weighted `log10(stars)`, an exponential freshness
  decay off `pushed_at`, and `log10(skillCountInRepo)`. Weights live in config.
- **Reporting** (`MarkdownReporter` + `FileDeliverer`) — builds a digest with "今日新上榜" and
  "熱度 Top N" sections, writes it to `reports/yyyy-MM-dd.md`.

## What's stubbed for v2 (not implemented)

- `LlmRelevanceScorer : IScorer` — send the top-N heat-scored descriptions to a Claude model
  (`ANTHROPIC_API_KEY`) for a 0–10 relevance score + zh-TW summary. Currently throws
  `NotImplementedException` and is **not** registered in DI.
- A Notion/LINE `IDeliverer` — the interface already supports this; just add a sibling
  implementation and swap the DI registration in `Program.cs`.
- Auto-cloning/downloading a chosen skill's full contents.

Do not implement these without discussing scope first — see the original build spec for why they're
deliberately deferred.

## Why HttpClient instead of Octokit

The pipeline only needs three GitHub endpoints: Search Repositories, Git Trees (recursive), and
single-file Contents. A typed `HttpClient` (`GitHubApiClient`) plus
`Microsoft.Extensions.Http.Resilience` gets us GitHub-specific rate-limit handling (see below)
without fighting an SDK's own retry/paging conventions, and keeps the dependency surface small for
a project this size.

## Rate limits

- Authenticated core API (Trees, Contents): 5000 req/hr.
- Search API: ~30 req/min, much tighter.
- `Program.cs` registers a standard resilience handler on `GitHubApiClient` via
  `AddStandardResilienceHandler`, widened to retry on `403` (GitHub's primary/secondary rate-limit
  status) in addition to the default `429`/`5xx`, with exponential backoff + jitter.

## Configuration

Non-secret config lives in [`appsettings.json`](appsettings.json), under `SkillRadar:*`:

| Section | Purpose |
|---|---|
| `GitHub.Topics` / `GitHub.SeedRepos` | what discovery searches for / always includes |
| `GitHub.MinStars` / `GitHub.MaxReposPerTopic` | search filtering |
| `Persistence.DatabasePath` | SQLite file location |
| `Scoring.Heat.*` | HeatScorer weights |
| `Scoring.LlmRelevance.*` | v2 stub config (model id, enabled flag) — unused until v2 |
| `Reporting.TopN` / `Reporting.OutputDirectory` | digest size and where it's written |

**Secrets never go in `appsettings.json` or source.** They're read only from environment variables:

- `GITHUB_TOKEN` — required for any real rate limit headroom (works unauthenticated too, just very
  limited).
- `ANTHROPIC_API_KEY` — reserved for v2, unused today.

## Running locally

```bash
# one-time: set your token for local dev (either works)
export GITHUB_TOKEN=ghp_xxx
# or, from src/SkillRadar.App:
dotnet user-secrets set GITHUB_TOKEN ghp_xxx

dotnet run --project src/SkillRadar.App
```

This applies any pending EF Core migrations automatically (`db.Database.MigrateAsync()` on
startup), then runs the pipeline once and exits — it's designed to be triggered by cron/CI, not to
stay running. Output lands in `reports/yyyy-MM-dd.md` and `state/skills.db` (paths relative to
wherever you run it from).

### Schema changes

Migrations live in `src/SkillRadar.Infrastructure/Persistence/Migrations`. To add one after
changing `SkillEntity` or `SkillRadarDbContext`:

```bash
dotnet tool restore
cd src/SkillRadar.Infrastructure
dotnet ef migrations add <Name> -o Persistence/Migrations
```

## Tests

```bash
dotnet test
```

Covers `HeatScorer` ranking behavior and `SqliteStateStore`'s new/updated/unchanged diff logic
end-to-end against a local (temp-file) SQLite database — no network calls, no mocked GitHub client
needed since Discovery/Harvest aren't exercised by these tests.

## State persistence across CI runs

Every GitHub Actions runner starts from a clean checkout and is thrown away when the job ends — so
if `state/skills.db` weren't saved somewhere durable, every run would see zero prior history and
mark every skill "new" forever, defeating the whole point of the diff stage.

The fix used here: **the workflow commits `state/skills.db` (and `reports/`) back to the repo**, so
the next run's `actions/checkout` restores exactly the state the previous run left off with. This
is the same commit-back mechanism the digest reports already used, just extended to the state file.
An alternative — exporting a JSON snapshot instead of committing the raw `.db` for cleaner diffs —
was considered but not used, to keep this single-mechanism and avoid adding a second persistence
format to maintain; revisit if the binary diffs in `git log` become annoying.

- **Where it lives**: `state/skills.db` (path is config-driven via `Persistence.DatabasePath` in
  [`appsettings.json`](appsettings.json); local runs and CI both default to the same relative path,
  so behavior matches between the two).
- **Do not `.gitignore` it** — see [`.gitignore`](.gitignore), which calls this out explicitly.
- **If you change the EF Core schema** (edit `SkillEntity`/`SkillRadarDbContext` and add a
  migration — see "Schema changes" above), the `.db` already committed to the repo predates that
  migration. `db.Database.MigrateAsync()` on startup will upgrade it in place on the next run, but
  for a breaking schema change you may need to delete the committed `state/skills.db` once (losing
  history) rather than relying on migration to reshape old data.

## GitHub Actions

[`.github/workflows/daily.yml`](.github/workflows/daily.yml) runs on a daily cron plus
`workflow_dispatch` for manual triggers, with a `concurrency` group so two overlapping runs never
race to commit/push at the same time. It builds, runs the pipeline from the repo root (so
`reports/` and `state/` land at the repo root), then commits both back to the repo as
`github-actions[bot]` using the built-in `secrets.GITHUB_TOKEN` (swap in a PAT repo secret if you
need higher limits or private repo access). The commit step diffs staged changes first and skips
committing entirely on a no-op day, so "nothing changed today" never fails the job.
