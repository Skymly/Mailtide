# Offline store responsibilities and partitioning

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: open

## Question

For Mailtide’s offline-first local store: one store for the whole install vs one per Account? Where do Message metadata, bodies, and attachments live? Where is the seam with the sync engine (store must not schedule network I/O)? Decide responsibilities and partitioning — not the concrete DB engine (that waits on AOT dependency research).
