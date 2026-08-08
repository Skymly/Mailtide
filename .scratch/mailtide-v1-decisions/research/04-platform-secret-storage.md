# Platform secret storage (Windows / Linux / Android) + .NET Native AOT notes

**Date:** 2026-08-05  
**Ticket:** [04-platform-secret-storage](../issues/04-platform-secret-storage.md)  
**Scope:** Primary-source fact sheet only. No final API choice — that waits on credential-model grilling.

## Executive summary

Platform-recommended patterns differ by OS, but they converge on the same idea: **do not store OAuth tokens or passwords as plaintext in app data**. On **Windows**, Microsoft documents the **Credential Locker** (`PasswordVault`) for app user credentials, plus **Windows Credential Manager** (`CredWrite` / `CredRead`) and **DPAPI** (`CryptProtectData` / .NET `ProtectedData`) for encrypt-then-persist. On **Linux** desktops, the freedesktop **Secret Service API** is the cross-DE contract; **libsecret** is the primary client library; sandboxed apps additionally have the XDG **Secret portal** (per-app master secret, not a full keyring API). On **Android**, first-party guidance is the **Android Keystore** provider for app-private cryptographic keys (and `KeyChain` for system-wide credentials); Jetpack `security-crypto` / `EncryptedSharedPreferences` is **deprecated** in favor of platform APIs and direct Keystore use. .NET docs document `ProtectedData` as **Windows-only**, MAUI `SecureStorage` as a convenience wrapper over Keystore / DPAPI-style paths, and Native AOT with important caveats: **Windows/Linux supported**, **Android Native AOT experimental with no built-in Java interop**, and **no built-in COM** on Windows Native AOT (affecting WinRT-shaped APIs unless projected via source-generated / CsWinRT-style interop).

## Windows

### Credential Locker (WinRT `PasswordVault`) — app credentials

Microsoft’s Windows apps security guidance describes the **Credential Locker** as the way for apps to securely store and retrieve user credentials (username + password), optionally roaming with the user’s Microsoft account.

Source: [Credential locker for Windows apps](https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker)

Documented facts:

- Access via `Windows.Security.Credentials.PasswordVault` / `PasswordCredential`.
- Intended for service login credentials the app collected and wants to reuse.
- Credentials do **not** expire via roaming quota / inactivity rules that apply to ordinary roaming app data.
- **Hard limit: up to 20 credentials per app.**
- Best practices in the same article: use the locker for **passwords**, not large blobs; save only after successful sign-in and only if the user opted in; **never** store credentials in plain text in app data or roaming settings.
- WinRT Credential Locker APIs are usable from WinUI and other desktop apps (WPF/WinForms); desktop consumption of WinRT is covered separately ([Call Windows Runtime APIs in desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps)).
- Newer apps are pointed to evaluate Windows Hello / passkeys as passwordless alternatives (same Credential Locker page).

Older UWP security intro material restates the same locker pattern (`PasswordVault.Add` / retrieve / remove) and the “don’t store credentials in the app storage container” rule.

Source: [Intro to secure Windows app development](https://learn.microsoft.com/en-us/windows/uwp/security/intro-to-secure-windows-app-development)

### Windows Credential Manager (Win32 `CredWrite` / `CredRead`)

Microsoft’s Win32 threat-mitigation guidance lists **Windows Credential Manager** as providing secure storage for user credentials (passwords, certificates, and other secrets), with programmatic access via `CredWrite` and `CredRead` (and UI via `CredUIPromptForWindowsCredentials`).

Source: [Threat Mitigation Techniques](https://learn.microsoft.com/en-us/windows/win32/secbp/threat-mitigation-techniques)

`CredWrite` creates or updates a credential in the **current user’s credential set**, associated with the logon session of the current token (`Advapi32.dll` / `wincred.h`).

Source: [CredWriteW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew)

### DPAPI — encrypt sensitive bytes, then persist yourself

The same Win32 threat-mitigation page lists **DPAPI** (`CryptProtectData` / `CryptUnprotectData`, plus memory variants) for encrypting/decrypting sensitive data bound to the user account or machine, and warns: never store passwords in plaintext.

Source: [Threat Mitigation Techniques](https://learn.microsoft.com/en-us/windows/win32/secbp/threat-mitigation-techniques)

.NET wraps DPAPI as `System.Security.Cryptography.ProtectedData` (`Protect` / `Unprotect`), documented for encrypting passwords, keys, and connection strings with `DataProtectionScope.CurrentUser` or machine scope. **Windows only** — use on non-Windows throws `PlatformNotSupportedException`.

Sources:

- [ProtectedData class](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata)
- [How to: Use Data Protection](https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection)

### .NET MAUI `SecureStorage` on Windows (reference implementation, not a Mailtide requirement)

Microsoft’s MAUI docs describe a cross-platform key/value secure store. On Windows it encrypts with **`DataProtectionProvider`** and stores ciphertext in `ApplicationData` local settings (packaged) or a `securestorage.dat` file (unpackaged). The conceptual guide’s sample literally uses an `oauth_token` key.

Sources:

- [Secure storage - .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0)
- [SecureStorage API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.storage.securestorage?view=net-maui-10.0)

This is Microsoft’s documented *pattern* for .NET client apps; Mailtide is Avalonia-shaped, so treat MAUI as evidence of underlying OS mechanisms, not as a stack mandate.

## Linux

### Freedesktop Secret Service API

The Secret Service API is the freedesktop D-Bus contract for storing secrets in a service in the user’s login session (designed by GNOME and KDE developers as a common successor to desktop-specific keyring APIs).

Source: [Secret Service API Draft](https://specifications.freedesktop.org/secret-service-spec/latest-single/) (Secret Service 0.2 DRAFT)

Documented model:

- A **secret** is an opaque byte array an app wants to store securely (password is the canonical example); combining multiple values into one secret via a textual format is allowed.
- Secrets are stored with **lookup attributes** and a **label** as an **item**; items group into **collections** (keyring/wallet analogues).
- Attributes are for lookup and are **not** treated as secret; the service may leave them unencrypted on disk.
- Collections may be accessed via aliases such as **`default`**.
- Items/collections may be **locked**; secrets of locked items cannot be read until unlocked (often via user prompt).
- Clients should look up by attributes, not hard-code D-Bus object paths.
- Transfer can use negotiated session algorithms (including plaintext and encrypted DH-based transfer as specified).

### libsecret (client library)

libsecret is the GNOME-maintained client library for talking to a running secret service (docs explicitly say “like gnome-keyring or ksecretservice”). Simple password APIs: store / lookup / clear, sync and async; schemas define attribute names/types; attributes must not contain secrets.

Sources:

- [libsecret API index](https://gnome.pages.gitlab.gnome.org/libsecret/)
- [libsecret Python examples](https://gnome.pages.gitlab.gnome.org/libsecret/libsecret-python-examples.html)
- [Using libsecret](https://gnome.pages.gitlab.gnome.org/libsecret/libsecret-using.html) (`pkg-config` name `libsecret-1`, header `<libsecret/secret.h>`)

### XDG Desktop Portal Secret (sandboxed / Flatpak)

For sandboxed apps, `org.freedesktop.portal.Secret` exposes `RetrieveSecret`: a **per-application master secret** written to a file descriptor, typically persisted in the user’s keyring under the app ID. The app encrypts its own confidential data with that secret (expand via KDF if needed). This is **not** a general multi-item credential API; it is a portal-mediated master key for in-sandbox ciphertext.

Source: [XDG Desktop Portal — Secret](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Secret.html)

## Android

### Android Keystore provider (app-private) vs KeyChain (system-wide)

Android’s Keystore system stores cryptographic keys in a container so key material is hard to extract; keys can be non-exportable and use-restricted (crypto modes, validity window, user authentication). Key material never enters the app process for Keystore-backed ops; it may be bound to TEE / StrongBox.

Source: [Android Keystore system](https://developer.android.com/privacy-and-security/keystore)

Explicit choice guidance from that page:

- Use **`KeyChain`** when you want **system-wide** credentials with user selection UI shared across apps.
- Use the **Android Keystore provider** when an individual app stores credentials **only that app** can access (no user credential picker).

Usage is via standard JCA types with the `AndroidKeyStore` provider (`KeyStore`, `KeyGenerator` / `KeyPairGenerator`, `KeyGenParameterSpec`), introduced at API 18. Crypto work should stay off the main thread.

Android’s “Hardcoded Cryptographic Secrets” risk guide’s mitigation is the same split: KeyChain for system-wide, Android Keystore for app-private credentials; sample code generates/stores a symmetric key in Keystore and encrypts with `AES/GCM/NoPadding`.

Source: [Hardcoded Cryptographic Secrets](https://developer.android.com/privacy-and-security/risks/hardcoded-cryptographic-secrets)

### Jetpack `security-crypto` / `EncryptedSharedPreferences` — deprecated

Android cryptography docs state that **all APIs** in the Jetpack `security-crypto` library were **deprecated** in stable **1.1.0**, with no subsequent releases.

Source: [Cryptography](https://developer.android.com/privacy-and-security/cryptography)

Jetpack release notes for Security-Crypto 1.1.0: deprecated all APIs in favour of **existing platform APIs and direct use of Android Keystore**.

Source: [Security Jetpack releases](https://developer.android.com/jetpack/androidx/releases/security)

`EncryptedSharedPreferences` API reference marks the class deprecated (replacement text points at `SharedPreferences`; MasterKeys deprecation points at `KeyGenerator` with AndroidKeyStore). Combined with the cryptography/Jetpack notes above, the platform-recommended direction is **Keystore-backed encryption**, not continued reliance on `security-crypto`.

Source: [EncryptedSharedPreferences](https://developer.android.com/reference/androidx/security/crypto/EncryptedSharedPreferences)

### .NET MAUI `SecureStorage` on Android (reference)

MAUI documents Android secure storage as: encryption keys in **KeyStore**, encrypted values in a named shared-preferences file; modern MAUI path uses `EncryptedSharedPreferences` (AES key/value schemes). Auto Backup can restore ciphertext without keys — exclude the prefs file or handle decryption failure / clear.

Sources:

- [Secure storage - .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0)
- [SecureStorage API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.storage.securestorage?view=net-maui-10.0)

Again: useful as Microsoft’s described Android mechanism for .NET apps; Jetpack deprecation means a greenfield design should weight **direct Keystore + own ciphertext store** heavily.

### OAuth refresh tokens on-device (Google Android identity note)

Google’s Android identity authorization guide strongly discourages storing long-lived **refresh tokens on the device** for apps that have a backend, preferring server-side storage. Mailtide is a personal offline-first client **without** an app backend in the working hypothesis — this is context that “mobile OAuth best practice” and “desktop mail client token cache” are not identical threat models; do not silently import the server-side assumption.

Source: [Authorize access to Google user data](https://developer.android.com/identity/authorization)

## .NET / Native AOT notes

### What .NET documents about secret APIs

| Mechanism | .NET surface | Platform | AOT-relevant notes from Microsoft docs |
| --- | --- | --- | --- |
| DPAPI | `ProtectedData` (`System.Security.Cryptography.ProtectedData` package) | Windows only | Managed wrapper over OS DPAPI; non-Windows → `PlatformNotSupportedException`. No AOT-specific ban in the API docs; still subject to general Native AOT interop rules for the underlying P/Invoke. |
| Credential Locker | WinRT `PasswordVault` via Windows TFM / WinRT projections | Windows | Desktop apps need WinRT enablement (e.g. `net10.0-windows10.0.*` TFM). Native AOT: **no built-in COM** on Windows — prefer source-generated / CsWinRT-style projections rather than runtime COM stubs. |
| CredMan | P/Invoke to `CredWrite` / `CredRead` (`Advapi32`) | Windows | Prefer `[LibraryImport]` source-generated P/Invoke for AOT/trimming; runtime `DllImport` IL stubs are incompatible with full Native AOT. |
| Secret Service / libsecret | No first-party .NET BCL API; D-Bus or native libsecret interop | Linux | Same P/Invoke / native interop AOT rules; must bind to session bus / libsecret and handle missing service. |
| Android Keystore | Java/`AndroidKeyStore` via .NET for Android bindings (or MAUI `SecureStorage`) | Android | Keystore is a **Java** provider API. Native AOT for Android is **experimental** and documented as having **no built-in Java interop** — a critical mismatch for Keystore if targeting `PublishAot` Native AOT rather than Mono/.NET Android AOT. |

Sources for the AOT/interop columns:

- [Native AOT deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/) — platform table: Windows/Linux supported; Android **experimental, no built-in Java interop**; limitations include **Windows: No built-in COM**, trimming, no `Reflection.Emit`, etc.
- [Native code interop with Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop) — P/Invoke works with AOT-specific direct-call options.
- [P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) — `LibraryImport` for AOT/trimming; runtime `DllImport` stubs not available for full Native AOT.
- [COM source generation / ComWrappers](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/comwrappers-source-generation) — built-in COM IL stubs incompatible with Native AOT; CsWinRT built on ComWrappers for WinRT.
- [ProtectedData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata) — Windows-only DPAPI wrapper.

### Distinguish two “AOT”s on Android

Microsoft documents separately:

1. **Native AOT** (`PublishAot`) — table entry for Android is experimental / no built-in Java interop ([Native AOT overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)).
2. **.NET for Android / MAUI release AOT** (`RunAOTCompilation`, profiled AOT) — Mono-style AOT used in mobile release builds ([Xamarin.Android → .NET MAUI Android migration](https://learn.microsoft.com/en-us/dotnet/maui/migration/android-projects?view=net-maui-9.0)).

Secret storage that depends on Android Keystore **assumes Java interop**. That is a first-class research constraint for any Avalonia + .NET 10 Android publish mode decision (see related ticket `02-avalonia-android-aot`).

### MAUI Native AOT scope

MAUI’s Native AOT deployment docs cover **iOS and Mac Catalyst**, not Android as a supported Native AOT target in that guide.

Source: [Native AOT for .NET MAUI](https://learn.microsoft.com/en-us/dotnet/maui/deployment/nativeaot?view=net-maui-10.0)

## Implications (no API selection)

Facts that should feed grilling / credential-model design — **not** a chosen stack:

1. **One abstract “secure store” seam, three OS backends** is what the platforms themselves imply: Windows credential locker/CredMan/DPAPI, Linux Secret Service, Android Keystore-backed encryption. Cross-platform .NET does not ship a BCL equivalent for all three.
2. **Credential Locker’s 20-credential cap** matters for a multi-account mail client if each account maps to one locker entry (or more, if refresh + access are split). That alone may push Windows toward CredMan or DPAPI-encrypted blobs for unbounded accounts.
3. **Linux has a session service dependency**: Secret Service may be locked, absent (headless/SSH), or implemented differently across GNOME/KDE; Flatpak needs the **Secret portal** path rather than raw `org.freedesktop.secrets` access assumptions.
4. **Android’s recommended primitive is a Keystore key**, with ciphertext living in app-private storage; Jetpack encrypted-prefs is a deprecated convenience layer. Auto Backup vs Keystore key loss is a documented footgun.
5. **Native AOT is not uniform**: Windows/Linux Native AOT is the documented sweet spot for DPAPI/P/Invoke/D-Bus-style backends; Android Keystore via Java interop conflicts with experimental Native AOT’s “no built-in Java interop” note — Android may need a different publish mode than desktop even if the *logical* secret API is shared.
6. **MAUI `SecureStorage`** is useful primary-source evidence of Microsoft’s .NET client pattern (including an `oauth_token` sample), but adopting MAUI as a dependency is a separate product/stack decision outside this ticket.
7. **OAuth token shape** (access vs refresh, multi-account, wipe-on-logout, roaming) is not decided here; platform stores are small-secret oriented (Credential Locker explicitly says not for large blobs; MAUI notes performance impact for large text).

## Open unknowns

- Exact WinRT **Credential Locker** availability and behavior for **unpackaged Avalonia** desktop under .NET 10 Native AOT (CsWinRT projection packaging, roaming with MSA, domain-account non-roaming rules in real installs).
- Whether **CredMan `CRED_TYPE_GENERIC`** is the intended Win32 home for arbitrary OAuth refresh tokens vs DPAPI-encrypted files under ACL’d app data — Microsoft documents both families without a single “OAuth token” recipe.
- **KDE / non-GNOME** Secret Service completeness and prompting behavior for a GTK-independent Avalonia app using libsecret or raw D-Bus (spec is shared; implementations vary — needs runtime matrix, not resolved by the DRAFT alone).
- Headless / CI / server-side Linux: acceptable fallback when no Secret Service is present (platform docs assume a login-session service).
- For Android under Avalonia: which publish path (.NET Android Mono AOT vs experimental Native AOT) is actually viable for **Keystore JNI**, and what minimum API level Mailtide targets (AES in Keystore from API 23+ is called out in MAUI’s legacy notes).
- Whether Mailtide’s personal offline-first model consciously **rejects** Google’s “don’t store refresh tokens on device” guidance (written for apps with backends) — product/threat-model grilling, not answered by storage APIs.
- Linker/trim warnings for any candidate NuGet (MAUI Essentials slice, third-party keyring bindings, CsWinRT packages) under `PublishAot` — not validated in this research pass.
- Interaction with ticket **07-credential-model** (what is stored: refresh token only vs password vs client secrets) and **08-v1-oauth-idp-set** (provider token lifetimes / revocation).

## Sources (primary)

**Windows / Microsoft**

- https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker
- https://learn.microsoft.com/en-us/windows/uwp/security/intro-to-secure-windows-app-development
- https://learn.microsoft.com/en-us/windows/win32/secbp/threat-mitigation-techniques
- https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew
- https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps
- https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata
- https://learn.microsoft.com/en-us/dotnet/standard/security/how-to-use-data-protection
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.maui.storage.securestorage?view=net-maui-10.0

**.NET AOT / interop**

- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop
- https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation
- https://learn.microsoft.com/en-us/dotnet/standard/native-interop/comwrappers-source-generation
- https://learn.microsoft.com/en-us/dotnet/maui/deployment/nativeaot?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/migration/android-projects?view=net-maui-9.0

**Linux / freedesktop / GNOME**

- https://specifications.freedesktop.org/secret-service-spec/latest-single/
- https://gnome.pages.gitlab.gnome.org/libsecret/
- https://gnome.pages.gitlab.gnome.org/libsecret/libsecret-python-examples.html
- https://gnome.pages.gitlab.gnome.org/libsecret/libsecret-using.html
- https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Secret.html

**Android**

- https://developer.android.com/privacy-and-security/keystore
- https://developer.android.com/privacy-and-security/cryptography
- https://developer.android.com/privacy-and-security/risks/hardcoded-cryptographic-secrets
- https://developer.android.com/jetpack/androidx/releases/security
- https://developer.android.com/reference/androidx/security/crypto/EncryptedSharedPreferences
- https://developer.android.com/identity/authorization
