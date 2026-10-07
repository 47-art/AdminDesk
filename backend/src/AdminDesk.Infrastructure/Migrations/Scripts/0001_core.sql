-- Core schema: masters, workflow definitions, limits, counters, requests, steps, actors,
-- the append-only audit trail and the operational logs table.
--
-- Rules for this file:
--  * Applied scripts are never edited. A change is a new numbered script; the runner
--    records every applied script in SchemaVersions.
--  * Moving to PostgreSQL means translating these scripts (types, RETURNING and
--    trigger syntax); all SQL in the application sits behind the dialect interface.
--  * Dates and enum-like values are TEXT. Dates are UTC in the form
--    yyyy-MM-ddTHH:mm:ss.fffZ, calendar dates are yyyy-MM-dd.
--  * Business tables carry created_utc, created_by, updated_utc, updated_by, is_active
--    and deleted_utc. The actor columns have no default, so an unstamped insert fails.
--    Business rows are never removed with DELETE: deleting means is_active = 0 plus
--    deleted_utc.
--  * Every UNIQUE constraint stays in force for soft-deleted rows, so a soft-deleted
--    code stays reserved and cannot be reused.
--  * Event types, step types and step states are string constants in code and are
--    deliberately not constrained here, because later releases add values.
--  * Log rows are operational records rather than business data. They are removed by
--    the retention purge at startup, they carry no actor or soft-delete columns, and
--    the Email category is reserved for later use.

CREATE TABLE departments (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE TABLE locations (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE TABLE projects (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE TABLE cost_centres (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL UNIQUE,
    name TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE TABLE employees (
    id INTEGER PRIMARY KEY,
    employee_code TEXT NOT NULL UNIQUE,
    full_name TEXT NOT NULL,
    email TEXT NULL,
    designation TEXT NULL,
    department_id INTEGER NULL REFERENCES departments(id),
    location_id INTEGER NULL REFERENCES locations(id),
    reporting_manager_id INTEGER NULL REFERENCES employees(id),
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE INDEX ix_employees_reporting_manager_id ON employees(reporting_manager_id);
CREATE INDEX ix_employees_department_id ON employees(department_id);
CREATE INDEX ix_employees_is_active ON employees(is_active);

CREATE TABLE module_definitions (
    id INTEGER PRIMARY KEY,
    code TEXT NOT NULL,
    version INTEGER NOT NULL,
    name TEXT NOT NULL,
    category TEXT NULL,
    prefix TEXT NOT NULL,
    definition_json TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL,
    UNIQUE(code, version)
);

CREATE TABLE module_limits (
    id INTEGER PRIMARY KEY,
    module_code TEXT NOT NULL,
    step_key TEXT NOT NULL,
    limit_key TEXT NOT NULL,
    value_minor INTEGER NOT NULL,
    unit TEXT NULL,
    is_sample INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL,
    UNIQUE(module_code, step_key, limit_key)
);

-- Hands out request numbers: one row per module and year, incremented in one statement.
CREATE TABLE request_counters (
    module_code TEXT NOT NULL,
    year INTEGER NOT NULL,
    last_value INTEGER NOT NULL,
    PRIMARY KEY (module_code, year)
);

CREATE TABLE requests (
    id INTEGER PRIMARY KEY,
    request_no TEXT NOT NULL UNIQUE,
    module_code TEXT NOT NULL,
    definition_id INTEGER NOT NULL REFERENCES module_definitions(id),
    definition_version INTEGER NOT NULL,
    requester_employee_id INTEGER NOT NULL REFERENCES employees(id),
    department_id INTEGER NULL REFERENCES departments(id),
    project_id INTEGER NULL REFERENCES projects(id),
    location_id INTEGER NULL REFERENCES locations(id),
    cost_centre_id INTEGER NULL REFERENCES cost_centres(id),
    request_date TEXT NOT NULL,
    required_date TEXT NULL,
    priority TEXT NOT NULL DEFAULT 'Normal',
    subject TEXT NULL,
    approval_status TEXT NOT NULL,
    current_status TEXT NOT NULL,
    current_step_key TEXT NULL,
    current_step_seq INTEGER NULL,
    responsible_employee_id INTEGER NULL REFERENCES employees(id),
    responsible_role TEXT NULL,
    remarks TEXT NULL,
    payload_json TEXT NOT NULL DEFAULT '{}',
    amount_minor INTEGER NULL,
    parent_request_id INTEGER NULL REFERENCES requests(id),
    row_version INTEGER NOT NULL DEFAULT 1,
    closed_utc TEXT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE INDEX ix_requests_requester_date ON requests(requester_employee_id, request_date DESC);
CREATE INDEX ix_requests_current_status ON requests(current_status);
CREATE INDEX ix_requests_module_code ON requests(module_code);
CREATE INDEX ix_requests_updated_utc ON requests(updated_utc);

CREATE TABLE request_steps (
    id INTEGER PRIMARY KEY,
    request_id INTEGER NOT NULL REFERENCES requests(id),
    seq INTEGER NOT NULL,
    step_key TEXT NOT NULL,
    name TEXT NOT NULL,
    step_type TEXT NOT NULL,
    state TEXT NOT NULL,
    activated_utc TEXT NULL,
    due_utc TEXT NULL,
    acted_by_user_id TEXT NULL,
    acted_by_name TEXT NULL,
    acted_utc TEXT NULL,
    comment TEXT NULL,
    captured_json TEXT NULL,
    UNIQUE(request_id, seq)
);

CREATE TABLE request_step_actors (
    id INTEGER PRIMARY KEY,
    request_id INTEGER NOT NULL REFERENCES requests(id),
    step_seq INTEGER NOT NULL,
    role_name TEXT NULL,
    employee_id INTEGER NULL REFERENCES employees(id),
    is_active INTEGER NOT NULL DEFAULT 1
);

CREATE INDEX ix_request_step_actors_role ON request_step_actors(role_name) WHERE is_active = 1;
CREATE INDEX ix_request_step_actors_employee ON request_step_actors(employee_id) WHERE is_active = 1;
CREATE INDEX ix_request_step_actors_request ON request_step_actors(request_id, step_seq);

-- Append-only audit trail: the triggers below reject every UPDATE and DELETE.
CREATE TABLE audit_events (
    id INTEGER PRIMARY KEY,
    request_id INTEGER NULL,
    event_type TEXT NOT NULL,
    actor_user_id TEXT NULL,
    actor_name TEXT NOT NULL,
    actor_role TEXT NULL,
    step_key TEXT NULL,
    from_status TEXT NULL,
    to_status TEXT NULL,
    comment TEXT NULL,
    details_json TEXT NULL,
    correlation_id TEXT NULL,
    created_utc TEXT NOT NULL
);

CREATE INDEX ix_audit_events_request ON audit_events(request_id, id);

CREATE TRIGGER trg_audit_events_no_update
BEFORE UPDATE ON audit_events
BEGIN
    SELECT RAISE(ABORT, 'audit_events is append-only: UPDATE is not allowed');
END;

CREATE TRIGGER trg_audit_events_no_delete
BEFORE DELETE ON audit_events
BEGIN
    SELECT RAISE(ABORT, 'audit_events is append-only: DELETE is not allowed');
END;

CREATE TABLE logs (
    id INTEGER PRIMARY KEY,
    timestamp_utc TEXT NOT NULL,
    level TEXT NOT NULL,
    category TEXT NOT NULL DEFAULT 'App' CHECK (category IN ('App', 'Email')),
    message TEXT NOT NULL,
    message_template TEXT NULL,
    exception TEXT NULL,
    correlation_id TEXT NULL,
    properties_json TEXT NULL
);

CREATE INDEX ix_logs_category_timestamp ON logs(category, timestamp_utc);
CREATE INDEX ix_logs_level_timestamp ON logs(level, timestamp_utc);
