# Mailtide

Personal, multi-account, offline-first IMAP/SMTP email client for Windows, Linux, and Android built with .NET 10 and Avalonia 12 (`Mailtide.slnx`).

## Build & Verify

```powershell
# Default CI gate (restores, builds, and runs managed MSTest suites; no Android workload needed):
.\build.ps1 Test        # Linux/macOS: ./build.sh Test

# Android host compilation (requires `dotnet workload install android`):
.\build.ps1 CompileAndroid
```

- SDK version is pinned in [`global.json`](global.json).

## Architecture & Invariants

- **Core + thin hosts** ([ADR-0001](docs/adr/0001-core-thin-hosts.md)): `src/Mailtide.Core/` is strictly UI-agnostic (store, sync, IMAP/SMTP, OAuth orchestration); `src/Mailtide.UI/` holds shared Avalonia UI; `Mailtide.Desktop/` and `Mailtide.Android/` are thin platform hosts.
- **Storage & security**: Local offline storage uses EF Core + SQLite with versioned migrations ([ADR-0002](docs/adr/0002-ef-core-sqlite-store.md), [ADR-0004](docs/adr/0004-versioned-store-migrations.md)). Credentials must only use OS-backed secure storage (Windows DPAPI, Linux libsecret, Android Keystore — never plaintext fallback, [ADR-0003](docs/adr/0003-secure-storage-apis.md)).
- **Domain vocabulary**: Follow [`CONTEXT.md`](CONTEXT.md) (`Account`, `Person`, `Message`, `Mailbox`, `Unified Inbox`, `Outbox`, `Credential`).

## Docs & Agent Config

- Domain glossary: [`CONTEXT.md`](CONTEXT.md)
- Maintainer docs: [`docs/ROADMAP.md`](docs/ROADMAP.md), [`docs/adr/`](docs/adr/)
- Issue routing & triage labels: [`docs/agents/`](docs/agents/)

## Conventions

- Commit messages, issues, and PR text: **English**.
- Releases are built and published on `v*` tags via `.github/workflows/release.yml`; do not commit, push, tag, or bump versions unless explicitly requested.
