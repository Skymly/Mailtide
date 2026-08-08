# Per-platform secure-storage API choice

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved  
Blocked by: 04, 07, 09

## Question

Given research 04, credential model 07, and host-side secure-storage adapters from 09: which **concrete** APIs does each Host use on Windows, Linux (Ubuntu 24.04), and Android to persist Credentials? Lock one primary approach per platform (with AOT/publish-mode constraints acknowledged) — not the full Auth UI flow.

## Answer

Host adapters implement Core’s secure-storage port — **no plaintext fallback**.

| Platform | Primary API |
| --- | --- |
| **Windows** | DPAPI via `ProtectedData` (`DataProtectionScope.CurrentUser`); ciphertext in app data. Not WinRT Credential Locker (20-credential cap) and not MAUI `SecureStorage` as a dependency. |
| **Linux** (Ubuntu 24.04 AppImage) | Freedesktop **Secret Service** via **libsecret** (or equivalent D-Bus client), lookup by Account attributes. Not XDG Secret portal (sandbox/Flatpak master-secret model). Missing/locked keyring fails observably. |
| **Android** | **Android Keystore** to protect keys; encrypt Credential blobs into app-private storage, implemented in the Android Host via Java/.NET for Android interop. Not deprecated Jetpack `EncryptedSharedPreferences`; not system `KeyChain`. |

OAuth browser/login UI flow remains out of this ticket.
