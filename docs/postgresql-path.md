# Moving to PostgreSQL

The application runs on SQLite. All SQL sits behind repository interfaces, and the few places where SQL differs between engines go through one small interface (`ISqlDialect`). Moving to PostgreSQL is a contained piece of work.

## Steps

1. **Translate the numbered SQL scripts** in `backend/src/AdminDesk.Infrastructure/Migrations/Scripts`. They are plain SQL run in order by DbUp, which also supports PostgreSQL. Typical changes: `INTEGER PRIMARY KEY AUTOINCREMENT` becomes `BIGINT GENERATED ALWAYS AS IDENTITY`, timestamps stored as text become `timestamptz`, and the append-only triggers on the audit table are rewritten in PL/pgSQL.
2. **Replace `SqliteDialect`** with a PostgreSQL dialect implementing the same interface: the counter upsert, JSON extraction (`payload_json ->> 'amount'`), limit and offset, the current time expression and the case-insensitive match (`ILIKE`).
3. **Swap the connection factory** (`SqliteConnectionFactory`) for one that opens an `NpgsqlConnection`, and the unit of work with it. Repositories only see `IDbConnectionFactory` and `IUnitOfWork`.
4. **Store JSON as `jsonb`** for the payload, captured values and audit details columns.
5. **Background jobs:** use the Hangfire PostgreSQL storage package instead of the SQLite one. The `Jobs:Provider` switch and the scheduler interface stay as they are.
6. **Identity:** change the Entity Framework provider used for the user and role tables from SQLite to Npgsql, and regenerate its tables in the scripts.
7. **Logging:** the log table sink writes through its own connection; point it at the same database.

The upsert used for request counters, `INSERT ... ON CONFLICT ... DO UPDATE ... RETURNING`, is already valid PostgreSQL, so it needs no change.

## What does not change

Controllers, the workflow engine, the definitions and the web app do not know which database is in use.
