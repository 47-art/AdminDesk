# AdminDesk

An Admin operations platform built around **one configuration-driven workflow engine**. Employees raise requests, the requests flow through approvals and tasks to automatic closure, every step is written to an audit trail, and the server enforces who can see or do what by role.

> **Context.** I built this as a practical assignment for a mid-level engineer position, from a broad process description that was too large to finish in the time given. So it is a proof of concept: the foundation and a representative set of processes are built properly, and what is left out is listed at the bottom.

## The idea

A process is a **definition file, not new code.** Fields, ordered steps, who acts on each step, conditions and approval limits live in one JSON file ([docs/definitions.md](docs/definitions.md)). Adding a process means adding a file.

Built on that engine: Stationery, Courier (with proof of delivery), SIM and SIM return, Laptop / IT asset and Asset return, ID card, Employee welfare and Housekeeping, plus masters for SIMs, assets and ID cards (holder, status, history), conditional approval steps, document upload, and limits and conditions you can edit in the app without a deploy.

## Run it

You need the **.NET 10 SDK** and **Node.js 22**. No database server or container: data lives in SQLite files created on first start.

```
git clone https://github.com/47-art/AdminDesk.git
cd AdminDesk
scripts/dev.ps1        # Windows PowerShell   (bash: scripts/dev.sh)
```

The script builds the backend, installs the frontend packages, starts the API (http://localhost:5080) and the web app, and waits. Open **http://localhost:4200**. Stop it with Ctrl+C or `scripts/dev.ps1 -Stop`. The first start creates the database, a sample organisation of 200 employees and demo requests; delete `data/` to start over. (`sample-data/app.db` is an optional ready-made database: copy it to `data/app.db` before the first start.)

**Sign in** by clicking a role card. Every account uses the fake practice password `Demo@12345`, for example `management@demo.test`, `finance@demo.test`, `hr@demo.test`, `it@demo.test`, `admin@demo.test`, `store@demo.test`, `security@demo.test`, `sysadmin@demo.test`, `manager@demo.test` and `employee@demo.test`.

**A two-minute tour.** As the Employee, raise a Laptop request with a cost, then approve it as the Manager and IT. A Finance step appears only because the cost is above the Finance limit. As Management, open *Limits and conditions*, raise the limit, and raise another request: the Finance step is skipped. As IT, allocate a laptop and watch the holder and history update under *Masters*. As Admin, open any closed request to read its audit trail.

## How it fits together

```
Angular 21 + PrimeNG  --JSON, JWT-->  ASP.NET Core API
                                         Application  (workflow, queries, documents, masters)
                                         Domain       (step planner, rule evaluator; no I/O)
                                         Infrastructure (Dapper + SQLite, migrations, Identity, jobs)
```

The web app calls the API with relative `/api` addresses. Sign-in returns a JWT that an interceptor attaches to every call. Every response has one shape (`success`, `data`, `error` with a code and per-field messages), so a failure appears beside the right input. The server authorises each endpoint with a policy; the screen hides buttons but is never the control. Every response carries a correlation id that matches the id stored with any logged error. In production the API also serves the built web app, so there is one process and one origin.

## How a module is configured

```json
{ "code": "laptop", "version": 1, "prefix": "LAP",
  "fields": [ { "key": "estimatedCost", "label": "Cost", "type": "money" } ],
  "steps": [
    { "key": "manager-approval", "type": "approval", "actor": { "reportingManager": true } },
    { "key": "finance-approval", "type": "approval", "actor": { "roles": ["Finance"] },
      "condition": { "field": "estimatedCost", "op": "gt", "limit": "finance-limit" } } ],
  "limits": [ { "stepKey": "finance-approval", "limitKey": "finance-limit", "valueMinor": 0 } ] }
```

Steps are `approval` or `task`. An actor is the reporting manager, the requester, or a list of roles. Conditions compare a field, a value captured by an earlier step, or a stored limit. Definitions are validated at start-up, so a typo stops the app with a message naming the file. Each request is **pinned to the version and the limits in force when it was created**, so changing a module never reroutes work in flight.

## Choices and why

- **JSON definitions, versioned in the database.** Processes are readable and diffable, and stable for requests already running.
- **A deliberately small workflow model** (two step kinds, four statuses). Easy to reason about and test.
- **Conditions as structured data, not expression strings.** Nothing is evaluated from text, and the editor can show them as dropdowns.
- **SQLite with Dapper behind repositories** (Entity Framework only for the Identity tables). Zero setup for a reviewer, explicit SQL, and a small dialect layer keeps a database change contained.
- **The audit trail is written in the same transaction as the change**, so it records exactly what was committed. Master updates use hooks in that transaction, with conditional updates so two requests cannot take the same item.
- **Soft delete only, and money as whole minor units.** History stays complete and there are no rounding errors.

**Practices:** layers that depend only inward; no magic strings (roles, policies, statuses and error codes are constants); validation at the edge and again in the engine; one exception middleware that never leaks a stack trace; optimistic concurrency with a row version; queries that avoid N+1; safe uploads (generated names, type allow-list, size cap, paths kept inside the data folder); and no secrets in the repository (the API refuses to start in Production with demo mode on unless that is switched on deliberately).

## Checking it

There is an in-process **engine probe** that walks every module through approvals, conditions, documents, master updates and role checks against a throwaway database, asserting the results and audit rows. It is a start-up diagnostic, not a unit-test suite: run the API in Development with `Demo__Enabled=true`, `Diagnostics__RunEngineProbe=true` and a temporary `ADMINDESK_DATA_DIR`, and look for `ENGINE PROBE PASSED` in the log.

## Not built

Left out for time: a JSON definition editor in the app (definitions are files; limits and conditions are editable), a phone-sized layout, and the vehicle, guest house, event, parking, food and uniform processes. Not built yet: the money flows (travel, advances, petty cash) with settlement, purchase and stock, the helpdesk with SLAs and escalation, reminders and notifications, the admin dashboard with exports and budget reports, global search, external integrations, and a unit-test project.

Where the process description was silent I made a call and kept it small: a requester cannot approve their own request, a step with nobody to act on it simply waits, and Management and System admin edit limits together (Management also maintains the masters, with the roles that own them).
