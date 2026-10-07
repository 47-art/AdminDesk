#!/usr/bin/env bash
# Helpers for starting the built API on a throw-away port and data folder, waiting
# for it, and stopping the whole process tree. Meant to be sourced by other scripts.
#
# Functions:
#   probe_dir NN                      print a fresh temporary data folder
#   api_start PORT DATA_DIR [VAR=val] start the built dll in the background
#   api_wait_health PORT SECONDS      poll /api/health until it answers 200
#   api_wait_log REGEX SECONDS        poll the process output for a matching line
#   api_wait_exit SECONDS             wait for the process to end, print its exit code
#   api_stop                          kill the whole process tree and wait for the port
#
# Build the solution first; the dll is started directly, never through a launcher.

_probe_root() {
  (cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
}

# Convert a path to the form the Windows runtime understands when cygpath exists.
_probe_path() {
  if command -v cygpath >/dev/null 2>&1; then
    cygpath -m "$1"
  else
    printf '%s' "$1"
  fi
}

probe_dir() {
  local nn="${1:-00}"
  mktemp -d "${TMPDIR:-/tmp}/admindesk-p${nn}.XXXXXX"
}

api_start() {
  local port="$1" data="$2"
  shift 2
  local root dll
  root="$(_probe_root)"
  dll="$root/backend/src/AdminDesk.Api/bin/Debug/net10.0/AdminDesk.Api.dll"
  if [ ! -f "$dll" ]; then
    echo "build first"
    return 1
  fi

  mkdir -p "$data/logs"
  API_DATA="$data"
  API_PORT="$port"
  API_OUT="$data/api.out.log"
  : > "$API_OUT"

  local win_data win_logs win_root content_root
  win_data="$(_probe_path "$data")"
  win_logs="$(_probe_path "$data/logs")"
  content_root="$(_probe_path "$root/backend/src/AdminDesk.Api")"

  local envargs=(
    "ASPNETCORE_URLS=http://localhost:$port"
    "ADMINDESK_DATA_DIR=$win_data"
    "ADMINDESK_LOG_DIR=$win_logs"
    "ASPNETCORE_CONTENTROOT=$content_root"
    "ASPNETCORE_ENVIRONMENT=Development"
  )

  local pair name value
  for pair in "$@"; do
    name="${pair%%=*}"
    value="${pair#*=}"
    if [ -e "$value" ]; then
      value="$(_probe_path "$value")"
    fi
    envargs+=("$name=$value")
  done

  local dll_arg
  dll_arg="$(_probe_path "$dll")"

  # The process is started without a wrapper shell so that API_PID (and the Windows
  # id read below) belong to the dotnet process itself.
  if command -v setsid >/dev/null 2>&1; then
    setsid env "${envargs[@]}" dotnet "$dll_arg" > "$API_OUT" 2>&1 &
  else
    env "${envargs[@]}" dotnet "$dll_arg" > "$API_OUT" 2>&1 &
  fi
  API_PID=$!
  API_WINPID=""
  if [ -r "/proc/$API_PID/winpid" ]; then
    API_WINPID="$(cat "/proc/$API_PID/winpid")"
  fi
  return 0
}

_probe_alive() {
  [ -n "${API_PID:-}" ] && kill -0 "$API_PID" 2>/dev/null
}

api_wait_health() {
  local port="$1" seconds="${2:-60}" i code
  for ((i = 0; i < seconds * 2; i++)); do
    code="$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$port/api/health" 2>/dev/null)"
    if [ "$code" = "200" ]; then
      return 0
    fi
    if ! _probe_alive; then
      echo "API process has exited; last output:"
      tail -n 40 "$API_OUT"
      return 1
    fi
    sleep 0.5
  done
  echo "API did not answer in $seconds seconds; last output:"
  tail -n 40 "$API_OUT"
  return 1
}

api_wait_log() {
  local regex="$1" seconds="${2:-30}" i line
  for ((i = 0; i < seconds * 2; i++)); do
    line="$(grep -E -m 1 -- "$regex" "$API_OUT" 2>/dev/null)"
    if [ -n "$line" ]; then
      echo "$line"
      return 0
    fi
    sleep 0.5
  done
  return 1
}

api_wait_exit() {
  local seconds="${1:-30}" i
  for ((i = 0; i < seconds * 2; i++)); do
    if ! _probe_alive; then
      # wait only knows the exit code when the process was started from this shell.
      local rc=0
      wait "$API_PID" 2>/dev/null || rc=$?
      if [ "$rc" = "127" ]; then
        echo "EXITED"
      else
        echo "$rc"
      fi
      return 0
    fi
    sleep 0.5
  done
  echo "RUNNING"
  return 0
}

api_stop() {
  if [ -n "${API_WINPID:-}" ] && command -v taskkill >/dev/null 2>&1; then
    taskkill //PID "$API_WINPID" //T //F >/dev/null 2>&1
  elif [ -n "${API_PID:-}" ]; then
    kill -KILL -- "-$API_PID" 2>/dev/null || kill -KILL "$API_PID" 2>/dev/null
  fi

  local i code
  if [ -n "${API_PORT:-}" ]; then
    for ((i = 0; i < 40; i++)); do
      code="$(curl -s -o /dev/null -w "%{http_code}" "http://localhost:$API_PORT/api/health" 2>/dev/null)"
      if [ "$code" != "200" ]; then
        break
      fi
      sleep 0.5
    done
  fi
  API_PID=""
  API_WINPID=""
  return 0
}
