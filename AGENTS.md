## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues (via `gh`). See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout — root `CONTEXT.md` + `docs/adr/`. See `docs/agents/domain.md`.

### Scratch

Local agent trash can: `.scratch/` (gitignored). See `docs/agents/scratch.md`.

### Build gate

Default gate: `.\build.ps1 Test` (Linux: `./build.sh Test`). Restores, builds, and runs managed tests. Does not restore or build the Android host and does not need the Android workload.

Android host: `.\build.ps1 CompileAndroid` after `dotnet workload install android`. That is the CI `android` job; the default `Test` target is not that job.
