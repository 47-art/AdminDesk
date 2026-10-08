-- Background job tables used by the timer-based scheduler and by the job probes.
-- These are operational records, not business data: they carry no actor or
-- soft-delete columns and finished rows stay in place as a history.

CREATE TABLE scheduled_jobs (
    id TEXT PRIMARY KEY,
    handler_type TEXT NOT NULL,
    args_json TEXT NULL,
    due_utc TEXT NOT NULL,
    state TEXT NOT NULL DEFAULT 'Pending',
    created_utc TEXT NOT NULL,
    completed_utc TEXT NULL,
    error TEXT NULL
);

CREATE INDEX ix_scheduled_jobs_state_due ON scheduled_jobs(state, due_utc);

CREATE TABLE job_probe_log (
    id INTEGER PRIMARY KEY,
    source TEXT NOT NULL,
    handler TEXT NOT NULL,
    worker_label TEXT NULL,
    written_utc TEXT NOT NULL
);

CREATE INDEX ix_job_probe_log_source ON job_probe_log(source, handler);
