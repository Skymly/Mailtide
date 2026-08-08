# Core + thin hosts (UI-agnostic Core)

Mailtide splits into a UI-free **Core** (store, sync, IMAP/SMTP, Auth orchestration), a thin **Avalonia UI** (presentation + intents only), and thin **Desktop/Android hosts** (lifecycle, secure-storage adapters, OAuth system UI). Desktop publishes with Native AOT; Android uses its own trimmed/Mono AOT path. Sync runs in-process for v1. Core stays hostable by future CLI/TUI without forking business logic.
