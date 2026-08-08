# Solution and module seams

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved  
Blocked by: 01, 02, 03, 05, 06

## Question

How should the solution split into deep modules — Avalonia UI, sync engine, IMAP/SMTP adapters, local persistence, credentials — and which parts must stay in AOT-friendly core libraries vs platform hosts? Working hypothesis stack stays; decide **boundaries**, not Nuke/MSTest project scaffolding tables.

## Answer

1. **Shape**: shared **Core** (no UI) + thin **Hosts** (Desktop for Win/Linux with Native AOT; Android with trimmed APK / Mono AOT) + thin **Avalonia UI** (presentation and intent forwarding only). Core must not reference Avalonia so future non-GUI hosts (CLI / TUI) can attach — those hosts are not v1 deliverables but the seam is required now.
2. **In Core** (or Core-adjacent protocol library with no platform APIs): domain model, offline store implementation, sync engine, IMAP/SMTP adapters, Auth orchestration (token lifecycle).
3. **In Hosts**: DI composition, app lifecycle, app-data paths, **secure-storage adapters**, OAuth system-browser / login UI callbacks.
4. **Process**: v1 runs the sync engine **in-process** with the app; no separate sync daemon.

Nuke / test project scaffolding tables remain out of this ticket.
