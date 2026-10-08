#!/usr/bin/env bash
# Starts the API and the Angular dev server together.
#
#   scripts/dev.sh         restore, build, start both servers and wait; Ctrl+C stops both
#   scripts/dev.sh stop    stop servers that a previous run left behind
#
# The API is started from the built dll, not through "dotnet run", so that stopping it
# also frees the port.

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
LOGS="$ROOT/logs"
API_PID_FILE="$LOGS/dev-api.pid"
WEB_PID_FILE="$LOGS/dev-web.pid"

to_native() {
  if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi
}

# Ends a process and everything it started.
kill_tree() { # pid
  local pid="$1" win
  [ -n "$pid" ] || return 0
  if [ -r "/proc/$pid/winpid" ]; then
    win="$(cat "/proc/$pid/winpid")"
    taskkill //PID "$win" //T //F >/dev/null 2>&1
  else
    kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null
  fi
}

stop_recorded() {
  local file pid
  for file in "$API_PID_FILE" "$WEB_PID_FILE"; do
    if [ -f "$file" ]; then
      pid="$(head -n 1 "$file")"
      kill_tree "$pid"
      rm -f "$file"
    fi
  done
}

if [ "${1:-}" = "stop" ]; then
  stop_recorded
  echo "Stopped."
  exit 0
fi

mkdir -p "$LOGS"
stop_recorded

cd "$ROOT" || exit 1

echo "Restoring and building the backend..."
dotnet restore backend/AdminDesk.sln || { echo "dotnet restore failed."; exit 1; }
dotnet build backend/AdminDesk.sln --no-restore -nologo -v q || { echo "dotnet build failed."; exit 1; }

if [ ! -d frontend/node_modules ]; then
  echo "Installing frontend packages..."
  (cd frontend && npm ci) || { echo "npm ci failed."; exit 1; }
fi

API_PROJECT="$ROOT/backend/src/AdminDesk.Api"
API_DLL="$API_PROJECT/bin/Debug/net10.0/AdminDesk.Api.dll"
if [ ! -f "$API_DLL" ]; then
  echo "Built API not found at $API_DLL"
  exit 1
fi

API_PID=""
WEB_PID=""

cleanup() {
  trap - EXIT INT TERM
  kill_tree "$WEB_PID"
  kill_tree "$API_PID"
  rm -f "$API_PID_FILE" "$WEB_PID_FILE"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

export ASPNETCORE_URLS="http://localhost:5080"
export ASPNETCORE_CONTENTROOT="$(to_native "$API_PROJECT")"
export ASPNETCORE_ENVIRONMENT="Development"

dotnet "$(to_native "$API_DLL")" > "$LOGS/dev-api.out.log" 2> "$LOGS/dev-api.err.log" &
API_PID=$!
echo "$API_PID" > "$API_PID_FILE"

echo "Waiting for the API on http://localhost:5080 ..."
READY=0
for ((i = 0; i < 120; i++)); do
  if ! kill -0 "$API_PID" 2>/dev/null; then break; fi
  if [ "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:5080/api/health)" = "200" ]; then
    READY=1
    break
  fi
  sleep 0.5
done
if [ "$READY" != "1" ]; then
  echo "The API did not answer within 60 seconds. See logs/dev-api.out.log and logs/dev-api.err.log."
  exit 1
fi

echo
echo "API ready:  http://localhost:5080"
echo "Web app:    http://localhost:4200  (starting now)"
echo "The login screen has one-click demo role cards; the shared practice password is Demo@12345."
echo "Plain commands instead of this script:"
echo "  dotnet run --project backend/src/AdminDesk.Api"
echo "  npm start   (inside the frontend folder)"
echo "Press Ctrl+C to stop both, or run scripts/dev.sh stop from another terminal."
echo

(cd frontend && exec npm start) &
WEB_PID=$!
echo "$WEB_PID" > "$WEB_PID_FILE"
wait "$WEB_PID"
