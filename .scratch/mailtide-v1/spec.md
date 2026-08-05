# Mailtide v1 — Product Spec

Status: ready-for-agent  
Source: Wayfinder map `.scratch/mailtide-v1-decisions/` (tickets 01–13)  
Glossary: `CONTEXT.md` · ADRs: `docs/adr/0001`–`0003`  
Tracker note: GitHub Issues create is blocked for the agent token (HTTP 403); this file is the published spec until it can be mirrored to a GitHub issue with label `ready-for-agent`.

## Problem Statement

People who use several personal email Accounts need a modern client on Windows, Linux, and Android that works when the network is unreliable. Existing options either push them into a browser tab, lock them to one vendor, skip Linux, or feel dated. They want one install that holds multiple Accounts, keeps Messages available offline, and sends when connectivity returns — without Apple platforms or enterprise IT features.

## Solution

Mailtide is a personal, multi-account, offline-first email client. The Person configures Accounts (Google OAuth, Microsoft consumer OAuth, QQ Mail with 授权码, or manual IMAP/SMTP). Messages and Mailboxes live in a local store; a sync engine fetches and sends in the background. A thin Avalonia UI presents Unified Inbox and per-Mailbox views; Core stays usable without that UI so future CLI/TUI hosts can attach. Ships via GitHub Releases (Windows installer, Linux AppImage on Ubuntu 24.04 x64, Android sideload APK).

## User Stories

1. As a Person, I want to install Mailtide on Windows from GitHub Releases, so that I can use it without a store account.
2. As a Person, I want to install Mailtide on Ubuntu 24.04 via AppImage, so that I can run it on my Linux desktop.
3. As a Person, I want to install Mailtide on Android via a signed APK from GitHub Releases, so that I can use it without Play Store.
4. As a Person, I want desktop builds to notify me when a newer Release exists, so that I can update without hunting for downloads.
5. As a Person on Android, I want to install a newer APK myself from Releases, so that updates stay simple without in-app updaters.
6. As a Person, I want to add a Google (Gmail) Account with OAuth, so that I do not store my Google password in the app.
7. As a Person, I want to add a Microsoft consumer Account (Outlook.com / Hotmail / Live) with OAuth, so that consumer Microsoft mail works without Entra work/school setup.
8. As a Person, I want to add a QQ Mail Account with preset servers and 授权码, so that QQ works without inventing OAuth for Tencent.
9. As a Person, I want to add a manual IMAP/SMTP Account with a password or app password, so that arbitrary providers still work.
10. As a Person, I want each Account to use exactly one primary Credential (OAuth or password), so that auth stays unambiguous.
11. As a Person, I want Credentials stored in OS-backed secure storage on this device only, so that secrets are not plaintext in the app folder and do not roam to other devices.
12. As a Person, I want to remove an Account and have its local data and Credential cleared, so that leaving a provider does not leave secrets behind.
13. As a Person, I want Mailboxes listed per Account, so that I can navigate like a normal IMAP client.
14. As a Person, I want Mailboxes to show optional roles (Inbox, Sent, Drafts, Trash, Junk), so that common folders are easy to find.
15. As a Person using Gmail, I want Gmail’s IMAP folder/label views mapped to Mailboxes, so that I am not forced into a separate Label domain model.
16. As a Person, I want a Unified Inbox view across Accounts, so that I can scan new mail without switching Accounts first.
17. As a Person, I want Unified Inbox to remain a view (not a stored container), so that each Message still belongs to one Account’s Mailbox.
18. As a Person, I want Messages available offline after sync, so that I can read mail without a network.
19. As a Person, I want Message bodies stored locally once fetched, so that reopening a Message does not require the network.
20. As a Person, I want attachments downloaded into a local blob area with metadata in the store, so that large files do not bloat the database.
21. As a Person, I want to open an attachment that is already downloaded while offline, so that files remain usable on a plane.
22. As a Person, I want unread/read and other standard flags reflected locally after sync, so that state matches the server when possible.
23. As a Person, I want to compose a draft that stays local until I send, so that unfinished mail is never submitted early.
24. As a Person, I want Send to move a Message into the Outbox, so that sending is explicit and durable.
25. As a Person, I want the sync engine to be the only consumer of the Outbox, so that send behavior is consistent.
26. As a Person, I want failed Outbox items to remain visible with an error, so that I know what did not send.
27. As a Person, I want to Retry or Discard a failed Outbox item, so that I can recover without retyping everything.
28. As a Person, I want sync to run per Account in parallel, so that one slow Account does not block others.
29. As a Person, I want the engine to sync while the app is in the foreground (and when background is allowed), so that mail stays fresh without manual babysitting.
30. As a Person, I want SyncNow on an Account or Mailbox, so that I can force a refresh.
31. As a Person, I want SendNow to flush Outbox work, so that I can push sends immediately.
32. As a Person, I want to see idle / syncing / error per Account, so that I understand what the client is doing.
33. As a Person, I want auth failures to surface as an Account error that suggests re-login, so that expired OAuth is actionable.
34. As a Person, I want the UI never to speak IMAP/SMTP directly, so that behavior stays consistent across hosts.
35. As a Person, I want the app to work without network for reading already-synced content and managing drafts/Outbox, so that offline-first is real.
36. As a Person, I want one install-wide local store partitioned by Account, so that backup and unified views stay simple.
37. As a Person, I want Desktop and Android to share the same Core behavior, so that Accounts and mail act the same on each device class (aside from host packaging).
38. As a Person who might later use a CLI, I want Core not to depend on Avalonia, so that non-GUI hosts remain possible.
39. As a Person, I want Native AOT on Desktop when it works, so that startup and distribution stay lean.
40. As a Person, I want a non-AOT Desktop build to still be shippable if AOT is not mature, so that releases are not blocked on experimental EF/AOT paths.
41. As a Person on Android, I want a trimmed APK using the Android publish path (not Desktop Native AOT), so that the app matches platform reality.
42. As a developer agent, I want MSTest v4 + MTP unit tests against the Core application surface, so that behavior is locked without UI flakiness.
43. As a developer agent, I want Nuke-based build/CI, so that package and test pipelines are repeatable.
44. As a Person, I do not want Apple (macOS/iOS) support in v1, so that scope stays focused.
45. As a Person, I do not want Entra work/school mail as a first-class OAuth path in v1, so that enterprise complexity stays out.
46. As a Person, I do not want JMAP or vendor-only APIs as the primary sync path in v1, so that IMAP/SMTP remains the common denominator.

## Implementation Decisions

### Architecture (ADR-0001)

- Split into **Core** (no UI), thin **Avalonia UI** (presentation + intents only), and thin **Hosts** (Desktop for Windows/Linux, Android).
- Core contains: domain model, offline store, sync engine, IMAP/SMTP adapters, Auth orchestration (Credential lifecycle, token refresh).
- Hosts provide: DI composition, lifecycle, app-data paths, **secure-storage adapters**, OAuth system-browser / login UI callbacks.
- Sync engine runs **in-process** with the app for v1 (no separate daemon).
- Core must remain hostable by future CLI/TUI (not a v1 product surface).

### Offline store (tickets 05, 12 · ADR-0002)

- One **install-wide** store, logically partitioned by Account.
- Message **metadata and bodies** in structured storage; **attachment bytes** in a co-located filesystem blob area with references in the store.
- Store does **not** schedule network I/O, hold protocol connections, or refresh OAuth.
- Engine: **EF Core + SQLite**.
- AOT: best-effort (prefer precompiled queries / avoid dynamic LINQ for AOT builds); **non-AOT publish allowed** until AOT is mature enough.

### Sync engine (ticket 06)

- One active sync pipeline **per Account**; Accounts run in **parallel**.
- Triggers: engine self-drive + explicit `SyncNow` / `SendNow`.
- Drafts are local only; Send → **Outbox** → engine sole consumer.
- UI-visible state: per-Account idle/syncing/error; Outbox queued/sending/failed; actions SyncNow, Retry, Discard.

### Credentials & providers (tickets 07, 08, 13 · ADR-0003)

- One primary **Credential** per Account: OAuth refresh (+ metadata) **or** password/授权码/app password — never both.
- Auth module owns token obtain/refresh/invalidate; sync consumes access tokens only.
- Secrets are **device/install-bound**; no cloud roaming of secrets; no plaintext fallback.
- Secure storage by Host:
  - Windows: DPAPI `ProtectedData` (CurrentUser) → ciphertext in app data
  - Linux: Secret Service via libsecret (AppImage; not XDG portal)
  - Android: Keystore + encrypted blobs in app-private storage
- First-class providers: Google OAuth, Microsoft consumer OAuth, QQ Mail preset + 授权码; manual IMAP/SMTP always available.

### Domain / navigation (ticket 10)

- **Mailbox-only** domain (no Label entity). Provider label/folder views map to Mailboxes.
- Optional Mailbox **roles** (Inbox, Sent, Drafts, Trash, Junk, …).
- **Unified Inbox** is a UI view over Inbox-role Mailboxes.

### Stack & distribution (map Notes · tickets 01–02, 11)

- Avalonia 12, .NET 10 LTS, C# 14, Nuke, MSTest v4 + MTP.
- Prefer MailKitLite/MimeKitLite and AOT-friendly OAuth clients where research allows; concrete package pins can land at implementation with ADR updates if needed.
- Desktop: prefer Native AOT publish when viable; Android: trimmed APK / Mono AOT path.
- Distribution: GitHub Releases — Windows installer, Linux AppImage (Ubuntu 24.04 x64 baseline), Android sideload APK.
- Desktop self-update from Releases; Android no in-app self-update.
- Microsoft Store / Play Store / Flatpak / deb are not v1 commitments.

### Testing seam (agreed for this spec)

- **Primary seam: Core application surface** (commands/queries/intents used by UI or future CLI).
- Fake IMAP/SMTP and secure-storage ports at that boundary.
- Do not treat Avalonia UI, EF internals, or real OS keyrings as the primary automated suite.

### Deferred to later (fog — not required to start Core vertical slices)

- Full-text search model, notifications, visual theme system, sync rate-limit numbers, deep multi-label UX, store signing/account provisioning tasks, Nuke/test project skeleton tables, CLI/TUI hosts.

## Testing Decisions

- Good tests assert **observable Core behavior** only: given fakes and local store state, when the Person/app issues an intent, then store projections and sync/Outbox/Account statuses match the contract. No tests of private EF mappings, control trees, or IMAP wire bytes unless behind an explicit protocol-port contract test.
- **Modules under test:** Core application surface (Accounts, Credentials via fake secure store, Mailboxes/Messages, drafts/Outbox, sync orchestration with fake protocol ports).
- **Prior art:** none in-repo yet (greenfield). Establish MSTest v4 + MTP patterns at this seam first; Host adapter smoke tests optional and separate.
- Prefer the **single Core seam** over proliferating module-level suites; add lower seams only when Core tests cannot express a failure mode.

## Out of Scope

- macOS / iOS
- Enterprise IT, shared mailboxes, compliance archiving, Entra work/school as first-class OAuth
- JMAP, vendor-only mail APIs, or Exchange ActiveSync as the v1 primary path
- Microsoft Store / Play / Flatpak / deb as v1 channels
- In-app Android self-update
- Shipping CLI/TUI in v1
- Deep Gmail multi-label UX, search productization, rich notification design, and full visual design system (later)

## Further Notes

- Domain vocabulary is mandatory: Person, Account, Message, Mailbox, Unified Inbox, Outbox, Credential (`CONTEXT.md`).
- Wayfinder decision detail lives under `.scratch/mailtide-v1-decisions/`; this spec is the build-facing collapse for `/to-tickets` / `/implement`.
- When GitHub Issues write access is available, mirror this document to an issue and apply `ready-for-agent`.
