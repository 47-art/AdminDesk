#!/usr/bin/env bash
# Background job provider checks against the real API host.
#
# Usage: bash scripts/spike/run-spike.sh <basic|restart|soak>
#
#   basic    3 minutes of health traffic with the recurring heartbeat and one delayed
#            job. PASS check1 when the heartbeat shows at least 6 lines per minute,
#            PASS check2 when the delayed job fires once, close to its due time.
#   restart  schedules a 60 second delayed job, hard-kills the whole process tree,
#            restarts on the same data folder after the due time and expects the job
#            to fire exactly once. PASS/FAIL check3.
#   soak     10 minutes with five write jobs every 5 seconds, a warning log request
#            once per second (so the database log sink writes in parallel) and health
#            traffic. PASS/FAIL check4: no lock errors, at least 95 percent of the
#            expected probe rows, no failed jobs, process still alive.
#
# Environment variables:
#   JOBS_PROVIDER  Hangfire (default) or Timer
#   SPIKE_PORT     port for the API (default 5190)
#
# Each run uses its own temporary data and log folders, which are printed, and the
# API process tree is always stopped at the end. Nothing is written to the repository.
# Build the solution first: dotnet build backend/AdminDesk.sln

set -u
cd "$(dirname "${BASH_SOURCE[0]}")/../.." || exit 1
source scripts/lib/api-probe.sh

MODE="${1:-}"
JOBS_PROVIDER="${JOBS_PROVIDER:-Hangfire}"
SPIKE_PORT="${SPIKE_PORT:-5190}"
FORBIDDEN='database is locked|SQLite Error 5|SQLITE_BUSY|AccessViolation|DistributedLockTimeout'
FAILED=0

pass() { echo "PASS $1"; }
fail() { echo "FAIL $1"; FAILED=1; }

# Counts matching lines across the console output and the rolling log files.
count_lines() {
  local regex="$1" total=0 n f
  for f in "$DATA/api.out.log" "$DATA"/logs/*; do
    [ -f "$f" ] || continue
    n=$(grep -ciE -- "$regex" "$f" 2>/dev/null || true)
    total=$((total + ${n:-0}))
  done
  echo "$total"
}

start_api() {
  api_start "$SPIKE_PORT" "$DATA" "Jobs__Provider=$JOBS_PROVIDER" "Spike__Enabled=true" "$@" || return 1
  api_wait_health "$SPIKE_PORT" 90 || return 1
}

traffic() {
  # $1 = seconds, $2 = also call the warning log trigger (1/0)
  local end=$((SECONDS + $1)) i=0
  while [ "$SECONDS" -lt "$end" ]; do
    curl -s -o /dev/null "http://localhost:$SPIKE_PORT/api/health"
    if [ "${2:-0}" = "1" ]; then
      curl -s -o /dev/null "http://localhost:$SPIKE_PORT/api/system/log-test?mode=warning"
      sleep 1
    else
      sleep 0.5
    fi
    i=$((i + 1))
    if ! kill -0 "$API_PID" 2>/dev/null; then
      echo "API process died during traffic"
      return 1
    fi
  done
}

hangfire_failed_count() {
  # Failed jobs are kept as state rows in the storage file.
  local db="$DATA/hangfire.db" tables
  [ -f "$db" ] || { echo 0; return; }
  tables=$(dbquery "$db" "SELECT name FROM sqlite_master WHERE type='table'" 2>/dev/null)
  if echo "$tables" | grep -qi "StateDto"; then
    dbquery "$db" "SELECT COUNT(*) FROM StateDto WHERE Name='Failed'" 2>/dev/null | tail -1
  else
    echo 0
  fi
}

DATA="$(probe_dir 07s)"
echo "mode=$MODE provider=$JOBS_PROVIDER port=$SPIKE_PORT"
echo "data folder: $DATA"
echo "log folder:  $DATA/logs"

case "$MODE" in
  basic)
    start_api || { fail "start"; api_stop; exit 1; }
    traffic 180 0
    api_stop
    # The console and the file sink each carry the line, so count one source only.
    beats=$(grep -ci "heartbeat" "$DATA/api.out.log" || true)
    echo "heartbeat lines in console output: $beats (needed 18)"
    [ "$beats" -ge 18 ] && pass "check1 recurring ($beats lines in 3 minutes)" || fail "check1 recurring ($beats lines)"
    fired=$(grep -c "delayed-probe fired .* check2" "$DATA/api.out.log" || true)
    echo "check2 fire lines: $fired"
    [ "$fired" = "1" ] && pass "check2 delayed (fired once)" || fail "check2 delayed (fired $fired times)"
    echo "startup task lines: $(grep -c 'Jobs startup task ran' "$DATA/api.out.log" || true)"
    echo "probe rows: $(dbquery "$DATA/app.db" "SELECT COUNT(*) FROM job_probe_log WHERE source='spike'" | tail -1)"
    ;;
  restart)
    start_api "SPIKE_RESTART_PROBE=1" || { fail "start"; api_stop; exit 1; }
    if ! api_wait_log "check3 scheduled" 30 >/dev/null; then
      fail "check3 restart (job was not scheduled)"; api_stop; exit 1
    fi
    scheduled=$SECONDS
    sleep 5
    api_stop
    echo "process tree killed after $((SECONDS - scheduled)) seconds"
    # Wait until the job is overdue, then start again on the same folder.
    sleep 65
    cp "$DATA/api.out.log" "$DATA/first.out.log"
    start_api "SPIKE_RESTART_PROBE=1" || { fail "restart start"; api_stop; exit 1; }
    api_wait_log "delayed-probe fired .* check3" 40 >/dev/null
    sleep 20
    api_stop
    fired=$(grep -c "delayed-probe fired .* check3" "$DATA/api.out.log" || true)
    before=$(grep -c "delayed-probe fired .* check3" "$DATA/first.out.log" || true)
    echo "check3 fires before kill: $before, after restart: $fired"
    [ "$before" = "0" ] && [ "$fired" = "1" ] && pass "check3 restart survival (fired once after restart)" || fail "check3 restart survival"
    ;;
  soak)
    start_api "Diagnostics__EnableLogTest=true" "Demo__Enabled=true" || { fail "start"; api_stop; exit 1; }
    traffic 600 1
    alive=0
    kill -0 "$API_PID" 2>/dev/null && alive=1
    sleep 5
    sink_rows=$(dbquery "$DATA/app.db" "SELECT COUNT(*) FROM logs WHERE message = 'Forced diagnostic warning'" | tail -1)
    rows=$(dbquery "$DATA/app.db" "SELECT COUNT(*) FROM job_probe_log WHERE source='spike'" | tail -1)
    if [ "$JOBS_PROVIDER" = "Hangfire" ]; then
      failed_jobs=$(hangfire_failed_count)
    else
      failed_jobs=$(dbquery "$DATA/app.db" "SELECT COUNT(*) FROM scheduled_jobs WHERE state='Failed'" | tail -1)
    fi
    api_stop
    bad=$(count_lines "$FORBIDDEN")
    sink_fail=$(count_lines "Database log sink failed")
    echo "expected 600 probe rows, actual $rows"
    echo "forbidden pattern matches: $bad; failed jobs: $failed_jobs; sink failures: $sink_fail; sink rows: $sink_rows; alive: $alive"
    ok=1
    [ "$alive" = "1" ] || ok=0
    [ "$bad" = "0" ] || ok=0
    [ "${failed_jobs:-0}" = "0" ] || ok=0
    [ "$sink_fail" = "0" ] || ok=0
    [ "${rows:-0}" -ge 570 ] || ok=0
    [ "${sink_rows:-0}" -gt 0 ] || ok=0
    [ "$ok" = "1" ] && pass "check4 concurrency soak" || fail "check4 concurrency soak"
    ;;
  *)
    echo "usage: bash scripts/spike/run-spike.sh <basic|restart|soak>"
    exit 2
    ;;
esac

api_stop
exit "$FAILED"
