# Concrete local DB engine for the offline store

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved  
Blocked by: 03, 05

## Question

Given ticket 05 (install-wide store, structured metadata/bodies + blob attachments) and research 03’s AOT inventory, which **concrete** local DB/engine and access style does Mailtide v1 use in Core? Lock the engine + access approach (e.g. Microsoft.Data.Sqlite + ADO.NET / Dapper.AOT) — not schema details or migrations tooling.

## Answer

1. **EF Core + SQLite** (`Microsoft.EntityFrameworkCore.Sqlite`) is the v1 local engine/access stack in Core — chosen despite research 03 tagging EF NativeAOT as experimental, because the team trusts EF Core and softens the AOT ship gate (below).
2. **Attachments** remain filesystem blobs per ticket 05; the DB stores references only.
3. **AOT posture for the store (and app publishes):** pursue Native AOT on Desktop where practical (precompiled queries; no dynamic LINQ composition for AOT builds). **AOT success is not a release gate** — until AOT is mature enough for Mailtide, **non-AOT publishes are allowed** (e.g. trimmed self-contained). Android continues on trimmed / Mono AOT paths and is not tied to Desktop Native AOT success.
4. Schema and migrations tooling are out of this ticket.

Map Notes updated to match this AOT posture.
