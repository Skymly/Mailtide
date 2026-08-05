# Avalonia 12 on Android with .NET 10 — Native AOT / trimming / publish mode

**Date:** 2026-08-05  
**Ticket:** 02  
**Scope:** Primary sources only (Avalonia Docs / Avalonia GitHub + templates / Microsoft Learn .NET & .NET for Android).  
**Non-goal:** Stack decision. Implications below are factual consequences of the cited docs, not a recommendation.

---

## Executive summary

Avalonia 12 officially supports Android on **.NET 10 only**, and the same Avalonia + .NET shared UI stack can target Android via a separate `net*-android` head project (xplat pattern). Official publish docs for Avalonia on Android describe **.NET for Android APK/AAB publishing** with **`PublishTrimmed` defaulting to `true` on Release** — they do **not** document desktop-style **`PublishAot` / Native AOT** as the Android publish path. Microsoft marks **Native AOT on Android** (`PublishAot=true`) as **experimental / not suitable for production**, with the platform table noting **“no built-in Java interop.”** The production-oriented Android compilation model in Microsoft’s mobile docs is **Mono AOT** (`RunAOTCompilation`, Release default) plus **partial trimming**, which is a different mechanism from Native AOT.

---

## Cited findings

### 1. Avalonia 12 requires .NET 10 for Android

Avalonia 12 dropped .NET Framework / .NET Standard; desktop can use .NET 8+, but **Android and iOS require .NET 10** to match Microsoft’s underlying mobile SDK support.

> “If your project targets Android or iOS, only .NET 10 is supported. This is to match the support Microsoft provides for the underlying .NET SDK.”  
> — [Breaking changes in Avalonia 12](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)

Supported-platforms table restates the same minimum and notes Microsoft removed .NET 8 mobile workloads from the .NET 10 SDK:

> “Minimum .NET version: .NET 10.0. … Microsoft removed .NET 8 mobile workloads from the .NET 10 SDK, so .NET 8 Android builds are not available when using the .NET 10 SDK.”  
> — [Supported platforms — Android](https://docs.avaloniaui.net/docs/supported-platforms)

Android 16 (API 36) is listed as **Tier 1** (ARM64, x64).

### 2. Same Avalonia UI stack can include Android; bootstrap is platform-specific

Avalonia documents a shared + per-platform project layout as the default xplat approach (shared library + Desktop / Android / iOS / Browser heads). Android uses `*-android` TFMs and Java/Android entry types.

> Default Avalonia.Xplat template: Shared (`net8.0` in the older guide text), Desktop, Android (`net8.0-android` in that guide), iOS, Browser.  
> — [Platform-specific .NET](https://docs.avaloniaui.net/docs/platform-specific-guides/dotnet)

Avalonia 12 changed Android app initialization (Java/`Application` attribute + non-generic activity):

1. Main activity inherits non-generic `AvaloniaMainActivity`
2. New `[Android.App.Application]` type deriving from `AvaloniaAndroidApplication<TApp>`
3. Lifetime is `IActivityApplicationLifetime` with `MainViewFactory` (not a single `MainView`)

— [Breaking changes in Avalonia 12 — Android](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)

Official templates still ship an Android head referencing `Avalonia.Android` and setting `AndroidEnableProfiledAot` to `false`; they do **not** set `PublishAot`.

```xml
<AndroidEnableProfiledAot>false</AndroidEnableProfiledAot>
```

— [Avalonia.Templates `AvaloniaTest.Android.csproj`](https://github.com/AvaloniaUI/Avalonia.Templates/blob/main/templates/csharp/xplat/AvaloniaTest.Android/AvaloniaTest.Android.csproj)

Latest Avalonia GitHub release at research time: **12.1.1** (2026-07-29).  
— [Avalonia releases](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.1)

### 3. Avalonia’s documented Android publish mode ≠ desktop Native AOT publish

Avalonia’s Android deployment guide publishes with `dotnet publish` to **APK/AAB**, signing via Android keystore properties. The only trimming/AOT-related property listed in that page’s build-properties table is:

| Property | Description |
| --- | --- |
| `PublishTrimmed` | Whether to trim unused code. **Default: `true` for release builds.** |

There is **no** `PublishAot`, `RunAOTCompilation`, or Native AOT section on that page. Example TFM in the published guide is still `net9.0-android` (see open unknowns).

— [Avalonia Docs — Deployment / Android](https://docs.avaloniaui.net/docs/deployment/android)  
— Source: [avalonia-docs `docs/deployment/android.md`](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/deployment/android.md)

### 4. Avalonia Native AOT docs target `PublishAot` and defer platform matrix to Microsoft

Avalonia’s Native AOT how-to sets:

- `PublishAot=true` on the main executable
- `IsAotCompatible=true` on projects/libraries
- Notes `BuiltInComInteropSupport=false` was necessary **before Avalonia 12.0**

Publish examples use desktop RIDs (e.g. `osx-arm64`). Platform support is explicitly deferred:

> “For platform support, refer to Platform/architecture restrictions.”  
> — [Avalonia Docs — Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot) (links to Microsoft Native AOT docs)

Avalonia-specific AOT/trim guidance on that page: compiled XAML/bindings, avoid dynamic XAML, trimmer roots for reflection, third-party control compatibility limits.

### 5. Microsoft: Native AOT on Android is experimental; Java interop caveat

Microsoft’s Native AOT platform table (.NET 9+ tab):

| Platform | Architectures | Notes |
| --- | --- | --- |
| Android | x64, Arm64, Arm | **Experimental, no built-in Java interop** |

— [Native AOT deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)

.NET for Android warning **XA1040**:

> “The NativeAOT runtime on Android is an experimental feature and **not yet suitable for production use**.”  
> Enabled via `$(PublishAot)=true`. Supported runtimes listed there: **CoreCLR (default)** and **MonoVM** via `$(UseMonoRuntime)=true`. Silencing requires removing `PublishAot` or setting `EnablePreviewFeatures=true`.  
> — [XA1040](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1040)

.NET MAUI runtimes/compilation doc (applies to the same underlying Android workload concepts) distinguishes:

| Mechanism | Property | Android status (as documented) |
| --- | --- | --- |
| **Mono AOT** | `RunAOTCompilation` | Default compilation for Mono Android **Release** |
| **NativeAOT** | `PublishAot` | **Android experimental**; full trim; no JIT/interpreter |
| **CoreCLR** | `UseMonoRuntime=false` | Experimental on Android in .NET 10; default Android Release runtime becomes CoreCLR in **.NET 11** per that page |

> “Mono AOT is not the same as NativeAOT. With Mono AOT, your app still includes the Mono runtime…”  
> — [Runtimes and compilation in .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/deployment/runtimes-compilation?view=net-maui-10.0)

### 6. Microsoft: Release Android defaults are trim + Mono AOT (not Native AOT)

.NET for Android / migration docs:

- Debug: linker off  
- Release: `PublishTrimmed=true` and `TrimMode=partial` by default  
- Release AOT defaults when unset:

```xml
<RunAOTCompilation>true</RunAOTCompilation>
<AndroidEnableProfiledAot>true</AndroidEnableProfiledAot>
```

— [Xamarin.Android project migration — Linker / AOT](https://learn.microsoft.com/en-us/dotnet/maui/migration/android-projects?view=net-maui-10.0)

Build property reference:

> `$(RunAOTCompilation)` is **False** by default for Debug and **True** by default for Release.  
> — [.NET for Android build properties — RunAOTCompilation](https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties#runaotcompilation)

Mono AOT requires trimming to be enabled:

> XA1030: `RunAOTCompilation` is only supported when trimming is enabled (`PublishTrimmed=true`).  
> — [XA1030](https://learn.microsoft.com/en-us/dotnet/android/messages/xa1030)

Native AOT implies **full** trimming / static analysis (MAUI trimming & runtimes docs); do not casually set `TrimMode` against Native AOT defaults.

### 7. First-party Avalonia marketing mentions Android + NativeAOT numbers; docs do not make that the Android publish path

Avalonia’s Avalonia 12 blog states Android startup “drops from 1,960ms to 460ms **with NativeAOT**” among other Android performance claims, and positions mobile as first-class in a single codebase.

— [Avalonia 12 blog](https://avaloniaui.net/blog/avalonia-12) (2026-04-07)

That blog claim is **not** mirrored as a how-to on Avalonia’s Android deployment page, which documents trimmed APK/AAB publish without `PublishAot`. Microsoft’s Android Native AOT status remains experimental (findings 5–6). Treat the blog as a first-party performance claim, not as a substitute for Microsoft’s production-support statement.

### 8. Avalonia 12 binding defaults are trim/AOT-friendly on paper

Compiled bindings are enabled by default in Avalonia 12 (`Binding` in XAML → `CompiledBinding`), which Avalonia documents as more performant and build-time checked vs reflection bindings.

— [Breaking changes in Avalonia 12 — Compiled bindings](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)

---

## Implications (facts only — no stack decision)

1. **Shared Avalonia + .NET 10 code can serve Android** under official Avalonia platform support and the documented xplat project shape; Android still needs its own head project, workload (`dotnet workload install android`), and Avalonia 12 Android bootstrap types.
2. **Official docs imply a different default publish/compilation mode for Android than desktop Native AOT.** Desktop Avalonia Native AOT docs use `PublishAot` + RID publish; Android Avalonia docs use .NET for Android `dotnet publish` → APK/AAB with **`PublishTrimmed`**.
3. **“AOT on Android” in Microsoft’s production defaults means Mono AOT (`RunAOTCompilation`), not Native AOT (`PublishAot`).** Those are different runtimes and different restriction sets.
4. **`PublishAot=true` on Android is explicitly experimental** in Microsoft docs (XA1040 + platform table), including a **Java interop** caveat that is material for Avalonia’s `AvaloniaAndroidApplication` / activity model.
5. **Release Android builds should be planned as trim-affected** (`PublishTrimmed` / partial trim by default); Mono AOT and trimming are coupled (XA1030). Avalonia’s compiled-binding default reduces one common reflection risk but does not remove trim/AOT constraints on DI, view locators, or third-party packages.
6. **TFM expectation for Avalonia 12 Android is `net10.0-android`**, even where some Avalonia Android publish examples still show `net9.0-android`.

---

## Open unknowns

1. **Avalonia under experimental Android Native AOT:** No Avalonia Docs page walks through `PublishAot=true` for an Avalonia Android app, or states whether Avalonia’s Java activity/`Application` bootstrap is supported when Microsoft’s Native AOT table still says “no built-in Java interop.”
2. **Blog vs docs gap:** How the Avalonia 12 blog’s Android “with NativeAOT” timings were produced (flags, SDK version, interop surface) is not specified in Avalonia’s Android deployment docs.
3. **Default runtime for a plain Avalonia.Android (.NET for Android) app on .NET 10:** XA1040 lists CoreCLR as default among “supported runtimes,” while the MAUI runtimes page still describes Mono as the default Android runtime for .NET 10 (CoreCLR default Release in .NET 11). Which default an Avalonia Android head actually gets without `UseMonoRuntime` / `PublishAot` overrides needs verification against the installed Android workload, not just prose.
4. **Doc lag:** Avalonia Android publish examples still use `-f net9.0-android` while Avalonia 12 platform rules require .NET 10 for Android.
5. **Template vs Microsoft Release defaults:** Official Avalonia Android template sets `AndroidEnableProfiledAot` to `false`; Microsoft migration docs describe profiled AOT as the unset Release default. Net effect on size/startup for Avalonia templates vs stock .NET Android defaults is not quantified in the cited pages.
6. **Production readiness timeline:** Microsoft GitHub / SDK work on Android Native AOT + Java typemaps continues (e.g. runtime/android issues tracked upstream); no cited Learn page declares Android Native AOT production-ready as of this research date.
