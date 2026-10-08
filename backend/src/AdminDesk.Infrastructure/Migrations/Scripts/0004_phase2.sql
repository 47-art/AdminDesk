-- Documents attached to requests, the SIM, asset and ID card masters with their history,
-- and the snapshot of the limits in force when a request was created.
--
-- Same rules as the first script: applied scripts are never edited, dates are UTC text in
-- the form yyyy-MM-ddTHH:mm:ss.fffZ (calendar dates yyyy-MM-dd), statuses are TEXT constants
-- in code without a CHECK, and business rows are never removed with DELETE: deleting means
-- is_active = 0 plus deleted_utc.

CREATE TABLE request_documents (
    id INTEGER PRIMARY KEY,
    request_id INTEGER NOT NULL REFERENCES requests(id),
    step_key TEXT NULL,
    original_name TEXT NOT NULL,
    stored_name TEXT NOT NULL UNIQUE,
    size_bytes INTEGER NOT NULL,
    content_type TEXT NOT NULL,
    uploaded_by_user_id TEXT NOT NULL,
    uploaded_by_name TEXT NOT NULL,
    uploaded_utc TEXT NOT NULL,
    deleted_by_name TEXT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE INDEX ix_request_documents_request ON request_documents(request_id, is_active);

CREATE TABLE sims (
    id INTEGER PRIMARY KEY,
    sim_number TEXT NOT NULL UNIQUE,
    mobile_number TEXT NOT NULL UNIQUE,
    telecom_operator TEXT NOT NULL,
    plan TEXT NOT NULL,
    activation_date TEXT NULL,
    status TEXT NOT NULL,
    monthly_cost_minor INTEGER NOT NULL DEFAULT 0,
    holder_employee_id INTEGER NULL REFERENCES employees(id),
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE INDEX ix_sims_status ON sims(status);
CREATE INDEX ix_sims_holder ON sims(holder_employee_id);

CREATE TABLE assets (
    id INTEGER PRIMARY KEY,
    asset_tag TEXT NOT NULL UNIQUE,
    asset_type TEXT NOT NULL,
    make_model TEXT NOT NULL,
    serial_number TEXT NOT NULL,
    status TEXT NOT NULL,
    item_condition TEXT NULL,
    holder_employee_id INTEGER NULL REFERENCES employees(id),
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE INDEX ix_assets_status ON assets(status);
CREATE INDEX ix_assets_holder ON assets(holder_employee_id);

CREATE TABLE id_cards (
    id INTEGER PRIMARY KEY,
    card_number TEXT NOT NULL UNIQUE,
    employee_id INTEGER NOT NULL REFERENCES employees(id),
    status TEXT NOT NULL,
    issued_date TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE INDEX ix_id_cards_employee ON id_cards(employee_id);

-- One row per allocation, return, damage or other event on a master record.
CREATE TABLE master_history (
    id INTEGER PRIMARY KEY,
    master_type TEXT NOT NULL,
    master_id INTEGER NOT NULL,
    event_type TEXT NOT NULL,
    employee_id INTEGER NULL REFERENCES employees(id),
    request_id INTEGER NULL REFERENCES requests(id),
    item_condition TEXT NULL,
    cost_minor INTEGER NULL,
    notes TEXT NULL,
    event_utc TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    created_by TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    updated_by TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    deleted_utc TEXT NULL
);

CREATE INDEX ix_master_history_master ON master_history(master_type, master_id);

-- The limits in force when the request was created, kept with the request.
ALTER TABLE requests ADD COLUMN limits_json TEXT NULL;
