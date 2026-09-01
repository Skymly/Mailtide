# Mailtide

A personal, multi-account, offline-first email client for **Windows**, **Linux**, and **Android**.

Built with **.NET 10** and **Avalonia 12** (Fluent theme). Core mail logic stays UI-agnostic; thin Desktop and Android hosts provide lifecycle, secure storage, and OAuth system-browser flows.

[![CI](https://github.com/Skymly/Mailtide/actions/workflows/ci.yml/badge.svg)](https://github.com/Skymly/Mailtide/actions/workflows/ci.yml)

## Who it is for

People who keep several personal mail Accounts on one device and need mail to stay readable when the network drops. Mailtide is a local client (IMAP/SMTP), not a webmail wrapper and not an enterprise / Entra work-account product.

**v1 platforms:** Windows · Linux (Ubuntu 24.04 x64 baseline) · Android  
**Not in v1:** macOS · iOS · Microsoft Store / Play Store / Flatpak / deb as distribution channels

## Status

Current product is **0.9** (move a Mailbox reply thread's Messages to another Mailbox). GitHub Release assets are still [v0.1.1](https://github.com/Skymly/Mailtide/releases/tag/v0.1.1) until a human cuts `v0.2.0`.

Current target: **0.10** — Message move, Move-to-Trash, and Restore-from-Trash share one Core relocate ritual ([plan](docs/plans/0.10.md)).

Build from source below when developing. Local `dotnet run` still needs OAuth env vars unless a Release pack baked the public client IDs.

## Capabilities

| Area | What ships |
|------|------------|
| Accounts | Google (OAuth), Microsoft consumer / Outlook.com (OAuth), QQ Mail (预设 + 授权码), manual IMAP/SMTP + password / app password |
| Offline | Install-wide EF Core + SQLite store; attachment blobs on disk; read synced Messages without network |
| Sync | In-process sync engine; per-Account parallel sync; drafts → Outbox → SMTP |
| UI | Unified Inbox view (reply-thread groups), per-Mailbox browse (reply-thread groups), HTML Message view, compose with optional HTML, local search (`is:unread` / `is:flagged` + text), move a Message or Mailbox reply thread to another Mailbox, create a Mailbox, rename a Mailbox, delete a Mailbox |
| Security | Credentials only via OS-backed secure storage (Windows DPAPI, Linux libsecret, Android Keystore) — no plaintext fallback |
| Updates | Desktop checks GitHub Releases (private repo: set `MAILTIDE_GITHUB_TOKEN`); Android updates by installing a newer APK from Releases |

Domain vocabulary (Account, Person, Message, Mailbox, Unified Inbox, Outbox, Credential) is defined in [`CONTEXT.md`](CONTEXT.md).

## Repository layout

```
├── src/
│   ├── Mailtide.Core/       # Store, sync, IMAP/SMTP, Auth orchestration (no UI)
│   ├── Mailtide.UI/         # Shared Avalonia UI (presentation + intents)
│   ├── Mailtide.Desktop/    # Windows / Linux host
│   └── Mailtide.Android/    # Android host
├── tests/                   # MSTest + Microsoft.Testing.Platform
├── build/                   # NUKE build project
├── packaging/               # Windows Inno Setup · Linux AppImage scaffolding
├── docs/adr/                # Architecture decision records
└── Mailtide.slnx
```

See [ADR-0001](docs/adr/0001-core-thin-hosts.md) (Core + thin hosts), [ADR-0002](docs/adr/0002-ef-core-sqlite-store.md) (SQLite store), [ADR-0003](docs/adr/0003-secure-storage-apis.md) (secure storage).

## Prerequisites

- **.NET SDK** matching [`global.json`](global.json) (currently `10.0.302`, `rollForward: latestFeature`)
- **Android workload** when building the Android host or running the full Nuke `Test` / `Compile` pipeline:

  ```bash
  dotnet workload install android
  ```

- **Linux Desktop credentials:** a working Freedesktop Secret Service (libsecret) for Credential storage
- **Optional — OAuth Account types:** public OAuth client IDs (see [OAuth setup](#oauth-setup) below). QQ Mail and manual IMAP/SMTP do not need these.

## Clone, build, test

```bash
git clone https://github.com/Skymly/Mailtide.git
cd Mailtide
```

Nuke entrypoints (preferred; same path CI uses):

```bash
# Linux / bash
./build.sh Test

# Windows
.\build.ps1 Test
# or: build.cmd Test
```
Useful targets:

| Target | Purpose |
|--------|---------|
| `Restore` / `Compile` / `Test` | Restore, build, run test projects (default target is `Test`) |
| `CompileAndroid` | Build the Android host only |
| `PublishDesktopWindows` / `PackWindowsInstaller` | Windows self-contained publish + Inno Setup installer (Windows only) |
| `PublishDesktopLinux` / `PackAppImage` | Linux self-contained publish + AppImage (Linux only) |
| `PublishAndroidApk` | Sideload APK under `artifacts/release/` |
| `Pack` / `Release` | Aggregate release artifacts / upload via `gh` |

Pass version for packaging, e.g. `./build.sh PackAppImage --Version 0.1.0`.

You can also open `Mailtide.slnx` in your IDE, or restore/build individual projects with `dotnet`.

## Run (Desktop)

```bash
# Optional OAuth (Google / Microsoft consumer Accounts)
export MAILTIDE_GOOGLE_OAUTH_CLIENT_ID="your-google-client-id.apps.googleusercontent.com"
export MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID="your-microsoft-public-client-id"

dotnet run --project src/Mailtide.Desktop
```

On Windows PowerShell:

```powershell
$env:MAILTIDE_GOOGLE_OAUTH_CLIENT_ID = "your-google-client-id.apps.googleusercontent.com"
$env:MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID = "your-microsoft-public-client-id"
dotnet run --project src/Mailtide.Desktop
```

App data lives under the OS local application-data folder, subdirectory `Mailtide`.

## Run / deploy (Android)

```bash
# Bake public OAuth client IDs into the APK process environment at build time
dotnet publish src/Mailtide.Android/Mailtide.Android.csproj \
  -c Release -f net10.0-android \
  -p:MAILTIDE_GOOGLE_OAUTH_CLIENT_ID=your-google-client-id.apps.googleusercontent.com \
  -p:MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID=your-microsoft-public-client-id
```

Or use Nuke: `./build.sh PublishAndroidApk --Version 0.1.0`  
Release signing uses `ANDROID_KEYSTORE_PATH`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`, and `ANDROID_KEY_PASSWORD` when set; otherwise publish uses default/debug signing for sideload.

## OAuth setup

Desktop and Android read **public** OAuth client IDs from the environment (no client secret in the app):

| Variable | Used for |
|----------|----------|
| `MAILTIDE_GOOGLE_OAUTH_CLIENT_ID` | Google / Gmail OAuth |
| `MAILTIDE_MICROSOFT_OAUTH_CLIENT_ID` | Microsoft consumer (Outlook.com / Hotmail / Live) — not Entra work/school |
| `MAILTIDE_GITHUB_TOKEN` | Desktop-only: optional PAT so the GitHub Releases update check works on a private repo. Never commit this token. Android has no in-app updater. |

Register a public / native client with a loopback (Desktop) or custom-scheme / intent (Android) redirect that matches the Host OAuth client. Without these variables, Google and Microsoft Account add flows fail at authorize time; QQ Mail and manual IMAP/SMTP still work.

## Distribution

Install from [GitHub Releases](https://github.com/Skymly/Mailtide/releases):

- Windows: `Mailtide-*-win-x64-setup.exe` (Inno Setup)
- Linux: `Mailtide-*-linux-x64.AppImage`
- Android: `Mailtide-*-android.apk`

Tag pushes / the Release workflow drive packaging (see [`.github/workflows/release.yml`](.github/workflows/release.yml)).

## Docs for contributors & agents

| Doc | Role |
|-----|------|
| [`CONTEXT.md`](CONTEXT.md) | Product one-liner + domain glossary |
| [`docs/adr/`](docs/adr/) | Architecture decisions |
| [`AGENTS.md`](AGENTS.md) | Agent entry: issues, triage labels, domain docs |
| [`docs/agents/`](docs/agents/) | Issue tracker, triage vocabulary, domain-doc layout |

Issues live in GitHub Issues. Prefer the glossary terms from `CONTEXT.md` in tickets and PRs.
