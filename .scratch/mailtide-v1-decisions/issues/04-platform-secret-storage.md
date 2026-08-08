# Platform secret storage on Windows / Linux / Android

Part of: [Mailtide v1 — decision map](../map.md)

Type: research  
Status: resolved

## Question

What are the first-party / platform-recommended ways to store app secrets and OAuth tokens on **Windows**, **Linux**, and **Android**, and what do .NET 10 docs say about using them (including AOT/linker notes)? Fact sheet only — final API choice waits on credential-model grilling.

## Answer

Platform paths: Windows Credential Locker / CredMan / DPAPI; Linux Secret Service (libsecret) + XDG portal when sandboxed; Android Keystore (prefer) / KeyChain — Jetpack `EncryptedSharedPreferences` deprecated. .NET Native AOT is fine on Win/Linux with careful interop; Android Native AOT is experimental and lacks built-in Java interop, which conflicts with Keystore usage. Prefer an abstract secure-store seam + OS backends; no concrete API chosen here.

Full cited fact sheet: [research/04-platform-secret-storage.md](../research/04-platform-secret-storage.md)
