# Solution and module seams

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: claimed  
Blocked by: 01, 02, 03, 05, 06

## Question

How should the solution split into deep modules — Avalonia UI, sync engine, IMAP/SMTP adapters, local persistence, credentials — and which parts must stay in AOT-friendly core libraries vs platform hosts? Working hypothesis stack stays; decide **boundaries**, not Nuke/MSTest project scaffolding tables.
