# Dependency Native AOT / trimming surface (IMAP, OAuth, store, secrets)

Date: 2026-08-05  
Ticket: 03 — candidate dependency AOT surface  
Sources: primary only (Microsoft Learn / .NET Blog, NuGet package docs, first-party GitHub READMEs / issues / PRs / changelogs)

## Executive summary

Native AOT is a **publish-time** constraint: no runtime IL emit, aggressive trimming, and native interop that must be visible to the compiler ([Microsoft Learn — Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/); [limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment)). Library authors signal intent with `IsAotCompatible` (enables trim/AOT analyzers); absence of that metadata is **not** proof of incompatibility ([IL3058](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3058); [.NET Blog — creating AOT-compatible libraries](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/)).

For Mailtide-shaped concerns, primary sources currently point to:

| Area | Stronger AOT story | Weaker / experimental / avoid |
| --- | --- | --- |
| IMAP/SMTP | **MailKitLite + MimeKitLite** | Full **MailKit/MimeKit** (crypto/SQLite cert DB); **System.Net.Mail.SmtpClient** (not recommended for new work) |
| OAuth/OIDC clients | **Duende.IdentityModel.OidcClient** (marked AOT) | **MSAL.NET** (marked for net8+, broker/NativeInterop caution); **Google.Apis.Auth** (unannotated, AOT errors reported) |
| Local persistence | **ADO.NET + Microsoft.Data.Sqlite** with careful SQLitePCLRaw init; **Dapper.AOT** | Vanilla **Dapper**; **EF Core NativeAOT** (experimental); **LiteDB 5.x** |
| OS secrets | Thin **P/Invoke** to OS keyrings (interop model is supported) | Unannotated wrappers; process-shell helpers; Data Protection as a stand-in for OS keyrings |

This inventory tags candidate **classes** and notable packages **usable / caution / avoid** for Native AOT. It does **not** pick Mailtide’s concrete packages.

---

## Tag legend

| Tag | Meaning in this inventory |
| --- | --- |
| **usable** | Primary sources state AOT/trim compatibility (or maintainers confirm a specific package variant works under AOT) with a documented path. |
| **caution** | Partial, experimental, version-/feature-gated, needs special init/linking, or structurally plausible (e.g. P/Invoke) but not cleanly annotated / still open issues. |
| **avoid** | Primary sources describe intrinsic AOT incompatibility, recommend against the API for new development, or maintainers say AOT is unsupported for that package. |

---

## Inventory

### 0. Baseline: what Native AOT forbids / requires

| Item | Tag | Notes + citations |
| --- | --- | --- |
| Dynamic assembly load / `Reflection.Emit` / runtime codegen | **avoid** (pattern) | Explicit Native AOT limitations: no `Assembly.LoadFile`, no `System.Reflection.Emit`, trimming + single-file implications ([Microsoft Learn — limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment)). |
| Libraries with `IsAotCompatible=true` (net8+) | **usable** (signal) | Sets trim + AOT analyzers; assembly metadata for consumers ([Microsoft Learn — AOT-compatibility analyzers](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#aot-compatibility-analyzers); [.NET Blog](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/)). |
| Unannotated but “probably fine” deps | **caution** | `VerifyReferenceAotCompatibility` may warn IL3058 even when runtime works; metadata only reliably present for packages built with recent SDKs ([IL3058](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3058)). |
| P/Invoke / native libraries | **caution** → can be **usable** | Supported; lazy bind by default; `DirectPInvoke` + `NativeLibrary` for startup bind / static link ([Microsoft Learn — Native AOT interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)). |

---

### 1. IMAP / SMTP / MIME

| Candidate | Tag | What primary sources say |
| --- | --- | --- |
| **MailKitLite + MimeKitLite** | **usable** | Maintainer (jstedfast): AOT is supported **only** with the Lite packages; MimeKitLite is “completely AOT compatible”; reporters used MailKitLite successfully under AOT on iOS (and Android after unrelated SSL revocation config) ([MailKit #1844](https://github.com/jstedfast/MailKit/issues/1844)). NuGet describes MimeKitLite / MailKitLite as stripped / mobile-oriented builds dropping crypto ([MimeKitLite NuGet](https://www.nuget.org/packages/MimeKitLite/); [MailKitLite NuGet](https://www.nuget.org/packages/MailKitLite/)). |
| **MailKit + MimeKit** (full) | **avoid** (for AOT publish) / **caution** (feature trade-off) | Same issue: AOT problems are in MimeKit cryptography, especially `SqliteCertificateDatabase`; maintainer does not see a path to fixing full MimeKit; “Only if you use the MimeKitLite and MailKitLite packages” ([MailKit #1844](https://github.com/jstedfast/MailKit/issues/1844)). MimeKit release notes: MimeKitLite AOT-compatible; MimeKit still has SQLite S/MIME certificate DB issues ([MimeKit 4.9.0 NuGet release notes](https://www.nuget.org/packages/MimeKit/4.9.0)). |
| **System.Net.Mail.SmtpClient** | **avoid** (for new development) | Microsoft: not recommended for new development; use MailKit or other libraries; limited modern protocol support ([SmtpClient remarks](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient)). Not an IMAP option; SMTP-only and discouraged regardless of AOT. |
| Built-in .NET IMAP API | **avoid** (class) | No first-party IMAP client in BCL comparable to MailKit; Microsoft points SMTP users at MailKit ([SmtpClient remarks](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient); [MailKit README](https://github.com/jstedfast/MailKit)). |

**Class takeaway:** For AOT, treat “full MIME crypto stack” and “IMAP/SMTP transport” as separate packages. Lite variants are the documented AOT path; full MimeKit is the documented blocker when S/MIME cert DB / reflection-heavy crypto stays in the graph.

---

### 2. OAuth2 / OIDC client libraries

| Candidate | Tag | What primary sources say |
| --- | --- | --- |
| **Duende.IdentityModel.OidcClient** (+ **Duende.IdentityModel**) | **usable** | Certified OIDC RP for native apps (RFC 8252); NuGet/docs describe desktop/mobile native clients ([Duende docs](https://docs.duendesoftware.com/identitymodel-oidcclient/); [NuGet](https://www.nuget.org/packages/Duende.IdentityModel.OidcClient/)). FOSS repo closed “Native AOT support” by merging “Mark IdentityModel and OidcClient AOT compatible and fix AOT trimming warning” ([foss #67](https://github.com/DuendeSoftware/foss/issues/67); [foss #71](https://github.com/DuendeSoftware/foss/pull/71); precursor [IdentityModel.OidcClient #451](https://github.com/IdentityModel/IdentityModel.OidcClient/pull/451)). |
| **MSAL.NET** (`Microsoft.Identity.Client`) | **caution** | Projects marked AOT compatible for net8 via merged PR ([#5458](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/pull/5458); feature request [#5248](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/5248)). Changelog: NativeAOT config-binder fix; Windows broker NativeAOT fix ([CHANGELOG](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/main/CHANGELOG.md); [#4424](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/4424)). Remaining risk: **Broker / `Microsoft.Identity.Client.NativeInterop`** single-file / AOT path historically broken and still discussed ([#5226](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/5226); [#4424](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/4424)). Older history: IL trimming gaps ([#3407](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/3407)); mobile TFM Newtonsoft→STJ migration ([#4518](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/4518)). |
| **Google.Apis.Auth** / google-api-dotnet-client | **caution** → lean **avoid** until fixed | Open first-party issue: AOT publish produces a wall of errors; request to set `IsTrimmable` / `IsAotCompatible` and fix/annotate ([googleapis/google-api-dotnet-client #3022](https://github.com/googleapis/google-api-dotnet-client/issues/3022)). |
| **OpenIddict** (server/core family) | **caution** (mostly out of client scope) | Assemblies marked `IsAotCompatible` for **.NET 9+** TFMs after removing reflection store resolvers ([openiddict-core #2278](https://github.com/openiddict/openiddict-core/pull/2278); [Directory.Build.targets](https://github.com/openiddict/openiddict-core/blob/dev/Directory.Build.targets)). Relevant as an OIDC ecosystem that invested in AOT; not a drop-in native-app OAuth client like OidcClient/MSAL. |
| Hand-rolled OAuth with **System.Text.Json** source-gen | **usable** (pattern) | Aligns with AOT guidance (prefer source-generated serialization over unbounded reflection) ([.NET Blog — AOT-compatible libraries](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/)). |
| Clients depending on **runtime IL emit** or unannotated **Newtonsoft.Json**-heavy graphs | **caution** / **avoid** | Same blog: designs that require runtime codegen are opposed to Native AOT; Newtonsoft historically noisy under trim/AOT (MSAL’s own migration notes in [#4518](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/4518)). |

**Class takeaway:** Prefer OIDC client libraries that have **closed AOT/trimming work** and `IsAotCompatible` (Duende OidcClient). Treat Microsoft identity broker interop and Google’s Auth stack as higher proof-cost even when core MSAL is marked compatible.

---

### 3. Local persistence (SQLite / data access)

| Candidate | Tag | What primary sources say |
| --- | --- | --- |
| **Microsoft.Data.Sqlite** (ADO.NET) | **caution** | Official overview: lightweight ADO.NET provider; EF Core SQLite sits on top ([Microsoft Learn — Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)). First-party tracking issue still open: NativeAOT publish can fail to find `e_sqlite3`; wants `IsAotCompatible` + warning-free CI sample ([efcore #36068](https://github.com/dotnet/efcore/issues/36068)). Earlier trim/AOT work discussed reflection init of `Batteries_V2.Init` ([efcore #29725](https://github.com/dotnet/efcore/issues/29725)). |
| **SQLitePCLRaw** / `bundle_e_sqlite3` / `SourceGear.sqlite3` | **caution** | README: initialize with `SQLitePCL.Batteries_V2.Init()`; native `e_sqlite3` supplied via bundle/config packages ([SQLitePCL.raw README](https://github.com/ericsink/SQLitePCL.raw/blob/main/README.md)). Native AOT static-library packaging **not** shipped; maintainer acknowledges interest ([SQLitePCL.raw #657](https://github.com/ericsink/SQLitePCL.raw/issues/657)). Dynamic native load + AOT/single-file friction also reported ([#656](https://github.com/ericsink/SQLitePCL.raw/issues/656)). Static link path uses Microsoft’s `DirectPInvoke` + `NativeLibrary` model ([Native AOT interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop); [#657](https://github.com/ericsink/SQLitePCL.raw/issues/657)). |
| **EF Core NativeAOT + precompiled queries** | **caution** (experimental — **avoid** for production per docs) | Microsoft: “highly experimental… not yet suited for production use”; publish still emits trim/AOT warnings; no dynamic LINQ composition; provider must support precompiled queries ([EF Core — NativeAOT and precompiled queries](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)). |
| **Dapper** (vanilla) | **avoid** | .NET Blog (primary): core design generates dynamic IL at runtime — “completely opposed” to Native AOT; cannot be modified into compatibility ([Creating AOT-compatible libraries — Dapper section](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/)). |
| **Dapper.AOT** | **usable** (with process caveats) | Official DapperAOT docs: build-time interceptors replace reflection/ref-emit; requires `[DapperAot]` opt-in + interceptors namespace; not all Dapper APIs supported ([Getting started](https://aot.dapperlib.dev/gettingstarted.html); [Dapper.AOT site](https://aot.dapperlib.dev/)). Open issue: ILC may still scan base `Dapper.dll` and fail `PublishAot` even when interceptors replace calls ([DapperAOT #168](https://github.com/DapperLib/DapperAOT/issues/168)). |
| **LiteDB 5.x** | **avoid** | Maintainers/community: AOT not supported in current line (LINQ expression compile / reflection); publish produces IL2104/IL3053; v6 plans discussed but reflection still cited as blocker ([LiteDB #2338](https://github.com/litedb-org/LiteDB/issues/2338); [#2623](https://github.com/litedb-org/LiteDB/issues/2623); iOS emit crash [#2082](https://github.com/litedb-org/LiteDB/issues/2082)). |
| **Community LiteDB AOT wrappers** | **caution** | Community source-generator layers claim AOT by trimming reflection mappers; not first-party LiteDB ([Community-LiteDb-AOT README](https://github.com/mrdevrobot/Community-LiteDb-AOT); referenced from [#2623](https://github.com/litedb-org/LiteDB/issues/2623)). |

**Class takeaway:** The durable AOT-friendly *shape* is **SQL text + ADO.NET (Sqlite) + optional source-generated micro-ORM**, not reflection ORMs. EF Core’s AOT story exists but is explicitly experimental. SQLite native binary packaging remains the sharp edge even when managed code is careful.

---

### 4. OS secret storage wrappers

| Candidate | Tag | What primary sources say |
| --- | --- | --- |
| **Direct P/Invoke** to Windows Credential Manager / macOS Keychain / Linux libsecret | **usable** (mechanism) | Native AOT supports P/Invoke; optional `DirectPInvoke` / static `NativeLibrary` linking ([Microsoft Learn — interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)). This is the AOT-compatible *mechanism* class for OS secrets. |
| **Windows Credential Manager wrappers** (e.g. AdysTech.CredentialManager, Meziantou.Framework.Win32.CredentialManager) | **caution** | Documented as P/Invoke to `CredWrite`/`CredRead`/etc.; Windows-only ([AdysTech README](https://github.com/AdysTech/CredentialManager/blob/master/README.md); [Meziantou csproj](https://github.com/meziantou/Meziantou.Framework/blob/master/src/Meziantou.Framework.Win32.CredentialManager/Meziantou.Framework.Win32.CredentialManager.csproj)). No prominent `IsAotCompatible` claim found in those primary pages — treat as unannotated P/Invoke (likely workable, must be proven under `PublishAot`). |
| **Cross-platform keyring facades** (e.g. libraries wrapping CredMan + Keychain + libsecret) | **caution** | Example: CredentialCache documents native APIs per OS and Linux `libsecret` + Secret Service requirements ([ktsu-dev/CredentialCache README](https://github.com/ktsu-dev/CredentialCache)). Primary sources emphasize platform deps, not AOT analyzers — classify as unproven AOT until `PublishAot` is exercised. |
| **Process-shell / docker-credential-helper style stores** | **caution** → lean **avoid** under single-file AOT | Pattern shells out to helper executables bundled beside the app ([pandabytes/native-credential-store README](https://github.com/pandabytes/native-credential-store)). Conflicts with Native AOT’s single-file / no-dynamic-load posture unless helpers are carefully deployed as external native siblings ([Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment)). |
| **ASP.NET Core Data Protection** as “secret storage” | **caution** (wrong abstraction + AOT cost) | Not an OS keyring. ASP.NET Core Native AOT matrix: many auth features unsupported; Data Protection historically pulled into auth graphs and discussed as AOT/trim size problem ([ASP.NET Core Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot); [aspnetcore #47410](https://github.com/dotnet/aspnetcore/issues/47410)). |

**Class takeaway:** For AOT, prefer **thin, explicit P/Invoke** (or generated bindings) to the OS secret service over reflection-heavy or helper-process abstractions. Expect per-OS packaging (e.g. `libsecret` on Linux) independent of managed AOT annotations.

---

## Implications (no final picks)

1. **AOT splits “popular package” from “Lite / AOT variant.”** MailKit’s maintainer statement makes the Lite packages the only endorsed AOT path; full MimeKit remains in the graph only if S/MIME cert-DB crypto is accepted as non-AOT.
2. **OAuth choice is as much about broker/native interop as about protocol.** Duende OidcClient has completed an AOT-compat pass; MSAL’s core marking coexists with broker/NativeInterop caution; Google Auth still lacks a clean AOT story in first-party tracking.
3. **Local store AOT risk is dual:** managed reflection/ORM design **and** native SQLite deployment. ADO.NET Sqlite is the Microsoft-owned data API, but `#36068` / SQLitePCLRaw native packaging mean “it compiles” ≠ “it runs warning-free as Native AOT.”
4. **EF Core and vanilla Dapper are different kinds of “no”:** EF is experimental/officially not production-ready for NativeAOT; Dapper’s runtime IL emit is categorically incompatible — Dapper.AOT is the documented redesign.
5. **Secret storage AOT risk is mostly packaging and annotation, not the OS APIs.** P/Invoke is in-bounds; unannotated NuGet wrappers and helper-exe designs need proof under `PublishAot` on Windows/Linux/Android separately.
6. **`IsAotCompatible` is necessary signal, not sufficient proof.** Microsoft notes older packages may lack metadata even when analyzers were enabled; runtime publish tests remain the gate ([IL3058](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3058)).

---

## Open unknowns

- Exact **feature gap** between MailKitLite and MailKit for XOAUTH2 / IDLE / CONDSTORE / Gmail extensions under current NuGet versions (Lite is AOT-ok; confirm protocol surface for multi-provider IMAP).
- Whether **MSAL public-client + system browser** (no WAM broker) is clean under Native AOT on Windows/Linux/Android at current package versions after `#5458`.
- Whether **Microsoft.Data.Sqlite** gains official `IsAotCompatible` + CI-proven NativeAOT samples before Mailtide locks a store ADR (`efcore #36068` still open as of sources reviewed).
- **Android** Native AOT is still called out as experimental / limited Java interop in Microsoft’s platform table — impacts how hard to weight desktop AOT proofs vs Android ([Native AOT platform table](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#platformarchitecture-restrictions)).
- Per-wrapper **PublishAot** results for candidate secret-store libraries (none of the surveyed READMEs published a first-party AOT guarantee).
- Whether **Dapper.AOT** + Sqlite can publish warning-free without stub packages (`DapperAOT #168`).
- Gmail OAuth without Google.Apis.Auth (OIDC/OAuth endpoints via a generic client) — protocol feasibility is out of this ticket’s AOT scope but interacts with the Google Auth **caution/avoid** tag.

---

## Source index (primary)

- [Native AOT deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Native code interop with Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop)
- [IL3058](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3058)
- [.NET Blog: Creating AOT-compatible libraries](https://devblogs.microsoft.com/dotnet/creating-aot-compatible-libraries/)
- [MailKit #1844 — AOT / MailKitLite](https://github.com/jstedfast/MailKit/issues/1844)
- [MimeKit / MimeKitLite NuGet](https://www.nuget.org/packages/MimeKitLite/)
- [SmtpClient — not recommended](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient)
- [Duende IdentityModel OidcClient docs](https://docs.duendesoftware.com/identitymodel-oidcclient/)
- [DuendeSoftware/foss #67 / #71](https://github.com/DuendeSoftware/foss/issues/67)
- [MSAL #5248 / #5458 / #4424 / #5226 / CHANGELOG](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet)
- [google-api-dotnet-client #3022](https://github.com/googleapis/google-api-dotnet-client/issues/3022)
- [Microsoft.Data.Sqlite overview](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/)
- [efcore #36068 / #29725](https://github.com/dotnet/efcore/issues/36068)
- [EF Core NativeAOT + precompiled queries](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries)
- [SQLitePCL.raw README / #657](https://github.com/ericsink/SQLitePCL.raw)
- [Dapper.AOT getting started](https://aot.dapperlib.dev/gettingstarted.html)
- [LiteDB #2338 / #2623](https://github.com/litedb-org/LiteDB/issues/2338)
- [ASP.NET Core Native AOT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/native-aot)
