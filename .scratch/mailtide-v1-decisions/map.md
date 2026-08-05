# Mailtide v1 — decision map

Label: `wayfinder:map`  
Tracker note: GitHub Issues is configured for this repo, but this agent token cannot create issues/labels (HTTP 403). This effort’s map lives under `.scratch/mailtide-v1-decisions/` until it can be republished to GitHub Issues.

## Destination

A locked decision set ready for `/to-spec` covering v1 of a personal multi-account, offline-first email client on Windows / Linux / Android (no Apple), with IMAP + SMTP as first-class protocols and OAuth2 for major providers hung on top of them, AOT-compatible module seams, and the Avalonia 12 + .NET 10 LTS + C# 14 + Nuke + MSTest v4/MTP working hypothesis validated or revised. **Decisions only — no implementation in this map.**

## Notes

- **Domain docs**: root `CONTEXT.md`; ADRs under `docs/adr/` when a hard-to-reverse trade-off lands.
- **Skills**: `/grilling`, `/domain-modeling`, `/research`; hand off to `/to-spec` when the frontier is clear.
- **Working hypothesis stack** (not yet ADR): Avalonia 12, .NET 10 LTS, C# 14, Nuke Build, MSTest v4 + MTP. Reopen only if AOT research proves it unworkable.
- **Product**: personal multi-account; offline-first local store; not enterprise IT / shared mailboxes / compliance archive.
- **Platforms**: Windows, Linux, Android. Apple (macOS / iOS) out of scope.
- **Protocols**: IMAP + SMTP first-class; Gmail / Outlook.com OAuth2 on IMAP/SMTP. JMAP / vendor-only APIs / EAS are not v1 primary paths.

## Decisions so far

- [Avalonia 12 + .NET 10 Native AOT on Windows/Linux](issues/01-avalonia-desktop-aot.md) — Desktop Native AOT is a documented Avalonia 12 path; Win/Linux RIDs supported by Microsoft; requires compiled bindings/XAML, compile-time DI, no reflection ViewLocator.
- [Avalonia 12 + .NET 10 on Android (AOT / trimming)](issues/02-avalonia-android-aot.md) — Same stack can serve Android via a separate head; official publish path is trimmed APK/AAB + Mono AOT, not desktop-style Native AOT (`PublishAot` experimental on Android).
- [Candidate dependency AOT surface (IMAP, OAuth, local store, secrets)](issues/03-dependency-aot-surface.md) — MailKitLite/MimeKitLite + Duende OidcClient usable; full MailKit/MimeKit and several ORMs avoid/caution; secrets prefer thin P/Invoke over unannotated wrappers.
- [Platform secret storage on Windows / Linux / Android](issues/04-platform-secret-storage.md) — Win Credential Locker/CredMan/DPAPI; Linux Secret Service (+ XDG portal); Android Keystore preferred; abstract secure-store seam; Android Native AOT vs Java Keystore is a hard tension.
- [Offline store responsibilities and partitioning](issues/05-offline-store-responsibilities.md) — One install-wide store partitioned by Account; metadata+bodies in structured store, attachments in blob area; store is local-only — sync engine owns network.
- [Sync engine external contract](issues/06-sync-engine-contract.md) — Per-Account pipeline, Accounts in parallel; self-drive + SyncNow/SendNow; drafts→Outbox→engine; UI sees idle/syncing/error and Outbox queued/sending/failed.

## Not yet specified

- Full-text / search model
- Notifications (especially Android)
- Theme and visual-system detail
- Concrete local DB engine (after AOT dependency inventory)
- Final per-platform secret-store API choice (after secret-storage research + credential-model grilling)
- Sync rate-limits / incremental algorithm detail
- Deep Gmail label interaction design
- Code signing and store-account provisioning (future `task` tickets)
- Nuke / test project skeleton tables (implementation phase)

## Out of scope

- macOS / iOS (Apple platforms)
- Enterprise IT controls, shared mailboxes, compliance archiving
- JMAP, vendor-only mail APIs, or Exchange ActiveSync as the v1 primary path
- Delivering a runnable client inside this map
