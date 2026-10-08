# AdminDesk

AdminDesk is a single-organisation Admin operations platform built around one configuration-driven workflow engine. Employees raise requests, the requests flow through approvals and tasks to automatic closure, and every step is recorded in an audit trail. A process is a definition file, not a new application: see [docs/definitions.md](docs/definitions.md).

This release ships two processes on the engine, Stationery and Courier, with demo data so the screens are not empty.

## Prerequisites

- .NET 10 SDK
- Node 22 with npm

## Quick start

```
git clone <this repository>
cd AdminDesk
scripts/dev.ps1        # Windows PowerShell
scripts/dev.sh         # bash (Git Bash, Linux, macOS)
```

The script restores and builds the backend, installs the frontend packages when needed, starts the API on http://localhost:5080 and the web app on http://localhost:4200, and waits. Press Ctrl+C to stop both, or run `scripts/dev.ps1 -Stop` / `scripts/dev.sh stop` from another window.

If you prefer plain commands, in two terminals:

```
dotnet run --project backend/src/AdminDesk.Api
cd frontend && npm start
```

Then open http://localhost:4200. The first start creates the database in `data/`, the sample organisation (200 employees), the demo users and a spread of demo requests.

If the API stops at start with a lock timeout straight after a forced stop, wait two minutes and start it again; the background-job storage releases a lock left by the killed process after that time.

## Demo accounts

The login screen shows one card per role; clicking a card signs you in at once. All accounts share the practice password `Demo@12345`. It is a fake password for local demo data and must never be used for a real deployment.

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

## Walkthrough

Stationery, from submission to closure:

1. Click the Employee card, choose New request, then Stationery, fill in an item and a quantity and submit.
2. Sign in as the Manager and approve the request from Waiting for me.
3. As Admin or Store, approve the verification step.
4. As Store, confirm that stock is available, then mark the items as issued.
5. As the Employee, confirm that you received the items.
6. As Store, confirm the stock update. The request closes by itself.
7. As Admin, open the closed request and read the Audit trail section at the bottom of the page.

Courier: raise a Courier request as the Employee. As Admin, select the courier (entering the courier company), mark it dispatched and save the tracking number. As the requester, confirm delivery. The request closes and the timeline shows the courier company and the tracking number.

Reject and Cancel both need a reason. The Manager can reject at an approval step; the requester can cancel at any time while the request is in progress.

## How a module is defined

A module is one JSON file: its fields, its steps, who acts on each step and any values a step captures. [docs/definitions.md](docs/definitions.md) describes the format. To try the sample that uses every field type, set `Definitions__OverrideDirectory=scripts/fixtures/fieldtypes` before starting the API (any folder of definition files works the same way; files there are added to the shipped set, or replace a shipped file of the same name).

## Architecture

- Five backend projects: Api, Application, Domain, Infrastructure and SharedKernel, with the web app in `frontend/`.
- SQLite with Dapper for data access; the schema is created by numbered DbUp scripts at start. ASP.NET Core Identity (through Entity Framework) holds users, roles and claims only.
- Angular 21 with PrimeNG for the web app, JWT bearer sign-in.
- Background jobs: a spike of four checks on SQLite (recurring job, delayed job, restart survival and a ten-minute soak) all passed, so Hangfire stays the default provider; see [docs/hangfire-spike.md](docs/hangfire-spike.md). The jobs dashboard comes with a later release and is not served in this one.
- A different database is a contained change: [docs/postgresql-path.md](docs/postgresql-path.md).

## Configuration

| Key | Meaning |
|-----|---------|
| `Demo:Enabled` | Default true. Creates the sample organisation, the demo users, the role cards and the demo requests. Set it to false in any deployment. |
| `Jwt:SigningKey` | Signing key for tokens. Supply a private key through the environment (`Jwt__SigningKey`) whenever demo mode is off; the shipped value is a development placeholder. |
| `Bootstrap:AdminEmail`, `Bootstrap:AdminPassword` | Create the first system administrator when demo mode is off and no user exists. |
| `Jobs:Provider` | `Hangfire` (default) or `Timer`. |
| `Definitions:OverrideDirectory` | Extra or replacement definition files. |

Any key can be set as an environment variable with double underscores, for example `Demo__Enabled=false`.

## Production

Build the web app and copy its output next to the API:

```
cd frontend
npm run build
```

Copy the contents of `frontend/dist/admindesk-web/browser` into `backend/src/AdminDesk.Api/wwwroot`. The API then serves the web app itself: unknown paths return the start page, `/api` keeps answering JSON, and `/hangfire` is not served.

## Logging

Every log line goes to the console and to a daily rolling file under `logs/`. Warnings and errors are additionally written in batches, together with the correlation id of the request, to the `logs` table of the application database. No screen or endpoint shows that table yet; read it with any SQLite tool. Every response carries an `X-Correlation-ID` header that matches the stored correlation id, so a reported error can be found from the id. Information-level request logs go to the console and the file only.

The table keeps App events for `Logging:Db:RetentionDays:App` days (default 30) and Email events for `Logging:Db:RetentionDays:Email` days (default 365); older rows are purged when the API starts. `Logging:Db:Enabled` switches the table writing off. Email sending events will be recorded in the same table in a later release.

## Security notes

- The JWT is kept in the browser's local storage, which scripts on the page can read. The token lifetime is short (8 hours). For a production deployment, serve the app with a content security policy.
- Demo accounts and the shared practice password exist only while `Demo:Enabled` is true. The API logs a warning if demo mode is on in a Production environment.
- Authorisation is enforced on the server; hiding a button in the web app is never the only control.

## Assumptions

The process description leaves some things open. What was chosen:

1. The workflow model is deliberately small: two step kinds, approval and task; four request statuses (In progress, Closed, Rejected and Cancelled); three approval statuses (Pending, Approved and Rejected). While a request is in progress, the name of the current step is the status shown. There is no draft, no editing after creating and no comment thread. Reject and Cancel, both with a reason, are not described in the process description and were added as the smallest possible rule set.
2. Cancel is allowed at any time while the request is in progress, by the requester, and always with a reason.
3. Visibility: a request can be opened by its requester, by anyone who has acted on it or is asked to act on it, and by Admin and System admin.
4. Role changes apply at the next sign-in.
5. A step's actors may be a list of roles; anyone holding one of them can act.
6. The courier delivery confirmation is done by the requester, because the process description does not say who confirms delivery.
7. The requester's receipt confirmation and delivery confirmation are the digital acknowledgement: the time and the name are recorded on the step and in the audit trail.
8. Stock availability and the stock update step are manual Store confirmations until stock arrives in the system.
9. Closure is automatic when the last step completes.
10. Waiting for me is open to every role, so a requester sees their own confirmation tasks there. Its SLA column is a placeholder.
11. A step that nobody can act on, for example a reporting-manager approval for someone who has no manager, simply waits; nothing is skipped automatically. The person who raised it can cancel it with a reason. A request raised by the Management demo user waits at its first step for exactly this reason: that user's reporting manager is the top of the hierarchy (employee E0011), who has no login and no demo account.
12. The Audit trail section on the request page is visible to Admin and System admin only.
13. Approval limits are held in the database (the shipped modules declare none) and can be edited there until the limits editor exists.
14. The Management role exists, but no shipped module routes to Management yet: approval by Management arrives with the modules whose approval rules need it, so a Management login sees an empty Waiting for me list.

## Not yet built

- Settings and Masters pages, global search
- the jobs dashboard
- a definitions viewer and a limits editor
- document upload: the documents slot on the request page is reserved, and supporting documents are only partially covered until upload exists
- the proof-of-delivery upload step of the courier flow (it arrives with document upload)
- the automatic stock effect of the stock update step
- notifications and background business jobs
- other modules
- service levels: the SLA column in Waiting for me is a placeholder showing "Not set" until a later phase adds them, and the age colours there use placeholder thresholds

## Checks

`scripts/smoke.sh` walks both processes over HTTP as the demo roles against a running API with a fresh database (`API_URL` selects the address). `node scripts/check-field-types.mjs` checks that the form renderer covers every field type.
