# Mailbox vs Label in the domain and navigation

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved

## Question

What navigation model does the Person see? Is IMAP **Mailbox** the single source of truth (Gmail labels mapped to special mailboxes/views), or do both Label and Mailbox exist in the domain with protocol adapters? This may revise `CONTEXT.md`’s Mailbox definition. Deep label UX stays in fog.

## Answer

1. **Mailbox-only domain** — Mailbox is the sole navigation/containment type. Provider label/folder views exposed over IMAP (including Gmail) are **mapped to Mailboxes**. No parallel Label entity in the domain for v1.
2. **Optional role** on Mailbox (SPECIAL-USE / provider convention: Inbox, Sent, Drafts, Trash, Junk, …) for pinning and post-send placement — a property, not a second entity.
3. **Unified inbox** is a **UI view** aggregating Messages from each Account’s Inbox-role Mailbox — not a new domain container.
4. Deep multi-label-on-one-message UX stays in fog.

`CONTEXT.md` Mailbox definition updated accordingly.
