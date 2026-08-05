# Avalonia 12 + .NET 10 Native AOT on Windows/Linux

**Date:** 2026-08-05  
**Ticket:** `.scratch/mailtide-v1-decisions/issues/01-avalonia-desktop-aot.md`  
**Scope:** Primary sources only (Avalonia docs/GitHub/blog, Microsoft Learn .NET docs).

## Executive summary

- Avalonia 12.0 shipped 2026-04-07 as a stable NuGet release; docs recommend targeting **.NET 10** (desktop: .NET 8+; Android/iOS: .NET 10 only).
- Avalonia publishes a first-party **Native AOT how-to** for Avalonia apps (desktop startup called out as a benefit). It does **not** mark desktop AOT as experimental; platform eligibility is deferred to Microsoft’s Native AOT RID table.
- For **.NET 9+**, Microsoft lists **Windows** (`x64`, `Arm64`, `x86`) and **Linux** (`x64`, `Arm64`, `Arm`) as supported Native AOT compilation targets (no “Experimental” note, unlike Android).
- Ship path is `PublishAot=true` + `dotnet publish -r <rid> -c Release`. Native AOT **requires trimming**, implies self-contained publish, and **does not support cross-OS** compilation.
- Avalonia-documented AOT constraints center on **compiled bindings / compiled XAML**, **no dynamic XAML load**, **compile-time DI registration**, and avoiding reflection-based ViewLocator / service location. Template `ViewLocator` using `Activator.CreateInstance` is explicitly **not AOT-compatible**.
- Avalonia 12 docs note `BuiltInComInteropSupport=false` was **necessary before Avalonia 12.0** (Windows accessibility used built-in COM). Avalonia later moved Win32 automation to source-generated COM (`GeneratedComInterface`) for .NET 8+ AOT/trim compatibility.
- “Supported” for desktop means: Microsoft supports building Native AOT for those Win/Linux RIDs when native toolchain prerequisites are met; Avalonia documents how to publish Avalonia apps that way, with known trim/AOT limitations and third-party-control caveats—not a guarantee that every dependency or reflection pattern will survive publish.

## Findings

### 1. Avalonia 12 + .NET 10 baseline

| Claim | Source |
| --- | --- |
| Avalonia **12.0.0** is a non-prerelease GitHub release, published **2026-04-07**. | [Avalonia 12.0.0 release](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.0.0) |
| Official blog announces Avalonia 12 the same day; NuGet packages available. | [Avalonia 12 blog](https://avaloniaui.net/blog/avalonia-12) |
| Avalonia 12 dropped .NET Framework / .NET Standard; **only .NET 8+**; **recommended target is .NET 10**. Android/iOS require .NET 10. | [Breaking changes in Avalonia 12](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes) |
| DI guide prerequisites: .NET 8+ SDK, **.NET 10 recommended**. | [Implementing dependency injection](https://docs.avaloniaui.net/docs/app-development/dependency-injection) |
| Avalonia 12 enables **compiled bindings by default** (`AvaloniaUseCompiledBindingsByDefault` is `true`). | [Breaking changes in Avalonia 12 — Compiled bindings](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes) |

### 2. Publish / trim / AOT flags (Avalonia + .NET)

#### Avalonia project flags

From Avalonia’s Native AOT doc ([Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot)):

```xml
<PropertyGroup>
  <!-- Only needed for the main executable project -->
  <PublishAot>true</PublishAot>

  <!-- Add to all projects/libraries in use, to ensure AOT compatibility -->
  <IsAotCompatible>true</IsAotCompatible>

  <!-- Necessary before Avalonia 12.0, was used for accessibility APIs -->
  <BuiltInComInteropSupport>false</BuiltInComInteropSupport>
</PropertyGroup>
```

| Flag / action | Role | Source |
| --- | --- | --- |
| `PublishAot` | Enables Native AOT on publish (exe project). | [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot); [MS Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| `IsAotCompatible` | Marks libraries AOT-compatible; enables trim/single-file/AOT analyzers (`IsTrimmable`, `EnableTrimAnalyzer`, `EnableSingleFileAnalyzer`, `EnableAotAnalyzer`). | [MS Native AOT — AOT-compatibility analyzers](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| `BuiltInComInteropSupport` | Avalonia docs: **necessary before Avalonia 12.0** for accessibility; still shown in the sample PropertyGroup. | [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot) |
| Publish command | `dotnet publish -r <runtime> -c Release` (example: `osx-arm64`; same pattern applies to `win-x64` / `linux-x64`). | [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot) |
| Reflection/trim fallback | `TrimmerRootAssembly` on assemblies that must be preserved. | [Avalonia Native AOT — Resolving reflection-related errors](https://docs.avaloniaui.net/docs/deployment/native-aot); [Prepare libraries for trimming](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming) |
| Compiled bindings (AOT) | Prefer `AvaloniaUseCompiledBindingsByDefault=true`, `x:DataType`, avoid unguarded `ReflectionBinding`. | [XAML compilation — Native AOT considerations](https://docs.avaloniaui.net/docs/xaml/compilation) |

#### What .NET does when `PublishAot` is set

| Claim | Source |
| --- | --- |
| Native AOT publish produces a **self-contained** app AOT-compiled to native code; no .NET runtime install required on the target. | [MS Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| Native AOT **requires trimming** (with trimming’s incompatibilities). | [MS Native AOT — Limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| Native AOT **implies single-file** compilation (with single-file API incompatibilities). | [MS Native AOT — Limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| From .NET 8+: `PublishAot` implies `SelfContained` during `dotnet publish` unless `PublishSelfContained=false`. | [Compatibility: RID / self-contained defaults](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/runtimespecific-app-default) |
| Prefer setting `PublishAot` in the project file (not only CLI), because it also enables dynamic-code analysis at build/edit time. | [MS Native AOT — Publish using CLI](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| Publish analyzes the app + dependencies and emits warnings for limitations that may fail at runtime. | [MS Native AOT — Limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/); [IL3050](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3050) |

#### Toolchain prerequisites (desktop)

| OS | Prerequisites | Source |
| --- | --- | --- |
| Windows | Visual Studio 2022+ with **Desktop development with C++** (default components). | [MS Native AOT — Prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| Linux (e.g. Ubuntu) | Native toolchain packages, e.g. `clang` + `zlib1g-dev` (distro-specific). | [MS Native AOT — Prerequisites](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| Cross-OS | **Not supported.** Need same OS (or VM/WSL/containers). Cross-**architecture** (x64↔Arm64) is limited-supported with the right linker. | [Cross-compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile) |
| Linux glibc coupling | AOT binary built on Linux runs on **same or newer** Linux version (e.g. Ubuntu 20.04 build won’t run on 18.04). | [MS Native AOT — Publish examples](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |

### 3. What “supported” means for desktop targets

**Microsoft (.NET Native AOT platform table, .NET 9+ tab):**

| Platform | Architectures | Notes in table |
| --- | --- | --- |
| Windows | x64, Arm64, x86 | *(no Experimental note)* |
| Linux | x64, Arm64, Arm | *(no Experimental note)* |

Source: [Platform/architecture restrictions](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#platformarchitecture-restrictions).

Contrast: **Android** is listed as **Experimental** (no built-in Java interop). Desktop Win/Linux are not marked experimental in that table.

**Avalonia:**

- Documents Native AOT as a deployment how-to for Avalonia applications, listing desktop startup benefits, and says platform support is whatever Microsoft documents ([Avalonia Native AOT — Platform support](https://docs.avaloniaui.net/docs/deployment/native-aot)).
- Separately states Native AOT is **supported** for **Avalonia XPF** ([same page — Avalonia XPF](https://docs.avaloniaui.net/docs/deployment/native-aot); [XPF Native AOT](https://docs.avaloniaui.net/xpf/deployment/native-aot)).
- Points to first-party samples/quick guides: [Avalonia.Samples](https://github.com/AvaloniaUI/Avalonia.Samples) (linked from docs) and [AvaloniaUI.QuickGuides/NativeAot](https://github.com/AvaloniaUI/AvaloniaUI.QuickGuides/tree/main/NativeAot) (linked from [samples-tutorials index](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/samples-tutorials/index.md)).
- Official QuickGuide `NativeAot.csproj` (as of fetch) still targets **net9.0 / Avalonia 11.2.3** with `PublishAot=true` and a comment that PublishAot is “the only thing you need” — useful as a first-party sample, but **not yet an Avalonia 12 / net10.0 reference project** ([raw csproj](https://raw.githubusercontent.com/AvaloniaUI/AvaloniaUI.QuickGuides/main/NativeAot/NativeAot.csproj)).

**Practical meaning of “supported” (from those primary sources combined):**

1. You can publish Avalonia apps with `PublishAot` for Win/Linux RIDs Microsoft lists.
2. Success assumes native toolchain prerequisites, RID-matched build OS, and AOT/trim-safe app + dependency surface.
3. Avalonia documents remaining limitations; Microsoft documents hard runtime restrictions (no JIT emit, no dynamic assembly load, no built-in COM, trimming required).
4. Warnings at publish are the contract for “might break at runtime” — not silence = automatic green light for all libraries.

### 4. Documented Avalonia AOT limitations

From [Avalonia Native AOT — Known limitations](https://docs.avaloniaui.net/docs/deployment/native-aot):

- Dynamic control creation must be configured in trimmer settings.
- Some third-party Avalonia controls may not be AOT-compatible.
- Platform-specific features need explicit configuration.
- Live preview in design-time tools may be limited.

Avalonia-specific setup constraints on the same page:

| Area | Guidance |
| --- | --- |
| XAML | Use `x:CompileBindings="True"`; avoid dynamic XAML loading; prefer static resource references. |
| Assets | Bundle as embedded / `AvaloniaResource`; avoid dynamic external asset loading. |
| VM / DI | Register view models at startup; use **compile-time DI configuration**; avoid reflection-based service location. |

XAML compilation docs add ([XAML compilation](https://docs.avaloniaui.net/docs/xaml/compilation)):

- Avalonia already compiles `.axaml` to IL at build time (XamlX).
- **Compiled bindings are required for Native AOT** because reflection-based bindings may not work without the full runtime.
- Ensure `AvaloniaUseCompiledBindingsByDefault=true`, all bindings have `x:DataType`, and no unguarded `ReflectionBinding`.

### 5. Known breakages: reflection, XAML, DI

#### Reflection / trimming

| Pattern | Status in primary sources | Source |
| --- | --- | --- |
| Unbounded reflection / dynamic assembly load | Incompatible with trimming and Native AOT. | [Known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities); [MS Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| `System.Reflection.Emit` / runtime codegen | Not available under Native AOT. | [MS Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| Reflection-based serializers (e.g. Newtonsoft.Json) | Known trim-incompatible; prefer source-generated alternatives. | [Known trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities) |
| Built-in COM marshalling (Windows) | Not trim-compatible; Native AOT: “Windows: No built-in COM.” | [Trimming incompatibilities — Built-in COM](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities); [MS Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) |
| Missing types after trim | Avalonia: root assemblies with `TrimmerRootAssembly`. | [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot) |

#### XAML / bindings / ViewLocator

| Pattern | Status | Source |
| --- | --- | --- |
| Default template ViewLocator (`Type.GetType` + `Activator.CreateInstance`) | **Not AOT-compatible**; no compile-time safety. | [View locator](https://docs.avaloniaui.net/docs/data-templates/view-locator) |
| Pattern-matching ViewLocator / XAML `DataTemplate` / DI+switch / source-gen ViewLocator | Documented as AOT-compatible alternatives. | [View locator — Choosing an approach](https://docs.avaloniaui.net/docs/data-templates/view-locator) |
| Reflection bindings | May fail under Native AOT; compiled bindings required. | [XAML compilation — Native AOT](https://docs.avaloniaui.net/docs/xaml/compilation) |
| Dynamic XAML load at runtime | Avoid for AOT. | [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot) |

Maintainer guidance (Avalonia GitHub, first-party): reflection ViewLocator should be avoided when trimming; prefer factory/`switch` mapping without `Activator.CreateInstance` ([Issue #14507](https://github.com/AvaloniaUI/Avalonia/issues/14507) comments by Avalonia maintainers; [Discussion #17738](https://github.com/AvaloniaUI/Avalonia/discussions/17738) — maxkatz6 on ViewLocator + trim).

#### DI

| Claim | Source |
| --- | --- |
| Avalonia Native AOT doc: register VMs at startup; **compile-time DI**; avoid reflection service location. | [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot) |
| Avalonia DI how-to uses `Microsoft.Extensions.DependencyInjection` with explicit `ServiceCollection` registration and `GetRequiredService` at startup (constructor injection). Prerequisites recommend .NET 10. | [Implementing dependency injection](https://docs.avaloniaui.net/docs/app-development/dependency-injection) |
| ViewLocator + DI pattern: resolve views via `GetRequiredService` inside a **switch**, not reflection. | [View locator — Dependency injection](https://docs.avaloniaui.net/docs/data-templates/view-locator) |
| Microsoft: configuration binding for AOT/trim should use the **configuration binding source generator** (`EnableConfigurationBindingGenerator`) to avoid reflection binder. | [Configuration binding source generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-generator) |

Avalonia’s DI page itself does **not** claim that every ME.DI feature is AOT-safe; the Native AOT page’s “compile-time DI configuration” is the Avalonia-side constraint.

#### Windows accessibility / COM (historical → Avalonia 11.3+/12 era)

| Claim | Source |
| --- | --- |
| Older Avalonia Win32 automation used built-in COM → **not trim/AOT compatible** (issues tracked for years). | [#8006](https://github.com/AvaloniaUI/Avalonia/issues/8006); [#11767](https://github.com/AvaloniaUI/Avalonia/issues/11767); [#13897](https://github.com/AvaloniaUI/Avalonia/issues/13897) (maintainer: built-in COM / accessibility was the real problem among many trim warnings) |
| PR **#16543** (merged 2024-10-31, labeled `area-trimming-aot`): .NET 8+ uses `[GeneratedComInterface]` source-generated COM for Windows automation — “well compatible with AOT and trimming”; opens path for Windows Automation tests under NativeAOT. | [PR #16543](https://github.com/AvaloniaUI/Avalonia/pull/16543) |
| Avalonia 12 Native AOT docs phrase `BuiltInComInteropSupport=false` as necessary **before Avalonia 12.0**. | [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot) |

#### Other first-party AOT notes

| Item | Source |
| --- | --- |
| Avalonia 12 blog cites Android startup **with NativeAOT** (4×); desktop AOT is not the blog’s focus metric. | [Avalonia 12 blog](https://avaloniaui.net/blog/avalonia-12) |
| `CompiledBinding.Create` made AOT-compatible (even if using reflection internally) — PR listed in 12.0.0 release notes. | [12.0.0 release notes / PR #20776](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.0.0) |
| Historical .NET 9 preview publish failure with Avalonia 11.1 beta (XAML ILC inject) — closed May 2024; relevant as past breakage class, not current Avalonia 12 status. | [#15646](https://github.com/AvaloniaUI/Avalonia/issues/15646) |
| Maintainer on #13897: `ReactiveUI` / `System.Reactive` were **not** trimming/AOT friendly at that time (third-party expectation). | [#13897 comment](https://github.com/AvaloniaUI/Avalonia/issues/13897) |

### 6. Microsoft Native AOT hard limitations (apply to any Avalonia desktop AOT app)

From [Limitations of Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/):

- No dynamic loading (e.g. `Assembly.LoadFile`).
- No runtime code generation (e.g. `Reflection.Emit`).
- No C++/CLI.
- Windows: no built-in COM.
- Requires trimming ([incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)).
- Implies single-file ([API incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#api-incompatibility)).
- Includes stripped runtime (size vs framework-dependent).
- `System.Linq.Expressions` always interpreted (slower than JIT-compiled expressions).
- Struct generic instantiations pre-generated (binary size impact).
- Not all runtime libraries fully annotated; some warnings not actionable by app authors.
- Diagnostics/debugging/profiling have limitations.

## Implications for Mailtide

Facts only — no stack decision.

1. **Working hypothesis alignment:** Avalonia 12 + .NET 10 matches Avalonia’s recommended desktop TFM and a shipping Avalonia major ([breaking changes](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes); [12.0.0](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.0.0)).
2. **Desktop AOT is a documented Avalonia publish path** on Windows and Linux RIDs Microsoft supports for .NET 9+ ([Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot); [platform table](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#platformarchitecture-restrictions)).
3. **CI/build topology:** Native AOT publish for Windows and for Linux must run on matching OSes (or containers/VMs); cannot cross-compile Win↔Linux from one host ([cross-compile](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile)).
4. **App architecture constraints if AOT is required:** compiled bindings (default in Avalonia 12), no reflection ViewLocator, explicit/static view resolution, startup-registered DI, no dynamic plugin assembly load, trim-safe serializers ([view locator](https://docs.avaloniaui.net/docs/data-templates/view-locator); [MS limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)).
5. **Dependency surface is part of the AOT contract:** Avalonia docs warn third-party controls may not be AOT-compatible; Microsoft requires the whole graph to survive trim/AOT analysis ([Avalonia known limitations](https://docs.avaloniaui.net/docs/deployment/native-aot); ticket `03-dependency-aot-surface` is the related inventory question).
6. **Packaging is orthogonal but complementary:** Avalonia’s desktop Linux `.deb` guide shows self-contained `dotnet publish` without requiring AOT; AOT would replace/augment that publish step with `PublishAot` ([Desktop Linux deployment](https://docs.avaloniaui.net/docs/deployment/linux)).
7. **Windows accessibility under AOT:** newer Avalonia Win32 automation path targets GeneratedComInterface for .NET 8+; still verify Narrator/automation behavior on an AOT publish rather than assuming parity ([PR #16543](https://github.com/AvaloniaUI/Avalonia/pull/16543)).

## Open unknowns

1. **First-party Avalonia 12 / net10.0 AOT sample freshness:** QuickGuide NativeAot sample still showed Avalonia **11.2.3 / net9.0** when fetched; whether Avalonia.Samples contain an updated Avalonia 12 AOT desktop sample was not confirmed from a browsable primary artifact beyond the docs link.
2. **Exact Avalonia 12 guidance on `BuiltInComInteropSupport`:** docs still list the property with a “before Avalonia 12.0” comment; whether leaving it unset, `true`, or `false` is required/recommended for Avalonia 12 Win AOT + accessibility is not spelled out as a yes/no matrix on that page.
3. **Zero-warning publish of a non-trivial Avalonia 12 desktop app** (mail client–scale dependency set) is not asserted by Avalonia docs; only that warnings are the analysis mechanism and third-party controls may fail.
4. **ReactiveUI / CommunityToolkit.Mvvm / IMAP/SQLite/etc. AOT status** for Avalonia 12 + .NET 10 is out of scope here (belongs to dependency-surface research); older Avalonia maintainer comments flagged ReactiveUI as not trim/AOT friendly at the time of #13897.
5. **Linux desktop compositor interaction under AOT** (X11 vs Wayland preview): Avalonia 12 blog says Wayland is foundational/private preview; no primary source ties Wayland maturity specifically to Native AOT.
6. **Whether Mailtide’s required Windows automation / a11y scenarios work end-to-end on Native AOT** after GeneratedComInterface — PR claims compatibility and testability, but no Avalonia doc publishes a desktop AOT a11y certification matrix.
7. **Binary size / startup numbers for Avalonia 12 desktop Native AOT on Win/Linux** are not published in the Avalonia 12 blog (Android NativeAOT startup figures are); no primary desktop benchmark cited here.

## Primary source index

- [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot)
- [Avalonia XAML compilation / AOT bindings](https://docs.avaloniaui.net/docs/xaml/compilation)
- [Avalonia View locator](https://docs.avaloniaui.net/docs/data-templates/view-locator)
- [Avalonia Dependency injection](https://docs.avaloniaui.net/docs/app-development/dependency-injection)
- [Avalonia 12 breaking changes](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)
- [Avalonia 12 blog](https://avaloniaui.net/blog/avalonia-12)
- [Avalonia 12.0.0 GitHub release](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.0.0)
- [AvaloniaUI.QuickGuides NativeAot](https://github.com/AvaloniaUI/AvaloniaUI.QuickGuides/tree/main/NativeAot)
- [PR #16543 GeneratedComInterface Windows automation](https://github.com/AvaloniaUI/Avalonia/pull/16543)
- [Microsoft Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
- [Microsoft Native AOT cross-compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/cross-compile)
- [Microsoft trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)
- [Microsoft configuration binding generator](https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration-generator)
- [IL3050](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/warnings/il3050)
