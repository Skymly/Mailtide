# Sync engine external contract

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: claimed  
Blocked by: 05

## Question

What is the sync engine’s external contract? How do multiple Accounts run concurrently? Who triggers fetch/push? How do send queue and drafts enter the engine? What is the minimal failure/retry surface exposed to UI? Lock **seam and responsibilities**, not IMAP pipeline internals or rate-limit numbers.
