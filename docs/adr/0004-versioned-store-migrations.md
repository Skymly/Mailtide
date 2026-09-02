# Versioned SQLite store migrations

Mailtide’s install-wide store is still EF Core + SQLite (ADR-0002). Schema changes are applied through an explicit **integer SchemaVersion** and ordered migrations in Core (`StoreMigrator`). Missing columns are detected with `pragma_table_info` and added only when absent. Migration SQL failures propagate — they are not swallowed by empty `catch` blocks.

Fresh installs still call `EnsureCreated` for the current EF model, then fast-forward the version table to the latest migration (including the FTS5 search index). Existing 0.10 stores without `SchemaVersion` are treated as version 0 and upgraded in place.
