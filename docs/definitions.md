# Module definitions

Every request type (a module) is one JSON file in the `definitions/` folder. Adding or changing a process means adding or editing a file; no code changes. The shipped files are `definitions/stationery.json` and `definitions/courier.json`.

## File format

```json
{
  "code": "stationery",
  "version": 1,
  "name": "Stationery request",
  "description": "Request office stationery.",
  "category": "Assets and equipment",
  "icon": "pi-pencil",
  "prefix": "STN",
  "subject": "{item} x {quantity}",
  "fields": [
    { "key": "item", "label": "Item", "type": "text", "required": true, "maxLength": 120, "section": "Items" },
    { "key": "quantity", "label": "Quantity", "type": "number", "required": true, "min": 1, "max": 500, "section": "Items" }
  ],
  "steps": [
    { "key": "manager-approval", "name": "Manager approval", "type": "approval", "actor": { "reportingManager": true } },
    { "key": "stock-check", "name": "Stock availability", "type": "task", "actor": { "roles": ["Store"] }, "actionLabel": "Confirm stock available" }
  ],
  "limits": []
}
```

| Key | Meaning |
|-----|---------|
| `code` | Unique module code used in routes and request numbers. |
| `version` | Whole number. See versioning below. |
| `prefix` | Letters at the start of the request number, for example `STN-2026-0001`. |
| `subject` | Template for the one-line title; `{fieldKey}` is replaced by the entered value. |
| `fields` | The form, in display order. |
| `steps` | The route the request takes, in order. |
| `limits` | Optional amounts that conditional steps compare against. |

## Field types

`text`, `longText`, `number`, `money`, `date`, `dateTime`, `yesNo`, `select`, `multiSelect` and `lookup`. Common properties: `key`, `label`, `type`, `required`, `section` (the heading it is grouped under) and `fullWidth`. Text accepts `maxLength`; number and money accept `min` and `max`; `select` and `multiSelect` take an `options` list of `value` and `label`; `lookup` takes a `lookupKind`.

Lookup kinds are registered in code. Five exist today: `employee`, `department`, `project`, `location` and `costCentre`.

Money is written in rupees in requests and in rule values (for example `12.50`) and is held internally as whole paise. Limits are written in paise as `valueMinor`.

The sample `scripts/fixtures/fieldtypes/fieldcheck.json` uses every field type.

## Steps

A step is either an `approval` (the actor can approve or reject, and a reject needs a reason) or a `task` (the actor completes it). The actor is one of:

- `{ "reportingManager": true }` the requester's reporting manager,
- `{ "requester": true }` the person who raised the request,
- `{ "roles": ["Admin", "Store"] }` anyone holding one of the roles.

A task step may carry `actionLabel`, the text of its one-click button, and `captureFields`: values the actor must enter when completing the step. The courier definition is the example: the selection step captures the courier company and the tracking-number step captures the tracking number. Captured values are shown on the request timeline and recorded in the audit trail.

The last step to complete closes the request automatically. A step that nobody can act on (for example a manager step for someone who has no manager) simply waits.

## Conditions

A step may have a `condition`. It is evaluated when the step becomes the next one to activate, so a rule may read a value captured by an earlier step, written as `stepKey.fieldKey`; until then the step shows as upcoming. A step whose condition is false is skipped.

```json
{ "condition": { "field": "inspection.damaged", "op": "eq", "value": true } }
{ "condition": { "field": "amount", "op": "gt", "limit": "amount-limit" } }
```

Operators: `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `in`, `notIn`, `isEmpty`, `isNotEmpty`. The right-hand side is either a literal `value` or the name of a `limit`.

## Limits

```json
"limits": [
  { "stepKey": "high-value-approval", "limitKey": "amount-limit", "valueMinor": 5000000, "unit": "INR" }
]
```

Limits are optional; the shipped modules declare none. Once stored they are held in the database and can be edited there. The sample `scripts/fixtures/routing/routingcheck.json` has a conditional step and a sample limit.

## Add-on seam

An `IRequestHook` interface is called inside the transaction of each action (when a request is created, when a step is done and when the request ends). Nothing implements it yet and a definition has no effects key.

## Versioning

A definition is stored in the database the first time it is seen. A higher `version` is inserted as a new row; a version already in the database is never overwritten. Requests in flight stay on the version they were created with.

## Trying a sample definition

Set `Definitions:OverrideDirectory` to a folder that holds extra files; files there are added to the shipped set, or replace a shipped file of the same name. For example, to try every field type:

```
Definitions__OverrideDirectory=scripts/fixtures/fieldtypes
```

set as an environment variable before starting the API, then open New request and pick "Field type check". The same works for `scripts/fixtures/routing` (`routingcheck.json`). `node scripts/check-field-types.mjs` checks that the form renderer covers every field type used by the field type sample.
