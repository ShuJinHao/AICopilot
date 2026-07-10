#!/usr/bin/env bash

# Shared deployment primitives. This file is sourced by local and remote
# release scripts, so it intentionally does not change the caller's shell
# options.

release_now_epoch() {
  date +%s
}

release_process_start_token() {
  local pid="$1"

  if [ -r "/proc/$pid/stat" ]; then
    awk '{print $22}' "/proc/$pid/stat" 2>/dev/null || true
    return
  fi

  ps -o lstart= -p "$pid" 2>/dev/null | awk '{$1=$1; print}' || true
}

release_lock_value() {
  local lock_dir="$1"
  local key="$2"
  local path="$lock_dir/$key"

  [ -f "$path" ] || return 0
  sed -n '1p' "$path"
}

release_write_lock_metadata() {
  local lock_dir="$1"
  local token="$2"
  local kind="$3"
  local state="$4"
  local phase="$5"
  local expires_at_epoch="${6:-0}"
  local pid="${7:-$$}"
  local process_start

  process_start="$(release_process_start_token "$pid")"
  printf '%s\n' "$token" > "$lock_dir/token"
  printf '%s\n' "$kind" > "$lock_dir/kind"
  printf '%s\n' "$state" > "$lock_dir/state"
  printf '%s\n' "$phase" > "$lock_dir/phase"
  printf '%s\n' "$pid" > "$lock_dir/pid"
  printf '%s\n' "$process_start" > "$lock_dir/process-start"
  printf '%s\n' "$(id -un 2>/dev/null || id -u)" > "$lock_dir/owner"
  printf '%s\n' "$(release_now_epoch)" > "$lock_dir/updated-at-epoch"
  printf '%s\n' "$expires_at_epoch" > "$lock_dir/expires-at-epoch"
}

release_lock_is_active() {
  local lock_dir="$1"
  local state
  local pid
  local expected_start
  local actual_start
  local expires_at

  [ -d "$lock_dir" ] || return 1
  state="$(release_lock_value "$lock_dir" state)"
  if [ "$state" = "reserved" ]; then
    expires_at="$(release_lock_value "$lock_dir" expires-at-epoch)"
    case "$expires_at" in
      ''|*[!0-9]*) return 1 ;;
    esac
    [ "$expires_at" -gt "$(release_now_epoch)" ]
    return
  fi

  pid="$(release_lock_value "$lock_dir" pid)"
  case "$pid" in
    ''|*[!0-9]*) return 1 ;;
  esac
  kill -0 "$pid" 2>/dev/null || return 1

  expected_start="$(release_lock_value "$lock_dir" process-start)"
  [ -n "$expected_start" ] || return 0
  actual_start="$(release_process_start_token "$pid")"
  [ -n "$actual_start" ] && [ "$actual_start" = "$expected_start" ]
}

release_print_lock_diagnostics() {
  local lock_dir="$1"

  printf 'Deployment lock is active: %s\n' "$lock_dir" >&2
  printf '  kind=%s state=%s phase=%s owner=%s pid=%s updatedAtEpoch=%s expiresAtEpoch=%s\n' \
    "$(release_lock_value "$lock_dir" kind)" \
    "$(release_lock_value "$lock_dir" state)" \
    "$(release_lock_value "$lock_dir" phase)" \
    "$(release_lock_value "$lock_dir" owner)" \
    "$(release_lock_value "$lock_dir" pid)" \
    "$(release_lock_value "$lock_dir" updated-at-epoch)" \
    "$(release_lock_value "$lock_dir" expires-at-epoch)" >&2
}

release_remove_stale_lock() {
  local lock_dir="$1"
  local stale_dir="${lock_dir}.stale.$(release_now_epoch).$$"

  if mv "$lock_dir" "$stale_dir" 2>/dev/null; then
    printf 'Removed stale deployment lock: %s\n' "$lock_dir" >&2
    rm -rf "$stale_dir"
    return 0
  fi
  return 1
}

release_acquire_lock() {
  local lock_dir="$1"
  local token="$2"
  local kind="$3"
  local state="${4:-active}"
  local phase="${5:-starting}"
  local ttl_seconds="${6:-0}"
  local attempts=0
  local expires_at=0

  mkdir -p "$(dirname "$lock_dir")"
  while [ "$attempts" -lt 5 ]; do
    if mkdir "$lock_dir" 2>/dev/null; then
      if [ "$state" = "reserved" ]; then
        expires_at=$(( $(release_now_epoch) + ttl_seconds ))
      fi
      release_write_lock_metadata "$lock_dir" "$token" "$kind" "$state" "$phase" "$expires_at" "$$"
      return 0
    fi

    if release_lock_is_active "$lock_dir"; then
      release_print_lock_diagnostics "$lock_dir"
      return 75
    fi

    release_remove_stale_lock "$lock_dir" || true
    attempts=$((attempts + 1))
  done

  printf 'Could not acquire deployment lock after stale-lock recovery: %s\n' "$lock_dir" >&2
  return 75
}

release_adopt_reserved_lock() {
  local lock_dir="$1"
  local token="$2"
  local kind="$3"
  local phase="$4"
  local existing_token
  local state

  existing_token="$(release_lock_value "$lock_dir" token)"
  state="$(release_lock_value "$lock_dir" state)"
  if [ "$state" != "reserved" ] || [ -z "$existing_token" ] || [ "$existing_token" != "$token" ]; then
    printf 'Deployment lock reservation does not match this release: %s\n' "$lock_dir" >&2
    release_print_lock_diagnostics "$lock_dir"
    return 75
  fi

  if ! release_lock_is_active "$lock_dir"; then
    printf 'Deployment lock reservation expired before release start: %s\n' "$lock_dir" >&2
    return 75
  fi

  release_write_lock_metadata "$lock_dir" "$token" "$kind" active "$phase" 0 "$$"
}

release_update_lock_phase() {
  local lock_dir="$1"
  local token="$2"
  local phase="$3"

  [ "$(release_lock_value "$lock_dir" token)" = "$token" ] || return 75
  printf '%s\n' "$phase" > "$lock_dir/phase"
  printf '%s\n' "$(release_now_epoch)" > "$lock_dir/updated-at-epoch"
}

release_unlock() {
  local lock_dir="$1"
  local token="$2"

  [ -d "$lock_dir" ] || return 0
  if [ "$(release_lock_value "$lock_dir" token)" != "$token" ]; then
    printf 'Refusing to release a deployment lock owned by another run: %s\n' "$lock_dir" >&2
    return 75
  fi
  rm -rf "$lock_dir"
}

release_sha256_file() {
  local path="$1"

  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{print $1}'
  else
    shasum -a 256 "$path" | awk '{print $1}'
  fi
}

release_validate_manifest_path() {
  local path="$1"
  case "$path" in
    ''|/*|../*|*/../*|*/..|.env|releases|releases/*|backups|backups/*|.locks|.locks/*|.support-staging|.support-staging/*|.support-backups|.support-backups/*)
      printf 'Unsafe or protected support manifest path: %s\n' "$path" >&2
      return 65
      ;;
  esac
  return 0
}

release_verify_sha256_manifest() {
  local root="$1"
  local manifest="$2"
  local expected
  local path
  local actual

  while read -r expected path; do
    [ -n "$expected" ] || continue
    path="${path#\*}"
    path="${path# }"
    release_validate_manifest_path "$path" || return $?
    [ -f "$root/$path" ] || {
      printf 'Support manifest file is missing: %s\n' "$root/$path" >&2
      return 66
    }
    actual="$(release_sha256_file "$root/$path")"
    if [ "$actual" != "$expected" ]; then
      printf 'Support manifest digest mismatch: %s expected=%s actual=%s\n' "$path" "$expected" "$actual" >&2
      return 65
    fi
  done < "$manifest"
}

release_process_children() {
  local parent_pid="$1"
  pgrep -P "$parent_pid" 2>/dev/null || true
}

release_signal_process_tree() {
  local signal="$1"
  local root_pid="$2"
  local child

  for child in $(release_process_children "$root_pid"); do
    release_signal_process_tree "$signal" "$child"
  done
  kill "-$signal" "$root_pid" 2>/dev/null || true
}

release_terminate_process_tree() {
  local root_pid="$1"
  local grace_seconds="${2:-5}"
  local waited=0

  kill -0 "$root_pid" 2>/dev/null || return 0
  release_signal_process_tree TERM "$root_pid"
  while kill -0 "$root_pid" 2>/dev/null && [ "$waited" -lt "$grace_seconds" ]; do
    sleep 1
    waited=$((waited + 1))
  done
  if kill -0 "$root_pid" 2>/dev/null; then
    release_signal_process_tree KILL "$root_pid"
  fi
}

release_run_with_timeout() {
  local seconds="$1"
  local label="$2"
  shift 2
  local marker
  local cmd_pid
  local timer_pid
  local exit_code

  if [ "${DRY_RUN:-false}" = true ]; then
    printf '[dry-run] %s:' "$label"
    printf ' %q' "$@"
    printf '\n'
    return 0
  fi

  marker="$(mktemp "${TMPDIR:-/tmp}/aicopilot-timeout.XXXXXX")"
  rm -f "$marker"
  "$@" &
  cmd_pid=$!
  RELEASE_ACTIVE_COMMAND_PID="$cmd_pid"
  (
    sleep "$seconds"
    if kill -0 "$cmd_pid" 2>/dev/null; then
      printf 'Timed out after %s seconds: %s\n' "$seconds" "$label" >&2
      : > "$marker"
      release_terminate_process_tree "$cmd_pid" 5
    fi
  ) &
  timer_pid=$!
  RELEASE_ACTIVE_TIMER_PID="$timer_pid"

  set +e
  wait "$cmd_pid"
  exit_code=$?
  set -e
  release_terminate_process_tree "$timer_pid" 0
  wait "$timer_pid" 2>/dev/null || true
  RELEASE_ACTIVE_COMMAND_PID=""
  RELEASE_ACTIVE_TIMER_PID=""

  if [ -f "$marker" ]; then
    rm -f "$marker"
    return 124
  fi
  rm -f "$marker"
  return "$exit_code"
}

release_cancel_active_timeout() {
  if [ -n "${RELEASE_ACTIVE_TIMER_PID:-}" ]; then
    release_terminate_process_tree "$RELEASE_ACTIVE_TIMER_PID" 0
    wait "$RELEASE_ACTIVE_TIMER_PID" 2>/dev/null || true
  fi
  if [ -n "${RELEASE_ACTIVE_COMMAND_PID:-}" ]; then
    release_terminate_process_tree "$RELEASE_ACTIVE_COMMAND_PID" 2
    wait "$RELEASE_ACTIVE_COMMAND_PID" 2>/dev/null || true
  fi
  RELEASE_ACTIVE_COMMAND_PID=""
  RELEASE_ACTIVE_TIMER_PID=""
}
