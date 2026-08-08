# Sync engine external contract

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved  
Blocked by: 05

## Question

What is the sync engine’s external contract? How do multiple Accounts run concurrently? Who triggers fetch/push? How do send queue and drafts enter the engine? What is the minimal failure/retry surface exposed to UI? Lock **seam and responsibilities**, not IMAP pipeline internals or rate-limit numbers.

## Answer

1. **Concurrency**: one active sync pipeline per Account; multiple Accounts run in parallel. The engine coordinates scheduling/limits but does not serialize all Accounts onto a single global worker.
2. **Triggers**: engine self-driving (while foreground / allowed background) plus explicit `SyncNow` / `SendNow` intents that merge into that Account’s pipeline. UI never speaks IMAP/SMTP.
3. **Drafts / Outbox**: drafts are local store state only and are not auto-sent. Send moves a Message into **Outbox** in the store; the engine is the sole Outbox consumer. On success, local sent state is updated; on failure, the item stays failed in Outbox with an observable error.
4. **UI surface**: per-Account idle / syncing / error (human-readable reason); per-Outbox-item queued / sending / failed; actions `SyncNow`, `Retry`, `Discard`. No IMAP codes, backoff curves, or connection-pool state on the UI.

Rate limits and IMAP pipeline internals remain in fog.
