#!/usr/bin/env bash
# HTTP walkthrough of the request API as the demo roles.
#
# Prerequisites: a running API (default http://localhost:5080, override with API_URL) with
# demo mode on and a fresh data folder, bash, curl and node (used to read JSON).
#
# Optional environment:
#   API_URL           base address of the API
#   DB_PATH           application database file; enables the audit row, audit stamp and logging checks
#   SMOKE_FIXTURES=1  also run the money checks (the API must have been started with
#                     Definitions__OverrideDirectory holding a copy of scripts/fixtures/routing/routingcheck.json)
#   SMOKE_ADMIN_EMAIL, SMOKE_ADMIN_PASSWORD   sign-in of an administrator without an employee record
#                     (used by the no-employee mode)
#
# Modes:
#   main (default)   walks Stationery and Courier from creation to the end as the demo roles
#   no-employee      checks the behaviour of a sign-in that has no employee record
#
# Every request that the script looks for in a list is found by the id the script received
# when it created that request, so demo data and earlier runs cannot confuse an assertion.
# Prints one PASS or FAIL line per check and a summary; exits non-zero when any check failed.

MODE="${1:-main}"
API_URL="${API_URL:-http://localhost:5080}"
DEMO_PASSWORD="Demo@12345"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/.." && pwd)"

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
if command -v cygpath >/dev/null 2>&1; then TMP_W="$(cygpath -m "$TMP")"; else TMP_W="$TMP"; fi

PASS=0
FAIL=0
STATUS=""
BODY=""

# ------------------------------------------------------------------ helpers

pass() { PASS=$((PASS + 1)); echo "PASS $1"; }
fail() { FAIL=$((FAIL + 1)); echo "FAIL $1 ${2:-}"; }

assert_eq() { # name actual expected
  if [ "$2" = "$3" ]; then pass "$1"; else fail "$1" "(expected '$3', got '$2')"; fi
}

assert_contains() { # name haystack needle
  case "$2" in *"$3"*) pass "$1" ;; *) fail "$1" "(missing '$3' in '$2')" ;; esac
}

assert_not_contains() { # name haystack needle
  case "$2" in *"$3"*) fail "$1" "(found '$3')" ;; *) pass "$1" ;; esac
}

assert_match() { # name actual regex
  if printf '%s' "$2" | grep -Eq "$3"; then pass "$1"; else fail "$1" "('$2' does not match $3)"; fi
}

assert_true() { # name json-expression-result
  if [ "$2" = "true" ]; then pass "$1"; else fail "$1" "(got '$2')"; fi
}

# Reads JSON from stdin and prints the value of a JavaScript expression over it (named d).
jx() { # json expression
  printf '%s' "$1" | node -e '
let s = "";
process.stdin.on("data", c => s += c).on("end", () => {
  let v;
  try { const d = JSON.parse(s); v = new Function("d", "return (" + process.argv[1] + ")")(d); } catch (e) { v = undefined; }
  console.log(v === undefined || v === null ? "null" : (typeof v === "object" ? JSON.stringify(v) : String(v)));
});' "$2"
}

jget() { jx "$BODY" "$1"; }

# call METHOD PATH [TOKEN] [JSON-BODY]  ->  STATUS, BODY, response headers in $TMP/hdr
call() {
  local method=$1 path=$2 token=${3-} body=${4-}
  local args=(-s -m 60 -D "$TMP_W/hdr" -o "$TMP_W/resp" -w '%{http_code}' -X "$method" "$API_URL$path")
  if [ -n "$token" ]; then args+=(-H "Authorization: Bearer $token"); fi
  if [ -n "$body" ]; then args+=(-H 'Content-Type: application/json' --data-binary "$body"); fi
  if [ -n "${EXTRA_HEADER-}" ]; then args+=(-H "$EXTRA_HEADER"); fi
  STATUS=$(curl "${args[@]}")
  BODY=$(cat "$TMP/resp")
}

header_value() { grep -i "^$1:" "$TMP/hdr" | head -n 1 | cut -d: -f2- | tr -d '\r ' ; }

login() { # email -> token
  call POST /api/auth/login "" "{\"email\":\"$1\",\"password\":\"${2:-$DEMO_PASSWORD}\"}"
  jget d.data.accessToken
}

dbval() { # sql -> first column of the first row
  dbquery "$DB_PATH" "$1" 2>&1 | sed -n 2p | cut -f1
}

long_text() { head -c "$1" /dev/zero | tr '\0' 'x'; }

assert_status() { assert_eq "$1" "$STATUS" "$2"; }

assert_error() { # name status code
  if [ "$STATUS" = "$2" ] && [ "$(jget d.error.code)" = "$3" ]; then pass "$1"; else fail "$1" "(expected $2 $3, got $STATUS $(jget d.error.code))"; fi
}

assert_field_error() { # name field
  if [ "$STATUS" = "400" ] && [ "$(jget "d.error.fieldErrors.some(f => f.field === '$2')")" = "true" ]; then
    pass "$1"
  else
    fail "$1" "(expected 400 with a field error on '$2', got $STATUS $BODY)"
  fi
}

# ------------------------------------------------------------ request helpers

# act ID TOKEN ACTION [COMMENT-JSON] [CAPTURED-JSON]  - sends the current row version.
act() {
  local id=$1 tok=$2 action=$3 comment=${4-null} captured=${5-null}
  call GET "/api/requests/$id" "$T_SYS"
  local rv
  rv=$(jget d.data.rowVersion)
  act_rv "$id" "$tok" "$action" "$rv" "$comment" "$captured"
}

act_rv() { # id token action rowVersion [comment-json] [captured-json]
  local id=$1 tok=$2 action=$3 rv=$4 comment=${5-null} captured=${6-null}
  call POST "/api/requests/$id/actions" "$tok" \
    "{\"action\":\"$action\",\"comment\":$comment,\"rowVersion\":$rv,\"captured\":$captured}"
}

detail() { call GET "/api/requests/$1" "${2:-$T_SYS}"; }

inbox_find() { # id token -> JSON of this request's inbox row, or null
  local id=$1 tok=$2 page=1 row more
  while :; do
    call GET "/api/requests/inbox?page=$page&pageSize=100" "$tok"
    row=$(jget "d.data.items.find(i => i.id === $id)")
    if [ "$row" != "null" ]; then echo "$row"; return; fi
    more=$(jget 'd.data.page * d.data.pageSize < d.data.total')
    if [ "$more" != "true" ]; then echo null; return; fi
    page=$((page + 1))
  done
}

stn_body() { # item quantity [remarks]
  local remarks=null
  if [ -n "${3-}" ]; then remarks="\"$3\""; fi
  printf '{"moduleCode":"stationery","definitionId":%s,"common":{"priority":"Medium","remarks":%s},"payload":{"item":"%s","quantity":%s}}' \
    "$DEF_STN" "$remarks" "$1" "$2"
}

cur_body() {
  printf '{"moduleCode":"courier","definitionId":%s,"common":{"priority":"Medium"},"payload":{"documentDescription":"Signed contract","senderName":"Priya Nair","receiverName":"Asha Menon","receiverAddress":"12 Lake Road","receiverCity":"Pune"}}' \
    "$DEF_CUR"
}

# create TOKEN BODY  ->  RID, RNO (and STATUS/BODY of the create call)
create() {
  call POST /api/requests "$1" "$2"
  RID=$(jget d.data.id)
  RNO=$(jget d.data.requestNo)
}

# Walks a new Stationery request through the manager, verification, stock check and issue steps.
walk_stationery_to_receipt() { # id
  act "$1" "$T_MGR" Approve; assert_status "stationery $1: manager approves" 200
  act "$1" "$T_ADM" Approve; assert_status "stationery $1: verification approved" 200
  act "$1" "$T_STORE" Complete; assert_status "stationery $1: stock confirmed" 200
  act "$1" "$T_STORE" Complete; assert_status "stationery $1: items issued" 200
}

# ---------------------------------------------------------- shared set-up

setup_dbquery() {
  if [ -n "${DB_PATH-}" ]; then
    # shellcheck source=lib/api-probe.sh
    source "$HERE/lib/api-probe.sh"
  fi
}

run_logging_block() { # label
  if [ -z "${DB_PATH-}" ]; then return; fi
  call GET "/api/system/log-test?mode=info"
  if [ "$STATUS" = "404" ]; then
    echo "SKIP logging (trigger not enabled)"
    return
  fi
  local first="smoke-log-$$-$RANDOM" second_id
  EXTRA_HEADER="X-Correlation-ID: $first" call GET "/api/system/log-test?mode=error"
  assert_status "logging: forced error answers 500" 500
  assert_eq "logging: error envelope is generic" "$(jget d.error.code)" INTERNAL_ERROR
  assert_not_contains "logging: no exception text in the response" "$BODY" "Forced diagnostic"
  assert_eq "logging: correlation id echoed in the header" "$(header_value X-Correlation-ID)" "$first"
  call GET "/api/system/log-test?mode=error"
  second_id=$(header_value X-Correlation-ID)
  assert_status "logging: second forced error answers 500" 500
  call GET "/api/system/log-test?mode=info"
  call GET /api/health
  sleep 4
  assert_true "logging: Error or Warning row with category App and exception for the sent id" \
    "$([ "$(dbval "SELECT COUNT(*) FROM logs WHERE correlation_id = '$first' AND level IN ('Error','Warning') AND category = 'App' AND COALESCE(exception,'') <> ''")" -ge 1 ] && echo true || echo false)"
  assert_true "logging: row for the generated id from the response header" \
    "$([ -n "$second_id" ] && [ "$(dbval "SELECT COUNT(*) FROM logs WHERE correlation_id = '$second_id' AND level IN ('Error','Warning') AND category = 'App'")" -ge 1 ] && echo true || echo false)"
  assert_eq "logging: no Information rows stored" "$(dbval "SELECT COUNT(*) FROM logs WHERE level IN ('Information','Debug','Verbose')")" 0
}

# ===================================================================== modes

main_mode() {
  T_EMP=$(login employee@demo.test)
  T_MGR=$(login manager@demo.test)
  T_ADM=$(login admin@demo.test)
  T_STORE=$(login store@demo.test)
  T_SYS=$(login sysadmin@demo.test)
  T_HR=$(login hr@demo.test)
  T_SEC=$(login security@demo.test)
  for t in "$T_EMP" "$T_MGR" "$T_ADM" "$T_STORE" "$T_SYS" "$T_HR" "$T_SEC"; do
    if [ "$t" = "null" ] || [ -z "$t" ]; then echo "FAIL demo sign-in (is demo mode on?)"; exit 1; fi
  done

  # ----------------------------------------------------------- modules
  call GET /api/modules "$T_EMP"
  assert_status "modules list" 200
  assert_contains "modules include stationery" "$(jget 'd.data.map(m => m.code).join(",")')" stationery
  assert_contains "modules include courier" "$(jget 'd.data.map(m => m.code).join(",")')" courier
  call GET /api/modules/stationery "$T_EMP"
  DEF_STN=$(jget d.data.definitionId)
  assert_match "stationery module returns its definition id" "$DEF_STN" '^[0-9]+$'
  call GET /api/modules/courier "$T_EMP"
  DEF_CUR=$(jget d.data.definitionId)
  assert_match "courier module returns its definition id" "$DEF_CUR" '^[0-9]+$'

  if [ -n "${DB_PATH-}" ]; then
    EMP_USER=$(dbval "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = 'EMPLOYEE@DEMO.TEST'")
    MGR_USER=$(dbval "SELECT Id FROM AspNetUsers WHERE NormalizedEmail = 'MANAGER@DEMO.TEST'")
    assert_match "stamp: demo user ids found in the database" "$EMP_USER $MGR_USER" '^[0-9A-Fa-f-]{20,} [0-9A-Fa-f-]{20,}$'
  fi

  # ------------------------------------------------- walk 1: stationery
  create "$T_EMP" "$(stn_body 'A4 paper' 5 'Please deliver to desk 12')"
  R1=$RID; N1=$RNO
  assert_status "stationery: create answers 201" 201
  assert_match "stationery: STN request id format" "$N1" '^STN-[0-9]{4}-[0-9]{4}$'
  assert_eq "stationery: status InProgress at creation" "$(jget d.data.currentStatus)" InProgress
  assert_eq "stationery: approval Pending at creation" "$(jget d.data.approvalStatus)" Pending
  assert_eq "stationery: first step is manager-approval at once" "$(jget d.data.currentStepKey)" manager-approval
  assert_eq "stationery: remarks round trip" "$(jget d.data.remarks)" "Please deliver to desk 12"
  assert_eq "stationery: the requester may only cancel" "$(jget 'd.data.allowedActions.join(",")')" Cancel

  if [ -n "${DB_PATH-}" ]; then
    assert_eq "stamp: created_by is the requester after creation" \
      "$(dbval "SELECT created_by FROM requests WHERE request_no = '$N1'")" "$EMP_USER"
  fi

  detail "$R1" "$T_EMP"; RV=$(jget d.data.rowVersion)
  act_rv "$R1" "$T_EMP" Approve "$RV"
  assert_error "stationery: requester cannot approve (403 ACTION_NOT_ALLOWED)" 403 ACTION_NOT_ALLOWED

  detail "$R1" "$T_HR"
  assert_error "stationery: uninvolved user gets 404" 404 NOT_FOUND

  ROW=$(inbox_find "$R1" "$T_MGR")
  assert_eq "inbox: manager sees the request, step type Approval" "$(jx "$ROW" d.currentStepType)" Approval
  assert_eq "inbox: manager row label is Approve" "$(jx "$ROW" d.primaryActionLabel)" Approve
  call GET /api/requests/inbox/count "$T_MGR"
  assert_true "inbox: count is at least 1" "$(jget 'd.data.count >= 1')"

  detail "$R1" "$T_MGR"; RV_BEFORE=$(jget d.data.rowVersion)
  assert_eq "stationery: manager is offered Approve and Reject" "$(jget 'd.data.allowedActions.join(",")')" "Approve,Reject"
  act "$R1" "$T_MGR" Approve '"a note that is ignored"'
  assert_status "stationery: manager approves" 200
  assert_eq "stationery: approval carries no note" "$(jget 'd.data.steps[0].comment')" null
  assert_eq "stationery: manager approval step is Done" "$(jget 'd.data.steps[0].state')" Done
  assert_eq "stationery: step records who approved" "$(jget 'd.data.steps[0].actedByName')" "Rohan Kapoor"
  if [ -n "${DB_PATH-}" ]; then
    assert_eq "stamp: updated_by is the manager after the approval" \
      "$(dbval "SELECT updated_by FROM requests WHERE request_no = '$N1'")" "$MGR_USER"
    assert_eq "stamp: created_by is still the requester" \
      "$(dbval "SELECT created_by FROM requests WHERE request_no = '$N1'")" "$EMP_USER"
  fi

  act_rv "$R1" "$T_MGR" Approve "$RV_BEFORE"
  assert_error "stationery: stale row version gets 409 STATE_CONFLICT" 409 STATE_CONFLICT

  act "$R1" "$T_ADM" Approve
  assert_status "stationery: verification approved" 200
  assert_eq "stationery: approval Approved after verification" "$(jget d.data.approvalStatus)" Approved
  assert_eq "stationery: still InProgress after verification" "$(jget d.data.currentStatus)" InProgress

  ROW=$(inbox_find "$R1" "$T_STORE")
  assert_eq "inbox: stock-check label" "$(jx "$ROW" d.primaryActionLabel)" "Confirm stock available"
  assert_eq "inbox: stock-check is a Task" "$(jx "$ROW" d.currentStepType)" Task
  detail "$R1" "$T_STORE"
  assert_eq "stationery: store is offered Complete only" "$(jget 'd.data.allowedActions.join(",")')" Complete
  detail "$R1" "$T_STORE"; RV=$(jget d.data.rowVersion)
  act_rv "$R1" "$T_STORE" Approve "$RV"
  assert_error "stationery: Approve at a task step gets 403" 403 ACTION_NOT_ALLOWED
  act_rv "$R1" "$T_STORE" Reject "$RV" '"Not valid here"'
  assert_error "stationery: Reject at a task step gets 403" 403 ACTION_NOT_ALLOWED
  act "$R1" "$T_STORE" Complete
  assert_status "stationery: stock confirmed" 200

  ROW=$(inbox_find "$R1" "$T_STORE")
  assert_eq "inbox: issue step label is Mark as issued" "$(jx "$ROW" d.primaryActionLabel)" "Mark as issued"
  act "$R1" "$T_STORE" Complete
  assert_status "stationery: items issued" 200

  # The receipt confirmation is a task whose actor is the requester.
  detail "$R1" "$T_EMP"; RV=$(jget d.data.rowVersion)
  assert_eq "receipt: requester is offered exactly Complete and Cancel" "$(jget 'd.data.allowedActions.join(",")')" "Complete,Cancel"
  assert_eq "receipt: primary action label" "$(jget d.data.primaryActionLabel)" "Confirm I received the items"
  ROW=$(inbox_find "$R1" "$T_EMP")
  assert_eq "receipt: the request is in the requester's own inbox" "$(jx "$ROW" d.currentStepType)" Task
  assert_eq "receipt: inbox row label" "$(jx "$ROW" d.primaryActionLabel)" "Confirm I received the items"
  call GET /api/dashboard/summary "$T_EMP"
  assert_true "receipt: dashboard waiting-for-me counts it" "$(jget 'd.data.waitingForMe >= 1')"
  act_rv "$R1" "$T_EMP" Approve "$RV"
  assert_error "receipt: requester Approve gets 403" 403 ACTION_NOT_ALLOWED
  act_rv "$R1" "$T_EMP" Reject "$RV" '"Not my call"'
  assert_error "receipt: requester Reject with a reason gets 403" 403 ACTION_NOT_ALLOWED
  act "$R1" "$T_EMP" Complete
  assert_status "receipt: requester confirms" 200
  assert_eq "acknowledgement step is Done" "$(jget 'd.data.steps.find(s => s.key === "acknowledgement").state')" Done
  assert_eq "acknowledgement records Priya Nair" "$(jget 'd.data.steps.find(s => s.key === "acknowledgement").actedByName')" "Priya Nair"
  assert_true "acknowledgement has a time" "$(jget 'd.data.steps.find(s => s.key === "acknowledgement").actedUtc !== null')"
  if [ -n "${DB_PATH-}" ]; then
    assert_eq "acknowledgement audit event names Priya Nair" \
      "$(dbval "SELECT actor_name FROM audit_events WHERE request_id = $R1 AND event_type = 'StepCompleted' AND step_key = 'acknowledgement'")" "Priya Nair"
  fi

  # The last step: the requester can still cancel, the store closes the request.
  assert_eq "stock update: requester is offered exactly Cancel" "$(jget 'd.data.allowedActions.join(",")')" Cancel
  ROW=$(inbox_find "$R1" "$T_STORE")
  assert_eq "inbox: stock update label" "$(jx "$ROW" d.primaryActionLabel)" "Confirm stock updated"
  act "$R1" "$T_STORE" Complete
  assert_status "stationery: stock update completes the request" 200
  assert_eq "stationery: final status Closed" "$(jget d.data.currentStatus)" Closed
  assert_eq "stationery: final approval Approved" "$(jget d.data.approvalStatus)" Approved
  assert_eq "stationery: all six steps Done" "$(jget 'd.data.steps.every(s => s.state === "Done") && d.data.steps.length === 6')" true
  assert_eq "stationery: steps are in order" "$(jget 'd.data.steps.map(s => s.seq).join(",")')" "1,2,3,4,5,6"
  assert_true "stationery: closed time is set" "$(jget 'd.data.closedUtc !== null')"
  detail "$R1" "$T_EMP"
  assert_eq "stationery: no allowed actions once closed" "$(jget 'd.data.allowedActions.length')" 0
  assert_eq "stationery: no primary action once closed" "$(jget d.data.primaryActionLabel)" null

  # --------------------------------------------------------- cancelling
  create "$T_EMP" "$(stn_body Pens 10)"; R2=$RID
  detail "$R2" "$T_EMP"; RV=$(jget d.data.rowVersion); ST=$(jget d.data.currentStatus)
  act_rv "$R2" "$T_EMP" Cancel "$RV" null
  assert_field_error "cancel: no reason gets 400 on comment" comment
  act_rv "$R2" "$T_EMP" Cancel "$RV" '"   "'
  assert_field_error "cancel: blank reason gets 400 on comment" comment
  act_rv "$R2" "$T_EMP" Cancel "$RV" "\"$(long_text 1001)\""
  assert_field_error "cancel: 1001 characters gets 400 on comment" comment
  detail "$R2" "$T_EMP"
  assert_eq "cancel: refused attempts leave the request unchanged" "$(jget d.data.currentStatus)/$(jget d.data.rowVersion)" "$ST/$RV"

  act_rv "$R2" "$T_EMP" Cancel "$RV" '"Raised by mistake"'
  assert_status "cancel: with a reason answers 200" 200
  assert_eq "cancel: status Cancelled" "$(jget d.data.currentStatus)" Cancelled
  assert_eq "cancel: reason shown in the detail" "$(jget d.data.cancelReason)" "Raised by mistake"
  assert_true "cancel: time shown in the detail" "$(jget 'd.data.cancelledUtc !== null')"
  assert_eq "cancel: no allowed actions afterwards" "$(jget 'd.data.allowedActions.length')" 0
  detail "$R2" "$T_ADM"
  assert_eq "cancel: same reason when read by the admin" "$(jget d.data.cancelReason)" "Raised by mistake"
  if [ -n "${DB_PATH-}" ]; then
    assert_eq "cancel: audit event carries the reason" \
      "$(dbval "SELECT comment FROM audit_events WHERE request_id = $R2 AND event_type = 'Cancelled'")" "Raised by mistake"
  fi
  detail "$R2" "$T_ADM"; RV=$(jget d.data.rowVersion)
  act_rv "$R2" "$T_EMP" Cancel "$RV" '"Trying again"'
  assert_error "cancel: an already Cancelled request gets 403 ACTION_NOT_ALLOWED" 403 ACTION_NOT_ALLOWED
  detail "$R1" "$T_ADM"; RV=$(jget d.data.rowVersion)
  act_rv "$R1" "$T_EMP" Cancel "$RV" '"Too late"'
  assert_error "cancel: a Closed request gets 403 ACTION_NOT_ALLOWED" 403 ACTION_NOT_ALLOWED

  # Cancel at the last step: there is no cutoff.
  create "$T_EMP" "$(stn_body Folders 3)"; R3=$RID
  walk_stationery_to_receipt "$R3"
  act "$R3" "$T_EMP" Complete
  assert_status "cancel at last step: receipt confirmed" 200
  assert_eq "cancel at last step: now at stock-update" "$(jget d.data.currentStepKey)" stock-update
  act "$R3" "$T_EMP" Cancel '"Not needed any more"'
  assert_status "cancel at last step: requester cancels" 200
  assert_eq "cancel at last step: status Cancelled" "$(jget d.data.currentStatus)" Cancelled

  # A Cancel by someone other than the requester.
  create "$T_EMP" "$(stn_body Staplers 2)"; R4=$RID; N4=$RNO
  detail "$R4" "$T_MGR"; RV=$(jget d.data.rowVersion)
  act_rv "$R4" "$T_MGR" Cancel "$RV" '"I would rather not"'
  assert_error "cancel: a user who is not the requester gets 403 ACTION_NOT_ALLOWED" 403 ACTION_NOT_ALLOWED
  detail "$R4" "$T_MGR"
  assert_eq "cancel: refused cancel leaves the request in progress" "$(jget d.data.currentStatus)/$(jget d.data.rowVersion)" "InProgress/$RV"

  # ---------------------------------------------------------- rejecting
  act_rv "$R4" "$T_MGR" Reject "$RV" null
  assert_field_error "reject: no reason gets 400 on comment" comment
  act_rv "$R4" "$T_MGR" Reject "$RV" '"  "'
  assert_field_error "reject: blank reason gets 400 on comment" comment
  act_rv "$R4" "$T_MGR" Reject "$RV" "\"$(long_text 1001)\""
  assert_field_error "reject: 1001 characters gets 400 on comment" comment
  act_rv "$R4" "$T_MGR" Reject "$RV" '"Not needed"'
  assert_status "reject: manager rejects with a reason" 200
  assert_eq "reject: status Rejected" "$(jget d.data.currentStatus)" Rejected
  assert_eq "reject: approval Rejected" "$(jget d.data.approvalStatus)" Rejected
  assert_eq "reject: the rejected step carries the reason" "$(jget 'd.data.steps.find(s => s.state === "Rejected").comment')" "Not needed"
  detail "$R4" "$T_MGR"
  assert_eq "reject: former actor has no allowed actions" "$(jget 'd.data.allowedActions.length')" 0
  RV=$(jget d.data.rowVersion)
  ROW=$(inbox_find "$R4" "$T_MGR")
  assert_eq "reject: the request is gone from the former actor's inbox" "$ROW" null
  act_rv "$R4" "$T_MGR" Approve "$RV"
  assert_error "reject: former actor Approve gets 403 ACTION_NOT_ALLOWED" 403 ACTION_NOT_ALLOWED
  act_rv "$R4" "$T_MGR" Reject "$RV" '"Again"'
  assert_error "reject: former actor Reject gets 403 ACTION_NOT_ALLOWED" 403 ACTION_NOT_ALLOWED
  detail "$R4" "$T_EMP"
  assert_eq "reject: requester has no allowed actions" "$(jget 'd.data.allowedActions.length')" 0
  call PUT "/api/requests/$R4" "$T_EMP" '{"payload":{"item":"x","quantity":1}}'
  assert_true "no endpoint edits a request after creation" "$([ "$STATUS" = 404 ] || [ "$STATUS" = 405 ] && echo true || echo false)"

  # -------------------------------------------------------- validation
  call POST /api/requests "$T_EMP" "$(printf '{"moduleCode":"stationery","definitionId":%s,"common":{},"payload":{"item":"Pens","quantity":1}}' "$DEF_CUR")"
  assert_field_error "create: another module's definition id gets 400 on definitionId" definitionId
  call POST /api/requests "$T_EMP" "$(stn_body Pens 0)"
  assert_field_error "create: quantity 0 gets 400 on quantity" quantity
  call POST /api/requests "$T_EMP" "$(printf '{"moduleCode":"stationery","definitionId":%s,"common":{},"payload":{"item":"Pens","quantity":1,"colour":"red"}}' "$DEF_STN")"
  assert_status "create: unknown payload key gets 400" 400
  call GET /api/requests/999999999 "$T_MGR"
  assert_error "unknown request id: detail gets 404 NOT_FOUND" 404 NOT_FOUND
  call POST /api/requests/999999999/actions "$T_MGR" '{"action":"Approve","comment":null,"rowVersion":1}'
  assert_error "unknown request id: action gets 404 NOT_FOUND" 404 NOT_FOUND

  # ----------------------------------------------------------- my requests
  call GET "/api/requests/mine?page=1&pageSize=2" "$T_EMP"
  assert_status "mine: first page answers 200" 200
  assert_true "mine: total is at least 3" "$(jget 'd.data.total >= 3')"
  assert_eq "mine: page holds 2 items" "$(jget 'd.data.items.length')" 2
  call GET "/api/requests/mine?status=Closed&pageSize=100" "$T_EMP"
  assert_true "mine: status filter returns only Closed" "$(jget 'd.data.items.length > 0 && d.data.items.every(i => i.currentStatus === "Closed")')"
  call GET "/api/requests/mine?status=Closed&status=Cancelled&q=$N1" "$T_EMP"
  assert_eq "mine: closed requests are included" "$(jget 'd.data.total')" 1
  call GET "/api/requests/mine?approvalStatus=Approved&q=$N1" "$T_EMP"
  assert_eq "mine: approval filter keeps an approved request" "$(jget 'd.data.total')" 1
  call GET "/api/requests/mine?approvalStatus=Approved&q=$N4" "$T_EMP"
  assert_eq "mine: approval filter drops a rejected request" "$(jget 'd.data.total')" 0
  call GET "/api/requests/mine?module=stationery&sort=requestNo&dir=asc&pageSize=50" "$T_EMP"
  assert_true "mine: sorted by request number" "$(jget '(a => JSON.stringify(a) === JSON.stringify([...a].sort()))(d.data.items.map(i => i.requestNo))')"
  call GET "/api/requests/mine?sort=nonsense%3B%20DROP%20TABLE%20requests&dir=asc" "$T_EMP"
  assert_status "mine: unknown sort falls back without an error" 200
  assert_not_contains "mine: no database text in the response" "$BODY" SQLite
  call GET "/api/requests/mine?dir=sideways" "$T_EMP"
  assert_status "mine: bad direction gets 400" 400
  call GET "/api/requests/mine?status=Bogus" "$T_EMP"
  assert_status "mine: bad status gets 400" 400
  call GET "/api/requests/mine?pageSize=101" "$T_EMP"
  assert_status "mine: page size above 100 gets 400" 400

  # ------------------------------------------------------------- audit
  call GET "/api/requests/$R1/audit" "$T_ADM"
  assert_status "audit: admin gets 200" 200
  assert_eq "audit: first event is Created" "$(jget 'd.data[0].eventType')" Created
  assert_eq "audit: last event is Closed" "$(jget 'd.data[d.data.length - 1].eventType')" Closed
  EVENTS=$(jget 'd.data.map(e => e.eventType).join(",")')
  assert_contains "audit: includes StepApproved" "$EVENTS" StepApproved
  assert_contains "audit: includes StepCompleted" "$EVENTS" StepCompleted
  assert_true "audit: events are in order" "$(jget 'd.data.every((e, i) => i === 0 || d.data[i - 1].id < e.id)')"
  call GET "/api/requests/$R1/audit" "$T_SYS"
  assert_status "audit: sysadmin gets 200" 200
  call GET "/api/requests/$R1/audit" "$T_EMP"
  assert_status "audit: employee gets 403" 403
  call GET "/api/requests/$R1/audit" "$T_MGR"
  assert_status "audit: manager gets 403" 403
  call GET "/api/requests/$R1/audit"
  assert_status "audit: no token gets 401" 401
  call GET "/api/requests/999999999/audit" "$T_ADM"
  assert_error "audit: unknown request gets 404" 404 NOT_FOUND

  # ------------------------------------------------- walk 2: courier
  create "$T_EMP" "$(cur_body)"; C1=$RID
  assert_status "courier: create answers 201" 201
  assert_match "courier: CUR request id format" "$RNO" '^CUR-[0-9]{4}-[0-9]{4}$'
  assert_eq "courier: exactly the four task steps" "$(jget 'd.data.steps.map(s => s.key).join(",")')" "courier-selection,dispatch,tracking-number,delivery-confirmation"
  assert_true "courier: every step is a Task and none is NotRequired" "$(jget 'd.data.steps.every(s => s.type === "Task" && s.state !== "NotRequired")')"
  assert_eq "courier: approval Approved from the start" "$(jget d.data.approvalStatus)" Approved
  assert_eq "courier: first step is courier-selection" "$(jget d.data.currentStepKey)" courier-selection
  assert_eq "courier: responsible role is Admin" "$(jget d.data.responsible.role)" Admin

  detail "$C1" "$T_ADM"
  assert_eq "courier: capture field key in the detail" "$(jget 'd.data.steps.find(s => s.isCurrent).captureFields.map(f => f.key).join(",")')" courierCompany
  assert_eq "courier: capture field is required" "$(jget 'd.data.steps.find(s => s.isCurrent).captureFields[0].required')" true
  assert_eq "courier: selection label" "$(jget d.data.primaryActionLabel)" "Select courier"
  ROW=$(inbox_find "$C1" "$T_ADM")
  assert_eq "courier: capture field key in the inbox row" "$(jx "$ROW" 'd.captureFields.map(f => f.key).join(",")')" courierCompany
  act "$C1" "$T_ADM" Complete null null
  assert_field_error "courier: Complete without captured values gets 400 on courierCompany" courierCompany
  act "$C1" "$T_ADM" Complete null '{"courierCompany":"Example Couriers"}'
  assert_status "courier: courier selected" 200
  assert_eq "courier: captured value shown on its step" "$(jget 'd.data.steps[0].captured.courierCompany')" "Example Couriers"

  detail "$C1" "$T_ADM"
  assert_eq "courier: dispatch label" "$(jget d.data.primaryActionLabel)" "Mark as dispatched"
  act "$C1" "$T_ADM" Complete
  assert_status "courier: dispatched" 200

  act "$C1" "$T_ADM" Complete null null
  assert_field_error "courier: tracking number missing gets 400 on trackingNumber" trackingNumber
  act "$C1" "$T_ADM" Complete null '{"trackingNumber":"   "}'
  assert_field_error "courier: blank tracking number gets 400 on trackingNumber" trackingNumber
  act "$C1" "$T_ADM" Complete null '{"trackingNumber":"TRK123456","notDeclared":"x"}'
  assert_status "courier: undeclared captured key gets 400" 400
  detail "$C1" "$T_ADM"
  assert_eq "courier: refused attempts do not move the request" "$(jget d.data.currentStepKey)" tracking-number
  act "$C1" "$T_ADM" Complete null '{"trackingNumber":"TRK123456"}'
  assert_status "courier: tracking number saved" 200
  assert_eq "courier: courier company still on its step" "$(jget 'd.data.steps[0].captured.courierCompany')" "Example Couriers"
  assert_eq "courier: tracking number on its step" "$(jget 'd.data.steps[2].captured.trackingNumber')" TRK123456

  detail "$C1" "$T_EMP"; RV=$(jget d.data.rowVersion)
  assert_eq "courier: requester is offered exactly Complete and Cancel" "$(jget 'd.data.allowedActions.join(",")')" "Complete,Cancel"
  assert_eq "courier: delivery label" "$(jget d.data.primaryActionLabel)" "Confirm delivery"
  act_rv "$C1" "$T_EMP" Approve "$RV"
  assert_error "courier: Approve at a task step gets 403" 403 ACTION_NOT_ALLOWED
  act_rv "$C1" "$T_EMP" Reject "$RV" '"No"'
  assert_error "courier: Reject at a task step gets 403" 403 ACTION_NOT_ALLOWED
  ROW=$(inbox_find "$C1" "$T_EMP")
  assert_eq "courier: delivery confirmation is in the requester's inbox" "$(jx "$ROW" d.currentStepType)" Task
  act "$C1" "$T_EMP" Complete
  assert_status "courier: delivery confirmed" 200
  assert_eq "courier: final status Closed" "$(jget d.data.currentStatus)" Closed
  assert_true "courier: closed time is set" "$(jget 'd.data.closedUtc !== null')"

  create "$T_EMP" "$(cur_body)"; C2=$RID
  detail "$C2" "$T_ADM"; RV=$(jget d.data.rowVersion)
  act_rv "$C2" "$T_ADM" Reject "$RV" '"Not possible"'
  assert_error "courier: admin Reject gets 403 (a task step has no Reject)" 403 ACTION_NOT_ALLOWED
  act "$C2" "$T_EMP" Cancel '"No longer needed"'
  assert_status "courier: requester cancels at courier selection" 200
  assert_eq "courier: status Cancelled" "$(jget d.data.currentStatus)" Cancelled

  # --------------------------------------------------- authentication
  for route in "GET /api/modules" "GET /api/modules/stationery" "POST /api/requests" "GET /api/requests/$R1" \
    "POST /api/requests/$R1/actions" "GET /api/requests/$R1/audit" "GET /api/requests/mine" "GET /api/requests/inbox" \
    "GET /api/requests/inbox/count" "GET /api/dashboard/summary"; do
    call "${route%% *}" "${route#* }"
    assert_status "no token: ${route} gets 401" 401
  done

  # ------------------------------------------------------- audit rows
  if [ -n "${DB_PATH-}" ]; then
    for type in Created StepApproved StepCompleted Closed; do
      assert_true "audit rows: $type exists for the walked request" \
        "$([ "$(dbval "SELECT COUNT(*) FROM audit_events WHERE request_id = $R1 AND event_type = '$type'")" -ge 1 ] && echo true || echo false)"
    done
    assert_contains "audit rows: tracking number in the completion event" \
      "$(dbval "SELECT details_json FROM audit_events WHERE request_id = $C1 AND event_type = 'StepCompleted' AND step_key = 'tracking-number'")" TRK123456
    CAPTURED=$(dbval "SELECT group_concat(captured_json) FROM request_steps WHERE request_id = $C1 AND captured_json IS NOT NULL")
    assert_contains "audit rows: courier company captured" "$CAPTURED" "Example Couriers"
    assert_contains "audit rows: tracking number captured" "$CAPTURED" TRK123456
    assert_contains "audit rows: audit_events cannot be updated (error 19)" \
      "$(dbquery "$DB_PATH" "UPDATE audit_events SET comment = 'x' WHERE id = (SELECT MIN(id) FROM audit_events)" 2>&1)" "ERROR 19"
    SYSTEM_ACTOR=$(grep -oE 'UserId = "[^"]+"' "$REPO_ROOT/backend/src/AdminDesk.SharedKernel/Constants/SystemActor.cs" | cut -d'"' -f2)
    assert_eq "stamp: every seeded employee row carries the system actor id" \
      "$(dbval "SELECT COUNT(*) FROM employees WHERE created_by <> '$SYSTEM_ACTOR'")" 0
    assert_true "audit rows: several event types recorded" \
      "$([ "$(dbval "SELECT COUNT(DISTINCT event_type) FROM audit_events")" -ge 5 ] && echo true || echo false)"
  fi

  if [ "${SMOKE_FIXTURES-}" = "1" ]; then money_checks; fi

  run_logging_block
}

# Money round trip and a captured money value, through the routing sample definition.
money_checks() {
  call GET /api/modules/routingcheck "$T_EMP"
  if [ "$STATUS" != "200" ]; then
    fail "money: routingcheck module is available (start the API with the override directory)" "($STATUS)"
    return
  fi
  local def
  def=$(jget d.data.definitionId)

  create "$T_EMP" "$(printf '{"moduleCode":"routingcheck","definitionId":%s,"common":{"priority":"Medium"},"payload":{"title":"Large item","amount":80000.5}}' "$def")"
  local big=$RID
  assert_status "money: routingcheck create answers 201" 201
  detail "$big" "$T_EMP"
  assert_eq "money: amount round trips in rupees" "$(jget d.data.payload.amount)" 80000.5
  assert_eq "money: conditional steps are Upcoming" \
    "$(jget 'd.data.steps.filter(s => s.key === "damage-assessment" || s.key === "high-value-approval").map(s => s.state).join(",")')" "Upcoming,Upcoming"
  assert_eq "money: approval Pending while the review is open" "$(jget d.data.approvalStatus)" Pending
  if [ -n "${DB_PATH-}" ]; then
    assert_eq "money: amount stored as integer paise" \
      "$(dbval "SELECT json_extract(payload_json, '\$.amount') FROM requests WHERE id = $big")" 8000050
  fi

  create "$T_EMP" "$(printf '{"moduleCode":"routingcheck","definitionId":%s,"common":{"priority":"Medium"},"payload":{"title":"Small item","amount":20000}}' "$def")"
  local small=$RID
  act "$small" "$T_ADM" Approve
  assert_status "money: review approved" 200
  act "$small" "$T_SEC" Complete null '{"damaged":false}'
  assert_status "money: inspection recorded" 200
  assert_eq "money: next step is extra-check" "$(jget d.data.currentStepKey)" extra-check
  act "$small" "$T_SEC" Complete null '{"repairCost":1250.75}'
  assert_status "money: extra check recorded" 200
  assert_eq "money: captured repairCost returned in rupees" "$(jget 'd.data.steps.find(s => s.key === "extra-check").captured.repairCost')" 1250.75
  if [ -n "${DB_PATH-}" ]; then
    assert_eq "money: captured repairCost stored as integer paise" \
      "$(dbval "SELECT json_extract(captured_json, '\$.repairCost') FROM request_steps WHERE request_id = $small AND step_key = 'extra-check'")" 125075
  fi
  call GET "/api/requests/$small/audit" "$T_ADM"
  assert_status "money: audit endpoint answers 200" 200
  assert_not_contains "money: audit response carries no paise value" "$BODY" 125075
}

no_employee_mode() {
  if [ -z "${SMOKE_ADMIN_EMAIL-}" ] || [ -z "${SMOKE_ADMIN_PASSWORD-}" ]; then
    echo "FAIL no-employee mode needs SMOKE_ADMIN_EMAIL and SMOKE_ADMIN_PASSWORD"
    exit 1
  fi
  call POST /api/auth/login "" "{\"email\":\"$SMOKE_ADMIN_EMAIL\",\"password\":\"$SMOKE_ADMIN_PASSWORD\"}"
  assert_status "no-employee: sign-in succeeds" 200
  assert_eq "no-employee: the user has no employee id" "$(jget d.data.user.employeeId)" null
  local tok
  tok=$(jget d.data.accessToken)

  call GET /api/requests/mine "$tok"
  assert_status "no-employee: mine answers 200" 200
  assert_eq "no-employee: mine is empty" "$(jget 'd.data.items.length + ":" + d.data.total')" "0:0"
  call GET /api/dashboard/summary "$tok"
  assert_status "no-employee: summary answers 200" 200
  assert_eq "no-employee: counters are zero and recent is empty" \
    "$(jget '[d.data.total, d.data.pending, d.data.approved, d.data.rejected, d.data.completed, d.data.recent.length].join(",")')" "0,0,0,0,0,0"
  call GET /api/requests/inbox "$tok"
  assert_status "no-employee: inbox answers 200" 200
  call GET /api/requests/inbox/count "$tok"
  assert_status "no-employee: inbox count answers 200" 200

  call GET /api/modules/stationery "$tok"
  DEF_STN=$(jget d.data.definitionId)
  call POST /api/requests "$tok" "$(stn_body Pens 1)"
  assert_error "no-employee: raising a request gets 403 NO_EMPLOYEE_PROFILE" 403 NO_EMPLOYEE_PROFILE

  call GET "/api/system/log-test?mode=error"
  assert_status "no-employee: the log trigger is absent when demo mode is off" 404
}

# ====================================================================== run

setup_dbquery

case "$MODE" in
  main) main_mode ;;
  no-employee) no_employee_mode ;;
  *) echo "usage: smoke.sh [main|no-employee]"; exit 2 ;;
esac

echo "$PASS passed, $FAIL failed"
if [ "$FAIL" -gt 0 ]; then exit 1; fi
exit 0
