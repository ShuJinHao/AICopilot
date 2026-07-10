#!/usr/bin/env bash

# Shared deployment primitives. This file is sourced by local and remote
# release scripts, so it intentionally does not change the caller's shell
# options.

release_now_epoch() {
  date +%s
}

release_require_canonical_decimal() {
  local value="$1"
  local label="$2"
  local minimum="$3"
  local maximum="$4"
  case "$value" in
    ''|*[!0-9]*|0[0-9]*) printf '%s must be a canonical decimal integer between %s and %s.\n' "$label" "$minimum" "$maximum" >&2; return 64 ;;
    *) ;;
  esac
  if [ "$value" -lt "$minimum" ] || [ "$value" -gt "$maximum" ]; then
    printf '%s must be between %s and %s.\n' "$label" "$minimum" "$maximum" >&2
    return 64
  fi
}

release_require_safe_token() {
  local value="$1"
  local label="$2"
  if [[ ! "$value" =~ ^[A-Za-z0-9._:-]+$ ]]; then
    printf '%s must contain only A-Za-z0-9._:- characters.\n' "$label" >&2
    return 64
  fi
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

release_transition_is_active() {
  local transition_dir="$1/.transition.d"
  local pid
  local expected_start
  local actual_start
  local expires_at
  local now_epoch

  [ -d "$transition_dir" ] || return 1
  pid="$(release_lock_value "$transition_dir" pid)"
  expires_at="$(release_lock_value "$transition_dir" expires-at-epoch)"
  case "$pid:$expires_at" in
    *[!0-9:]*|:*|*:) return 1 ;;
  esac
  expected_start="$(release_lock_value "$transition_dir" process-start)"
  if [ -n "$expected_start" ] && kill -0 "$pid" 2>/dev/null; then
    actual_start="$(release_process_start_token "$pid")"
    if [ -n "$actual_start" ] && [ "$actual_start" = "$expected_start" ]; then
      return 0
    fi
  fi

  # A live process identity is authoritative even after the transition TTL.
  # TTL is only a grace period before reclaiming a dead/orphaned transition.
  now_epoch="$(release_now_epoch)"
  release_require_canonical_decimal "$now_epoch" "Current epoch" 1 99999999999 >/dev/null 2>&1 || return 1
  [ "$expires_at" -gt "$now_epoch" ]
}

release_remove_stale_transition() {
  local lock_dir="$1"
  local transition_dir="$lock_dir/.transition.d"
  local stale_dir="$lock_dir/.transition.stale.$(release_now_epoch).$$"

  [ -d "$transition_dir" ] || return 0
  release_transition_is_active "$lock_dir" && return 75
  if mv "$transition_dir" "$stale_dir" 2>/dev/null; then
    rm -rf "$stale_dir"
    printf 'Removed stale deployment lock transition: %s\n' "$lock_dir" >&2
    return 0
  fi
  return 75
}

release_lock_is_active() {
  local lock_dir="$1"
  local state
  local pid
  local expected_start
  local actual_start
  local expires_at

  [ -d "$lock_dir" ] || return 1
  if [ -d "$lock_dir/.transition.d" ]; then
    if release_transition_is_active "$lock_dir"; then
      return 0
    fi
    release_remove_stale_transition "$lock_dir" || return 0
  fi
  state="$(release_lock_value "$lock_dir" state)"
  if [ "$state" = "blocked" ]; then
    return 0
  fi
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
  local now_epoch

  release_require_safe_token "$token" "Deployment lock token" || return $?
  release_require_canonical_decimal "$ttl_seconds" "Deployment lock TTL" 0 86400 || return $?
  if [ "$state" = "reserved" ] && [ "$ttl_seconds" -lt 1 ]; then
    printf 'Reserved deployment lock TTL must be greater than zero.\n' >&2
    return 64
  fi

  mkdir -p "$(dirname "$lock_dir")"
  while [ "$attempts" -lt 5 ]; do
    if mkdir "$lock_dir" 2>/dev/null; then
      if [ "$state" = "reserved" ]; then
        now_epoch="$(release_now_epoch)"
        release_require_canonical_decimal "$now_epoch" "Current epoch" 1 99999999999 || return $?
        expires_at=$((now_epoch + ttl_seconds))
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
  local expires_at
  local now_epoch

  release_begin_lock_transition "$lock_dir" "$token" reserved || return $?

  existing_token="$(release_lock_value "$lock_dir" token)"
  state="$(release_lock_value "$lock_dir" state)"
  if [ "$state" != "reserved" ] || [ -z "$existing_token" ] || [ "$existing_token" != "$token" ]; then
    printf 'Deployment lock reservation does not match this release: %s\n' "$lock_dir" >&2
    release_print_lock_diagnostics "$lock_dir"
    release_end_lock_transition "$lock_dir" "$token" || true
    return 75
  fi

  expires_at="$(release_lock_value "$lock_dir" expires-at-epoch)"
  now_epoch="$(release_now_epoch)"
  case "$expires_at:$now_epoch" in
    *[!0-9:]*|:*|*:)
      printf 'Deployment lock reservation expiry metadata is invalid: %s\n' "$lock_dir" >&2
      release_end_lock_transition "$lock_dir" "$token" || true
      return 75
      ;;
  esac
  if [ "$expires_at" -le "$now_epoch" ]; then
    printf 'Deployment lock reservation expired before release start: %s\n' "$lock_dir" >&2
    release_end_lock_transition "$lock_dir" "$token" || true
    return 75
  fi

  release_write_lock_metadata "$lock_dir" "$token" "$kind" active "$phase" 0 "$$" || return $?
  release_end_lock_transition "$lock_dir" "$token"
}

release_begin_lock_transition() {
  local lock_dir="$1"
  local token="$2"
  local expected_state="$3"
  local transition_dir="$lock_dir/.transition.d"
  local attempts=0
  local now_epoch
  local expires_at
  local process_start
  release_require_safe_token "$token" "Deployment lock transition token" || return $?
  [ -d "$lock_dir" ] || return 75
  while ! mkdir "$transition_dir" 2>/dev/null; do
    if release_transition_is_active "$lock_dir"; then
      printf 'Deployment lock transition is already owned by another live process: %s\n' "$lock_dir" >&2
      return 75
    fi
    release_remove_stale_transition "$lock_dir" || return 75
    attempts=$((attempts + 1))
    [ "$attempts" -lt 3 ] || return 75
  done
  now_epoch="$(release_now_epoch)"
  release_require_canonical_decimal "$now_epoch" "Current epoch" 1 99999999999 || { rm -rf "$transition_dir"; return 64; }
  expires_at=$((now_epoch + 30))
  process_start="$(release_process_start_token "$$")"
  if [ -z "$process_start" ]; then
    rm -rf "$transition_dir"
    printf 'Could not capture deployment transition process identity.\n' >&2
    return 75
  fi
  printf '%s\n' "$token" > "$transition_dir/token"
  printf '%s\n' "$$" > "$transition_dir/pid"
  printf '%s\n' "$process_start" > "$transition_dir/process-start"
  printf '%s\n' "$(id -un 2>/dev/null || id -u)@$(hostname 2>/dev/null || printf unknown)" > "$transition_dir/owner"
  printf '%s\n' "$now_epoch" > "$transition_dir/updated-at-epoch"
  printf '%s\n' "$expires_at" > "$transition_dir/expires-at-epoch"
  if [ "$(release_lock_value "$lock_dir" token)" != "$token" ] ||
     [ "$(release_lock_value "$lock_dir" state)" != "$expected_state" ]; then
    rm -rf "$transition_dir"
    return 75
  fi
}

release_reserve_owned_lock() {
  local lock_dir="$1"
  local token="$2"
  local kind="$3"
  local phase="$4"
  local ttl_seconds="$5"
  local now_epoch
  local expires_at

  release_require_canonical_decimal "$ttl_seconds" "Deployment lock TTL" 1 86400 || return $?
  release_begin_lock_transition "$lock_dir" "$token" active || return $?
  now_epoch="$(release_now_epoch)"
  release_require_canonical_decimal "$now_epoch" "Current epoch" 1 99999999999 || {
    release_end_lock_transition "$lock_dir" "$token" || true
    return 64
  }
  expires_at=$((now_epoch + ttl_seconds))
  release_write_lock_metadata "$lock_dir" "$token" "$kind" reserved "$phase" "$expires_at" "$$" || {
    release_end_lock_transition "$lock_dir" "$token" || true
    return 75
  }
  release_end_lock_transition "$lock_dir" "$token"
}

release_end_lock_transition() {
  local lock_dir="$1"
  local token="$2"
  local transition_dir="$lock_dir/.transition.d"
  [ -d "$transition_dir" ] || return 0
  [ "$(release_lock_value "$transition_dir" token)" = "$token" ] || return 75
  rm -rf "$transition_dir"
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

release_mark_lock_blocked() {
  local lock_dir="$1"
  local token="$2"
  local phase="$3"

  [ -d "$lock_dir" ] || return 75
  [ "$(release_lock_value "$lock_dir" token)" = "$token" ] || return 75
  printf '%s\n' blocked > "$lock_dir/state"
  printf '%s\n' "$phase" > "$lock_dir/phase"
  printf '%s\n' 0 > "$lock_dir/expires-at-epoch"
  printf '%s\n' "$(release_now_epoch)" > "$lock_dir/updated-at-epoch"
}

release_sha256_file() {
  local path="$1"

  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{print $1}'
  else
    shasum -a 256 "$path" | awk '{print $1}'
  fi
}

release_sha256_stream() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum | awk '{print $1}'
  else
    shasum -a 256 | awk '{print $1}'
  fi
}

release_validate_manifest_path() {
  local path="$1"
  case "$path" in
    ''|*[!A-Za-z0-9._/-]*|.|./*|/*|*//*|*/|../*|*/../*|*/..|*/./*|*/.|.env|releases|releases/*|backups|backups/*|.locks|.locks/*|.support-staging|.support-staging/*|.support-backups|.support-backups/*)
      printf 'Unsafe or protected support manifest path: %s\n' "$path" >&2
      return 65
      ;;
  esac
  return 0
}

release_validate_manifest_target() {
  local root="$1"
  local path="$2"
  local root_physical
  local current
  local component
  local parent_physical
  local -a path_components

  release_validate_manifest_path "$path" || return $?
  [ -d "$root" ] || {
    printf 'Support manifest root is missing: %s\n' "$root" >&2
    return 66
  }
  root_physical="$(cd "$root" 2>/dev/null && pwd -P)" || return 66
  current="$root_physical"
  IFS='/' read -r -a path_components <<< "$path"
  for component in "${path_components[@]}"; do
    current="$current/$component"
    if [ -L "$current" ]; then
      printf 'Support manifest path traverses a symbolic link: %s\n' "$path" >&2
      return 65
    fi
  done
  parent_physical="$(cd "$(dirname "$current")" 2>/dev/null && pwd -P)" || parent_physical=""
  if [ -n "$parent_physical" ]; then
    case "$parent_physical/" in
      "$root_physical/"|"$root_physical/"*) ;;
      *)
        printf 'Support manifest path escapes its physical root: %s\n' "$path" >&2
        return 65
        ;;
    esac
  fi
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
    release_validate_manifest_target "$root" "$path" || return $?
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

release_mark_unsafe_partial() {
  local deploy_dir="$1"
  local token="$2"
  local reason="$3"
  local backup_dir="$deploy_dir/.support-backups/$token"
  local releases_dir="$deploy_dir/releases"
  local temp_file=""
  local marker_status=0

  release_require_safe_token "$token" "Unsafe partial token" || return $?
  if [ "${AICOPILOT_NON_PRODUCTION_MECHANISM_TEST:-false}" = true ] &&
     [ "${AICOPILOT_TEST_FORCE_UNSAFE_MARKER_WRITE_FAILURE:-false}" = true ]; then
    printf 'NON_PRODUCTION_MECHANISM_TEST forced unsafe-marker persistence failure.\n' >&2
    return 73
  fi
  mkdir -p "$backup_dir" 2>/dev/null || marker_status=1
  {
    printf 'DEPLOY_STATUS=unsafe-partial\n'
    printf 'DEPLOY_LOCK_TOKEN=%s\n' "$token"
    printf 'DEPLOY_FAILURE_REASON=%s\n' "$reason"
    printf 'DEPLOY_BLOCKED_AT_UTC=%s\n' "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  } > "$backup_dir/UNSAFE_PARTIAL" 2>/dev/null || marker_status=1
  if mkdir -p "$releases_dir" 2>/dev/null; then
    temp_file="$(mktemp "$releases_dir/.blocked-release.XXXXXX" 2>/dev/null || true)"
    if [ -n "$temp_file" ]; then
      cp "$backup_dir/UNSAFE_PARTIAL" "$temp_file" 2>/dev/null && mv "$temp_file" "$releases_dir/blocked-release.env" 2>/dev/null || marker_status=1
      rm -f "$temp_file" 2>/dev/null || true
    else
      marker_status=1
    fi
  else
    marker_status=1
  fi
  return "$marker_status"
}

release_find_unsafe_partial_marker() {
  local deploy_dir="$1"
  find "$deploy_dir/.support-backups" -mindepth 2 -maxdepth 2 -type f -name UNSAFE_PARTIAL -print -quit 2>/dev/null || true
}

release_find_unowned_support_backup() {
  local deploy_dir="$1"
  local lock_dir="$deploy_dir/.locks/release.lock.d"
  local backup_dir
  local token
  local lock_token
  local lock_state
  local expires_at
  local pid
  local expected_start
  local actual_start
  local normal_live_reservation

  for backup_dir in "$deploy_dir"/.support-backups/*; do
    [ -d "$backup_dir" ] || continue
    token="$(basename "$backup_dir")"
    release_require_safe_token "$token" "Support backup token" >/dev/null 2>&1 || {
      printf '%s\n' "$backup_dir"
      return 0
    }
    lock_token="$(release_lock_value "$lock_dir" token)"
    lock_state="$(release_lock_value "$lock_dir" state)"
    normal_live_reservation=false
    if [ "$lock_token" = "$token" ] && [ "$lock_state" = reserved ]; then
      expires_at="$(release_lock_value "$lock_dir" expires-at-epoch)"
      case "$expires_at" in
        ''|*[!0-9]*) ;;
        *) [ "$expires_at" -gt "$(release_now_epoch)" ] && normal_live_reservation=true ;;
      esac
    elif [ "$lock_token" = "$token" ] && [ "$lock_state" = active ]; then
      pid="$(release_lock_value "$lock_dir" pid)"
      expected_start="$(release_lock_value "$lock_dir" process-start)"
      case "$pid" in
        ''|*[!0-9]*) ;;
        *)
          if [ -n "$expected_start" ] && kill -0 "$pid" 2>/dev/null; then
            actual_start="$(release_process_start_token "$pid")"
            [ -n "$actual_start" ] && [ "$actual_start" = "$expected_start" ] && normal_live_reservation=true
          fi
          ;;
      esac
    fi
    if [ "$normal_live_reservation" = true ]; then
      continue
    fi
    printf '%s\n' "$backup_dir"
    return 0
  done
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

  release_require_canonical_decimal "$grace_seconds" "Process termination grace seconds" 0 3600 || return $?

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
  local errexit_was_set=false

  release_require_canonical_decimal "$seconds" "Command timeout seconds" 1 86400 || return $?

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

  case "$-" in
    *e*) errexit_was_set=true ;;
  esac
  set +e
  wait "$cmd_pid"
  exit_code=$?
  if [ "$errexit_was_set" = true ]; then
    set -e
  else
    set +e
  fi
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
