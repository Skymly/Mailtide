# EF Core + SQLite for the offline store

Mailtide’s install-wide offline store uses **EF Core with the SQLite provider**. Attachments stay on the filesystem as blobs. Native AOT is pursued where practical (including EF precompiled queries) but is **not a release gate** — non-AOT publishes remain allowed until AOT is mature enough for Mailtide. This accepts Microsoft’s current “EF NativeAOT is experimental” stance as a risk, not a veto.
