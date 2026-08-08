# v1 distribution channels

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved

## Question

Which distribution channels does v1 commit to for Windows, Linux, and Android (e.g. installer vs store; AppImage/deb/flatpak subset; sideload APK vs Play)? Is **self-update** in v1? Lock channel set and update policy — not CI job tables or store-account provisioning (future tasks).

## Answer

**Channels (v1)**
- **Windows**: installer/package on **GitHub Releases** — not Microsoft Store
- **Linux**: **AppImage** on GitHub Releases; supported/tested baseline **Ubuntu 24.04 LTS x64** — Flatpak/deb not v1 commitments
- **Android**: signed **sideload APK** on GitHub Releases — not Play Store as a v1 commitment

**Self-update**
- **Desktop (Win/Linux)**: yes — check GitHub Releases and guide the Person to download/install (opening the release / downloading the package is enough for v1; silent patching not required)
- **Android**: no in-app self-update — Person installs newer APKs from Releases

Store accounts, signing pipelines, and CI job tables remain later tasks / implementation phase.
