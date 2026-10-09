# AdminDesk

AdminDesk is a single-organisation Admin operations platform built around **one configuration-driven workflow engine**. Employees and managers raise requests (stationery, courier, SIM, laptop and IT assets, ID cards, welfare, housekeeping and more). Each request flows through approvals and tasks to automatic closure, every step is recorded in an append-only audit trail, and who can see or do what is enforced on the server by role.

The central idea: **a process is a definition file, not new application code.** Fields, ordered steps, who acts on each step, conditions and approval limits all live in a JSON file. Adding or changing a process means editing that file. See [docs/definitions.md](docs/definitions.md).

## Context

This project was built as a **practical assignment for a mid-level engineer position**. The brief was a broad description of an Admin department's processes: about two dozen kinds of request (travel, advances, petty cash, SIMs and assets, ID cards, purchase, vendors, a helpdesk and more) together with a list of system controls such as an audit trail, role-based access, approval limits, service levels and reporting. It described the shape of each flow but left many details open (who approves at some steps, the approval limits, the status values), and it was far larger than could be finished in the time available.

So it was planned as a **proof of concept that favours depth over breadth**. The foundation comes first: one configuration-driven workflow engine, the audit trail, role-based access, and approval limits and conditions that are versioned and editable. On top of it sit a representative set of processes that exercise every part of that foundation: approvals, conditional steps, documents, allocation and return, and masters. Everything else is listed plainly under [Not built](#not-built). Where the brief was silent, a small and reasonable assumption was made and written down under [Assumptions](#assumptions), so a reviewer can see what was decided and why.

## Contents

- [Context](#context)
- [What is built](#what-is-built)
- [Requirements](#requirements)
- [Run it locally](#run-it-locally)
- [Demo accounts and a short tour](#demo-accounts-and-a-short-tour)
- [Architecture](#architecture)
- [How the web app and the API talk to each other](#how-the-web-app-and-the-api-talk-to-each-other)
- [How a module is configured](#how-a-module-is-configured)
- [The workflow engine](#the-workflow-engine)
- [Roles and access](#roles-and-access)
- [Design choices and why](#design-choices-and-why)
- [Practices followed](#practices-followed)
- [Configuration](#configuration)
- [Self-check (engine probe)](#self-check-engine-probe)
- [Running it as one process](#running-it-as-one-process)
- [Project layout](#project-layout)
- [Assumptions](#assumptions)
- [Not built](#not-built)

## What is built

**The engine**
- Request IDs generated automatically per module (for example `STN-2026-0001`).
- Approval steps and task steps, with an approve / reject / complete / cancel model. Reject and cancel need a reason.
- Conditional steps ("only if the cost is above the limit"), evaluated when the step becomes next, using form values, values captured by earlier steps, and stored limits.
- Approval limits and step conditions that can be edited in the app, without code changes. Edits apply to new requests only; requests in flight keep the version they started on.
- A complete, append-only audit trail of every action.
- Document upload and download on requests, including steps that require a document before they can complete.
- Optimistic concurrency, so two people acting on the same request cannot overwrite each other.

**The processes (modules)**
Stationery, Courier (with proof of delivery), SIM and SIM return, Laptop / IT asset and Asset return, ID card, Employee welfare, Housekeeping.

**Around the engine**
- Masters for SIMs, assets and ID cards: current holder, status and history, updated by the request flows, maintained by the owning roles.
- Screens: Waiting for me, My requests, Handled by me, All requests, Team requests, People, Masters, Limits and conditions, Module definitions, and dashboards for employees and managers.
- Ten demo roles and a sample organisation of 200 employees, created automatically on first start.

## Requirements

| Needed | Version |
|--------|---------|
| .NET SDK | 10 |
| Node.js | 22 (with npm) |
| Git | any recent version |

No database server, container or cloud account is needed. The data lives in SQLite files created on first start.

Works on Windows, Linux and macOS. The one-command start script is PowerShell for Windows, with a bash equivalent for the others.

## Run it locally

```
git clone <this repository>
cd AdminDesk
scripts/dev.ps1        # Windows PowerShell
scripts/dev.sh         # bash (Git Bash, Linux, macOS)
```

The script restores and builds the backend, installs the frontend packages when they are missing, starts the API on http://localhost:5080 and the web app on http://localhost:4200, and waits. Then open **http://localhost:4200**.

- Stop both with Ctrl+C, or run `scripts/dev.ps1 -Stop` (`scripts/dev.sh stop`) from another window.
- The first start creates the database in `data/`, the sample organisation, the demo users and a spread of demo requests. Delete `data/` to start again from scratch.
- If the API stops at start with a lock timeout straight after a forced stop, wait two minutes and start it again; the background-job storage releases a lock left by a killed process after that time.

**Optional ready-made data.** `sample-data/app.db` is a freshly seeded database (the sample organisation, the demo users and demo requests at different stages). The app creates and seeds its own database on first start, so you do not need it. To use it instead, stop the app and copy it to `data/app.db` before the first start. The app then applies any newer migrations on top. Do not commit changes to it: runs write to `data/`, which is ignored.

Without the script, in two terminals:

```
dotnet run --project backend/src/AdminDesk.Api
cd frontend && npm start
```

## Demo accounts and a short tour

The sign-in screen shows one card per role; clicking a card signs you in. All accounts share the practice password `Demo@12345`, a fake password for demo data that must never be used in a real deployment.

| Role | Name | Email |
|------|------|-------|
| Management | Vikram Rao | management@demo.test |
| Finance | Anita Desai | finance@demo.test |
| HR | Suresh Iyer | hr@demo.test |
| IT | Kavita Shah | it@demo.test |
| Admin | Rahul Verma | admin@demo.test |
| Store | Meera Joshi | store@demo.test |
| Security | Imran Sheikh | security@demo.test |
| System admin | Neha Kulkarni | sysadmin@demo.test |
| Manager | Rohan Kapoor | manager@demo.test |
| Employee | Priya Nair | employee@demo.test |

**Stationery, from submission to closure**
1. As the Employee: New request, Stationery, fill in an item and quantity, submit.
2. As the Manager: approve it from Waiting for me.
3. As Admin or Store: approve the verification step.
4. As Store: confirm stock is available, then mark the items issued.
5. As the Employee: confirm you received them.
6. As Store: confirm the stock update. The request closes by itself.
7. As Admin: open the closed request and read the Audit trail at the bottom.

**A conditional step.** As the Employee raise a Laptop request with a cost, then as the Manager and IT approve it. The Finance step appears only because the cost is above the Finance limit. As Management, open Limits and conditions, raise the limit above that cost, and raise another request: the Finance step is now skipped.

**Allocation.** As IT or Admin, allocate an available laptop on a Laptop request, let the Employee acknowledge it, then open Masters and see the holder and history update. Return it with an Asset return request, recording its condition and any damage cost.

**Documents.** Raise a Courier request, walk it to the proof of delivery step as Admin, and note it cannot complete until a document is uploaded.

## Architecture

```
 Browser (Angular 21 + PrimeNG)
        |   HTTPS, JSON, JWT bearer token
        v
 ASP.NET Core API  (backend/src/AdminDesk.Api)
   controllers, authorisation policies, validation filter,
   exception and correlation-id middleware
        |
 Application   (use cases, rules about who may do what)
   workflow service, query service, documents, masters, limits editor
        |
 Domain        (pure engine logic, no I/O)
   definitions and their validator, step planner, rule evaluator,
   status deriver, step resolver
        |
 Infrastructure (everything that touches the outside world)
   SQLite + Dapper repositories, DbUp migrations, Identity (EF Core),
   Hangfire jobs, file storage, logging sink, seeding, request hooks
        |
 SQLite files in data/   +   uploaded documents on disk
```

- **Five backend projects**, each depending only inward: `Api`, `Application`, `Domain`, `Infrastructure`, `SharedKernel` (constants, enums, exceptions, the money type, the response wrapper). The engine rules in `Domain` can be read and reasoned about without a database.
- **Dapper** for all data access, with SQL behind repository interfaces and a small SQL-dialect layer. **Entity Framework Core is used only for the ASP.NET Core Identity tables** (users, roles, claims).
- **SQLite** in WAL mode for everything, so a fresh clone runs with nothing to install.
- **DbUp** applies numbered, embedded SQL scripts at start, so the schema and the module definitions travel with the build.
- **Hangfire** (with SQLite storage) for background jobs. A short spike confirmed recurring jobs, delayed jobs, restart survival and a ten-minute soak all work on .NET 10; a simple timer-based provider remains available behind the same interface (`Jobs:Provider`).
- **Serilog** for logging, with warnings and errors also batched into a `logs` table together with the request's correlation id.
- **Angular 21** (standalone components, signals, lazy-loaded routes) with **PrimeNG 21** and a custom calm-blue theme.

## How the web app and the API talk to each other

1. The browser loads the Angular app. It calls the API with **relative addresses** (`/api/...`), so it works wherever it is hosted.
2. Sign-in is `POST /api/auth/login`. The API answers with a **JWT** (8-hour lifetime) and the user's roles. The web app keeps it in local storage and an HTTP interceptor adds `Authorization: Bearer ...` to every call.
3. Every response uses one wrapper: `{ "success": true, "data": ..., "error": null }`, or on failure an error code, a message and per-field messages that the form shows beside the right input. The web app turns that into typed results.
4. The API never trusts the screen. Every endpoint carries an **authorisation policy**; hiding a button in the web app is never the only control. The web app reads the roles only to decide what to show.
5. Every response carries an `X-Correlation-ID` header. If something unexpected happens, the screen shows a reference that matches the id stored with the error in the logs.
6. **In development** the Angular dev server (port 4200) forwards `/api` to the API (port 5080) through its proxy settings. **In production** the API serves the built web app itself, so page and API share one origin and no cross-origin settings are needed. See [Running it as one process](#running-it-as-one-process).

## How a module is configured

A module is **one JSON file** in `definitions/`, compiled into the API as an embedded resource. A simplified example (the shipped `definitions/laptop.json` is the full version):

```json
{
  "code": "laptop",
  "version": 1,
  "name": "Laptop or IT asset request",
  "prefix": "LAP",
  "subject": "{assetType} request",
  "fields": [
    { "key": "assetType", "label": "Asset type", "type": "select", "required": true,
      "options": [ { "value": "Laptop", "label": "Laptop" }, { "value": "Desktop", "label": "Desktop" } ] },
    { "key": "estimatedCost", "label": "Cost, if a payment is involved", "type": "money", "min": 0 }
  ],
  "steps": [
    { "key": "manager-approval", "name": "Manager approval", "type": "approval", "actor": { "reportingManager": true } },
    { "key": "it-admin-verification", "name": "IT or Admin verification", "type": "approval", "actor": { "roles": ["IT", "Admin"] } },
    { "key": "finance-approval", "name": "Finance approval", "type": "approval", "actor": { "roles": ["Finance"] },
      "condition": { "field": "estimatedCost", "op": "gt", "limit": "finance-limit" } },
    { "key": "asset-allocation", "name": "Asset allocation", "type": "task", "actor": { "roles": ["IT", "Admin"] },
      "captureFields": [ { "key": "asset", "label": "Asset to allocate", "type": "lookup", "lookupKind": "availableAsset", "required": true } ] }
  ],
  "limits": [ { "stepKey": "finance-approval", "limitKey": "finance-limit", "valueMinor": 0, "unit": "INR" } ]
}
```

What a definition can express:

| Part | What it does |
|------|--------------|
| `fields` | The request form, rendered by one generic form component. Types: text, long text, number, money, date, date-time, yes/no, select, multi-select and lookups (employee, department, project, location, cost centre, available or held SIM and asset). |
| `showWhen` | A field appears only when an earlier answer has a given value; hidden values are ignored on the server too. |
| `requiredCommonFields` | The module insists on common fields (location, project, cost centre) that are optional elsewhere. |
| `defaultFrom` | A text field starts with a value taken from the signed-in user. |
| `steps` | The route, in order. An `approval` step can approve or reject; a `task` step is completed, optionally with `captureFields` the actor must enter. |
| `actor` | The requester's reporting manager, the requester, or anyone holding one of a list of roles. |
| `condition` | Makes a step conditional on a field, a value captured earlier, or a stored limit. |
| `requiresDocument`, `locksCancel`, `actionLabel` | A step that needs an uploaded document; a step after which cancelling is no longer allowed; the text of its button. |
| `limits` | Named amounts that conditions compare against, stored in the database and editable in the app. |

**Versioning.** A definition is stored in the database the first time it is seen. A higher `version` becomes a new row; an existing version is never overwritten. Each request is pinned to the version it was created with, and to a copy of the limits in force at that moment, so editing a module never reroutes work already in flight. Edits made in the app publish a new version the same way.

**Validation.** All definitions are validated when the API starts (known field types, step types, operators and roles; condition references that exist; and so on). A typo stops startup with a message naming the file, instead of surfacing later as a broken form.

The full format, every option and the rules above are in [docs/definitions.md](docs/definitions.md).

## The workflow engine

A request moves through its definition's steps in order.

- **Statuses.** A request is *In progress*, *Closed*, *Rejected* or *Cancelled*. While in progress, the current step's name is what users see. Approval status is *Pending*, *Approved* or *Rejected*.
- **Closing.** The request closes automatically when its last step completes.
- **Routing.** Each step's actors are resolved when it becomes active (the reporting manager is read from the organisation data at that moment). A conditional step is evaluated at that moment too and is marked not required when its condition is false.
- **Rules about who may act.** The actor of the current step may act; the requester may cancel until a `locksCancel` step is done; an Admin may reject at any step as an operational override; nobody may approve or reject their own request at an approval step.
- **Transactions.** Every action runs in one database transaction that also writes its audit events, and calls the request hooks. A change and its audit record cannot get out of step.
- **Hooks.** A small interface (`IRequestHook`) is called inside that transaction when a request is created, when a step is done and when the request ends. The SIM, asset and ID card flows use it to update the masters (holder, status, history, condition and cost) atomically with the step that causes the change. Allocation uses conditional updates, so two requests cannot take the same item.
- **Concurrency.** Each request carries a row version; acting on a stale view returns a conflict instead of overwriting.
- **Money** is held in whole minor units (paise) everywhere and written in rupees at the edges.

## Roles and access

Ten roles: **Employee, Manager, Admin, Finance, HR, IT, Store, Security, Management, System admin.**

- A request can be opened by the person who raised it, by anyone who has acted on it or is currently asked to act on it, by the requester's manager, and by Admin, System admin and Management (read-only). Everyone else gets "not found", which does not reveal that it exists.
- **Waiting for me** lists what needs your action. **Handled by me** lists requests you personally acted on. **All requests** (Admin, System admin, Management) shows everything read-only. **Team requests** shows a manager's reports.
- **Limits and conditions** are edited by Management and System admin. Management owns the money policy and System admin applies it.
- **Masters** are maintained by the roles that own them (SIM: Admin and Management; laptop and IT asset: IT, Admin and Management; ID card: HR, Admin and Management). Others are read-only. Holder and status change only through the request flows.
- **System admin** is a technical role: it configures but takes no business actions and sits in no approval chain.

## Design choices and why

| Choice | Why |
|--------|-----|
| **Definitions as JSON files, stored and versioned in the database** | A process is readable, diffable and reviewable, and changing it needs no code. Pinning each request to a version keeps work in flight stable. |
| **A deliberately small workflow model** (two step kinds, four request statuses) | Everything the processes need fits without a rules language. A small model is easy to reason about, test and explain. |
| **Conditions as structured data, not expression strings** | Nothing is evaluated from text, so there is nothing to inject and the editor can show conditions as dropdowns. |
| **SQLite + Dapper** | Zero setup for anyone who clones the repository, explicit and reviewable SQL, no hidden queries. A dialect layer isolates the few SQLite-specific parts so a different database is a contained change. |
| **Entity Framework only for Identity** | Identity needs it; everything else benefits from plain SQL, batched queries instead of N+1, and server-side paging. |
| **DbUp with embedded scripts** | The schema is versioned with the code and applied the same way everywhere, including a clean clone. |
| **JWT bearer, server-side policies** | Simple and stateless for a single API. Authorisation is checked on the server for every endpoint. |
| **One response wrapper with error codes and field errors** | The web app handles every failure the same way and can show a message beside the exact field. |
| **Audit written in the same transaction** | The trail is trustworthy: it records exactly what was committed. |
| **Soft delete everywhere** | Nothing is destroyed. Masters are retired with a flag and a timestamp, documents are marked removed, and history stays complete. |
| **Money as integer minor units** | No floating-point rounding in limits, conditions or totals. |
| **Hooks inside the transaction** | Master updates cannot drift from the request that caused them. |
| **Single-process production hosting** | The API serves the built web app, so there is one thing to run, one address, and no cross-origin configuration. |
| **Signals and standalone components in Angular** | Less boilerplate and clear local state. Role groups are constants shared by the router guards and the menu. |

## Practices followed

- **Layering with inward dependencies**, repository interfaces in the application layer, and no data access in controllers.
- **No magic strings.** Roles, policies, statuses, error codes, audit event types, lookup kinds and step and field types are constants or enums on both sides.
- **Validation at the edge**, with FluentValidation filters returning field-level messages, and again inside the engine for the rules that matter.
- **One exception-handling middleware** maps typed domain exceptions (not found, validation, conflict, rule refused) to consistent responses; unexpected errors become a generic message plus a correlation id, never a stack trace.
- **Correlation id on every request**, in logs, in the response header and in the stored error record.
- **No secrets in the repository.** The shipped signing key is a labelled development placeholder; production supplies its own through the environment. The API refuses to start in Production with demo mode on unless that is switched on deliberately.
- **Safe uploads:** server-generated file names, a type allow-list, a size cap applied before the body is read, paths checked to stay inside the data folder, and downloads sent as attachments with `nosniff`.
- **Queries written to avoid N+1**: joins, batched lookups and aggregate queries for counters; sort columns come from a fixed list and values are always parameters.
- **Concurrency safety** through row versions and guarded updates, not through hoping two people never click at once.
- **Idempotent start-up**: migrations, definition sync and seeding can run on every start without duplicating anything.

## Configuration

| Key | Meaning |
|-----|---------|
| `Demo:Enabled` | Default true. Creates the sample organisation, demo users, the role cards and demo requests. Set it to false in any real deployment. |
| `Demo:AllowInProduction` | In a Production environment the API refuses to start with demo mode on unless this is true. |
| `Jwt:SigningKey` | Token signing key (at least 32 bytes). Supply a private one through the environment (`Jwt__SigningKey`) whenever demo mode is off. |
| `Bootstrap:AdminEmail`, `Bootstrap:AdminPassword` | Create the first system administrator when demo mode is off and no user exists. |
| `Storage:DataDirectory` / `ADMINDESK_DATA_DIR` | Where the databases and uploaded documents live (default `data/`). |
| `ADMINDESK_LOG_DIR` | Where the rolling log files go (default `logs/`). |
| `Jobs:Provider` | `Hangfire` (default) or `Timer`. |
| `Definitions:OverrideDirectory` | Extra or replacement definition files, for trying a sample module. |
| `Logging:Db:*` | Switch and retention for the `logs` table (App 30 days, Email 365 by default). |

Any key can be set as an environment variable with double underscores, for example `Demo__Enabled=false`.

## Self-check (engine probe)

Alongside the app there is an in-process **engine probe**: a scripted run that creates requests and walks every module through approvals, tasks, conditions, documents, master updates and role checks, asserting the results and the audit trail. It runs against a throwaway database.

It is a start-up diagnostic, not a unit-test suite, and it calls the services directly rather than over HTTP. To run it, start the API once in the Development environment with demo mode on:

```
set ASPNETCORE_ENVIRONMENT=Development
set Demo__Enabled=true
set Diagnostics__RunEngineProbe=true
set ADMINDESK_DATA_DIR=%TEMP%\admindesk-probe
dotnet run --project backend/src/AdminDesk.Api
```

and read the log for `ENGINE PROBE PASSED (N checks)` or the failing check. The probe refuses to run in any other environment. `node scripts/check-field-types.mjs` separately checks that the form renderer covers every field type.

## Running it as one process

For a deployment, build the web app and let the API serve it:

```
cd frontend && npm run build
```

Copy the contents of `frontend/dist/admindesk-web/browser` to a `wwwroot` folder next to the published API. The API then serves the web app itself: page addresses return the start page, files are served as files, and `/api` keeps answering JSON. Sign-in is still required for everything except the sign-in page and its files.

For a real deployment set `Demo__Enabled=false`, a private `Jwt__SigningKey` and the `Bootstrap__Admin*` values, and serve the app over HTTPS with a content security policy (the token is kept in local storage, which scripts on the page can read; its 8-hour lifetime limits the exposure).

## Project layout

```
backend/src/
  AdminDesk.Api/             controllers, policies, middleware, hosting of the web app
  AdminDesk.Application/     workflow service, queries, documents, masters, limits editor
  AdminDesk.Domain/          definitions, step planner, rule evaluator, status deriver
  AdminDesk.Infrastructure/  repositories, migrations, identity, jobs, storage, seeding, hooks
  AdminDesk.SharedKernel/    constants, enums, exceptions, money, response wrapper
frontend/src/app/
  core/                      API clients, auth, constants, navigation, theme
  features/                  one folder per screen area (inbox, requests, masters, limits...)
  shared/                    reusable components (dynamic form fields, dialogs, tags)
definitions/                 the modules, one JSON file each
docs/definitions.md          the definition format
scripts/                     dev.ps1 / dev.sh, the field-type check, sample definitions
```

## Assumptions

The process description leaves some things open. What was chosen:

1. **The workflow model is deliberately small.** Two step kinds (approval and task), four request statuses, three approval statuses. There is no draft, no editing after creation and no comment thread. Reject and cancel, both with a reason, are the smallest rule set that lets a request end early.
2. **Cancel** is for the requester only, with a reason, while the request is in progress and until a step marked `locksCancel` is done (issuing the item for stationery, dispatch for courier). Nobody else can cancel someone's request. **Reject** is allowed to whoever holds an approval step at that step, and to the Admin at any step; it is never locked.
3. **Nobody can approve or reject their own request** at an approval step, even when they hold the role of that step. The process description does not say either way; this is the safer reading. Task steps are not affected.
4. **Visibility.** See [Roles and access](#roles-and-access). Seeing a request never gives the right to act on it. Role changes apply at the next sign-in.
5. **A step with nobody to act on it** (for example a manager step for someone with no manager) simply waits; nothing is skipped automatically. The top of the demo hierarchy is a person with no login, so a request raised by the Management demo user waits at its first manager step.
6. **Acknowledgements.** The requester's receipt and delivery confirmations are the digital acknowledgement; time and name are recorded on the step and in the audit trail.
7. **Closure** is automatic when the last step completes.
8. **Actors the description does not name:** Welfare approval is the reporting manager; courier proof of delivery is uploaded by the Admin after the requester confirms delivery; a SIM return is handed in by the employee and verified by the Admin; Laptop and asset steps are shared by IT and Admin; ID card verification is HR, printing and handover Admin; SIM verification, allocation and activation are Admin steps; Housekeeping has no approval step and is handled by the Admin.
9. **Finance on SIM and Laptop** is a step taken from the approval matrix, because the individual flows do not show it. It is required only when the entered cost is above the Finance limit, which is zero by default (any payment involved). An empty cost never triggers it. The cost field is an addition to those forms so the rule has a value to compare.
10. **The approval matrix is seeded only for the modules that exist** (SIM and Laptop or IT asset). The rows for the other processes arrive with those modules.
11. **Limits and step conditions** are edited by Management and System admin together; the description names no owner. Every change is audited with old and new values and applies to new requests only. A definition file whose version is not higher than a stored edit is ignored, so bump the file version above the stored one.
12. **Management is read-only across the organisation** and approves only at steps assigned to it. **System admin** takes no business actions. Only the Admin has the operational reject override.
13. **Documents** are stored on local disk under the data folder. PDF, image and Office files up to 5 MB are accepted. Removing one is a soft delete that keeps the file and the audit rows; every download is audited. Management and System admin are read-only; a document can be tagged to a step only when it is the current step and the uploader can act on it.
14. **Masters** (SIM, laptop and IT asset, ID card) are seeded sample data. Records are maintained by their owning roles; retiring sets an inactive flag and a timestamp and is refused while the item is held; every change is audited with old and new values. The description names no maintainer beyond the Admin, IT and HR steps. Returns record condition and any damage or loss cost, and an ID card replacement keeps the old card as Replaced. Identifiers (SIM number, mobile number, asset tag, serial number, card number) are unique, ignoring case and spaces.
15. **Handled by me** shows only requests the signed-in person personally dealt with (approved, completed, rejected, cancelled or closed a step, or is or was named as the actor of a step). Holding a role that has or had a step does not count, and neither does raising a request.
16. **The people directory** is open to managers (their reports), Admin, HR, Management and System admin. Email addresses are not shown.
17. **Money amounts** are limited to one billion rupees per field.
18. **Stock availability and the stock update** steps are manual Store confirmations until stock exists in the system.

## Not built

Left out on purpose (time):

- a JSON definition editor in the app (definitions are viewed on the Module definitions page and edited as files; limits and conditions can be edited in the app)
- a phone-sized layout for approvals
- the vehicle master and trips, guest house rooms
- event, parking, food and pantry, vehicle, and uniform and PPE modules

Not built yet:

- the money flows (travel, travel advance, petty cash, general advance) with settlement, recovery schedules and the petty cash register
- purchase, vendors and contracts, and the stock master with its automatic stock effects
- the helpdesk with service levels and escalation; the SLA column in Waiting for me is a placeholder showing "Not set"
- scheduled reminders and notifications, and the jobs dashboard
- the admin dashboard, exports and reports, budget control and cost allocation reports
- a global search and a settings page
- integration with external systems (ERP, payroll) and SMS
- a unit-test project; behaviour is covered by the engine probe described above
