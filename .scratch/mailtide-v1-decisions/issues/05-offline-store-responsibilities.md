# Offline store responsibilities and partitioning

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved

## Question

For Mailtide’s offline-first local store: one store for the whole install vs one per Account? Where do Message metadata, bodies, and attachments live? Where is the seam with the sync engine (store must not schedule network I/O)? Decide responsibilities and partitioning — not the concrete DB engine (that waits on AOT dependency research).

## Answer

1. **One install-wide store**, logically partitioned by Account (not one physical DB per Account).
2. **Message metadata and bodies** live in the store’s structured data; **attachment bytes** live in a co-located blob area, with only references + attachment metadata in tables.
3. **Store responsibilities**: local persistence, queries, transactions (including local draft/outbox records). **Store must not** schedule network I/O, hold IMAP/SMTP connections, or refresh OAuth tokens. The sync engine is the only network scheduler and talks to the store via read/write APIs; UI reads local truth through the store (or store-backed queries), not the protocol layer.

Concrete DB engine remains in fog until chosen against the AOT dependency inventory.
