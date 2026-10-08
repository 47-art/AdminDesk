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

Courier: raise a Courier request as the Employee, describing what is being sent in the "What are you sending?" box. As Admin, select the courier (entering the courier company), mark it dispatched and save the tracking number. As the requester, confirm delivery. The request closes and the timeline shows the courier company and the tracking number.

Reject and Cancel both need a reason. Whoever holds an approval step can reject at it, and the Admin can reject at any step, task steps included. Only the requester can cancel, while the request is in progress and until the step that hands over the item is done (Issue material for stationery, Dispatch for courier). After that Cancel is no longer offered to anyone; Reject is never locked.

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
2. Cancel is allowed while the request is in progress, by the requester only, and always with a reason, until the step that hands over the item is done (Issue material for stationery, Dispatch for courier); after that Cancel is no longer offered and the server refuses it. Nobody else can cancel someone's request, not the Admin, System admin or Management. The lock is read from the definition version the request was created with, so a request raised before that version keeps the old rule. A manager cannot cancel a report's request. Reject is allowed to whoever holds an approval step at that step, and to the Admin at any step of an in-progress request (System admin and Management cannot reject on other people's requests); it is never locked. Any reject, at an approval step or a task step, ends the request as Rejected with approval status Rejected, so it is never counted as Approved or Pending; a cancel keeps the approval status it had. The audit trail records who acted, in which role (Admin, Requester or the step's role) and the reason, and the requester sees both on the timeline.
3. Visibility: a request can be opened by the person who raised it, by anyone who has acted on it or is asked to act on it, by the manager of the person who raised it (read-only, apart from their own step actions) and by Admin, System admin and Management, who see every request read-only. Everyone else gets "not found". The dashboard counters show the signed-in person's own requests, except for those three roles, who see the whole organisation. They also get an All requests page (not the System admin, whose menu is minimal; its API access stays read-only), and managers get a Team requests page with the requests of their direct reports. Seeing a request gives no right to act on it.
4. Role changes apply at the next sign-in.
5. A step's actors may be a list of roles; anyone holding one of them can act.
6. The courier delivery confirmation is done by the requester, because the process description does not say who confirms delivery.
7. The requester's receipt confirmation and delivery confirmation are the digital acknowledgement: the time and the name are recorded on the step and in the audit trail.
8. Stock availability and the stock update step are manual Store confirmations until stock arrives in the system.
9. Closure is automatic when the last step completes.
10. Waiting for me is open to every role, so a requester sees their own confirmation tasks there. Its SLA column is a placeholder.
11. A step that nobody can act on, for example a reporting-manager approval for someone who has no manager, simply waits; nothing is skipped automatically. The person who raised it can cancel it with a reason. A request raised by the Management demo user waits at its first step for exactly this reason: that user's reporting manager is the top of the hierarchy (employee E0011), who has no login and no demo account.
12. The Audit trail section on the request page is visible to Admin, System admin and Management only.
13. Limits and step conditions are edited on the Limits and conditions page by Management and System admin together. The process description does not name an owner: Management owns the money policy and System admin applies it. Management stays read-only everywhere else, and every change is in the audit trail with the old and new values. Edits apply to new requests only: limits are copied onto each request when it is created, and a condition edit publishes a new definition version, so requests in progress keep theirs. A definition file whose version is equal to or lower than a stored edit is ignored, so bump the file version above the stored one.
14. The Management role is read-only across the organisation (every request, the All requests page, organisation counters and the audit trail) and approves only at steps assigned to it. No shipped module routes to Management, so a Management login sees an empty Waiting for me list. The System admin is a technical role: it configures definitions and limits and sees the Module definitions page, takes no business actions and is not part of any approval chain. The request lists and New request are closed to it, while a direct link to a request still opens read-only for support. Only the Admin has the operational override (reject at any step).
15. Segregation of duties: the process description does not say whether a person may approve their own request, so nobody can approve or reject their own request at an approval step, even when they hold the role of that step (for example a Store person raising a stationery request cannot do the Admin or Store verification; an Admin can). Task steps (issuing, stock confirmation, receipt confirmation) are not affected, and the Admin's operational reject override still applies.
16. The people directory (the People page in the menu) is open to managers, Admin, HR, Management and System admin. Managers see their direct reports; the others see everyone. Each row shows the code, name, designation, department and location; email addresses are not shown.
17. The process description does not say who completes the courier proof of delivery upload, so it is a task for the Admin, after the requester has confirmed delivery. The step cannot be completed until a document has been uploaded for it.
18. Finance approval on the SIM and Laptop requests is a step taken from the approval matrix, because the individual flows do not show it. It is required only when the entered cost is above the Finance limit, which is zero by default (any payment involved); an empty cost never triggers it. Management is not in those rows. The cost field is an addition to the two forms so the "if required" rule has a value to compare.
19. The approval matrix is seeded only for the modules that exist now (SIM and Laptop or IT asset). The rows for Travel, Advances, Petty Cash, Purchase, Maintenance and the others arrive with those modules.
20. Actors the process description does not name: Welfare approval is by the reporting manager; Courier proof of delivery is uploaded by the Admin; a SIM return is handed in by the employee and then verified by the Admin; the Laptop and asset steps are shared by IT and Admin; ID card verification is by HR and printing and handover by Admin; SIM verification, allocation and activation are Admin steps; housekeeping has no approval step and is handled by the Admin; acknowledgement and confirmation steps belong to the requester.
21. SIM return and Asset return are separate modules from the request flows. A SIM replacement or transfer request records its type in the history, and the old SIM is returned through a SIM return request.
22. Nobody can approve or reject their own request at an approval step, even when they hold the role of that step.
23. Documents are stored on local disk under the data folder. PDF, image and Office files up to 5 MB are accepted. Removing a document is a soft delete that keeps the file and the audit rows, every download is audited, and Management and System admin are read-only. The ID card photo is attached as a supporting document.
24. Masters (SIM, laptop and IT asset, ID card) are seeded sample data shown on the Masters page. Records are maintained by their owning roles, and Management can manage all of them: the SIM master by Admin and Management; the laptop and IT asset master by IT, Admin and Management; the ID card master by HR, Admin and Management. Everyone else, System admin included, is read-only, and HR sees the ID card master only. Retiring a record sets an inactive flag and a timestamp (nothing is deleted) and is refused while the item is held. Every change is audited with the old and new values, and holder and status change only through the request flows. The process description names no maintainer beyond the Admin, IT and HR steps. Asset condition and any damage or loss cost are recorded at return, and an ID card replacement keeps the old card as Replaced.
25. The Handled by me list (IT, HR, Finance, Store, Security and managers) shows only requests the signed-in person personally dealt with: they approved, completed, rejected, cancelled or closed a step, or they are or were named as the actor of a step by name (for example as the reporting manager). Holding a role that has or had a step does not count, and neither does raising a request. Everyone on that list can open the request later. A role holder sees a request only while a step assigned to that role is waiting.

## Not built in this release

Left out on purpose:

- the JSON definition editor (definitions are viewed on the Module definitions page and edited as files)
- a phone-sized layout for approvals
- the vehicle master and trips
- guest house rooms
- event, parking, food and pantry, vehicle, and uniform and PPE modules

Not built yet:

- Settings page and global search
- the jobs dashboard
- the automatic stock effect of the stock update step
- notifications and background business jobs
- the remaining modules
- service levels: the SLA column in Waiting for me is a placeholder showing "Not set", and the age colours there use placeholder thresholds

## Checks

`node scripts/check-field-types.mjs` checks that the form renderer covers every field type.
