#!/usr/bin/env bash
set -euo pipefail

DEPLOY_DIR="${1:-}"
LOCK_TOKEN="${2:-}"
[[ "$DEPLOY_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] || { printf 'Query requires a safe absolute deploy directory.\n' >&2; exit 64; }
[[ "$LOCK_TOKEN" =~ ^[A-Za-z0-9._:-]+$ ]] || { printf 'Query requires a safe deployment token.\n' >&2; exit 64; }

COMMON_SCRIPT="$DEPLOY_DIR/scripts/release-common.sh"
STATE_FILE="$DEPLOY_DIR/releases/invocations/$LOCK_TOKEN.env"
LOCK_DIR="$DEPLOY_DIR/.locks/release.lock.d"
[ -f "$COMMON_SCRIPT" ] || { printf 'Query helper is unavailable: %s\n' "$COMMON_SCRIPT" >&2; exit 66; }
# shellcheck source=release-common.sh
. "$COMMON_SCRIPT"

print_state_file() {
  local key
  local value
  for key in DEPLOY_STATUS DEPLOY_EXIT_CODE DEPLOY_LOCK_TOKEN DEPLOY_INVOCATION_ID DEPLOY_EXPECTED_SHA DEPLOY_SERVER_DEADLINE_EPOCH DEPLOY_UPDATED_AT_UTC; do
    value="$(sed -n "s/^${key}=//p" "$STATE_FILE" | tail -n 1)"
    [ -n "$value" ] && printf '%s=%s\n' "$key" "$value"
  done
}

if [ -f "$STATE_FILE" ]; then
  status="$(sed -n 's/^DEPLOY_STATUS=//p' "$STATE_FILE" | tail -n 1)"
  deadline="$(sed -n 's/^DEPLOY_SERVER_DEADLINE_EPOCH=//p' "$STATE_FILE" | tail -n 1)"
  print_state_file
  case "$status" in
    succeeded|failed-safe|unsafe-partial|committed-cleanup-failed) exit 0 ;;
    active)
      now_epoch="$(release_now_epoch)"
      case "$deadline:$now_epoch" in
        *[!0-9:]*|:*|*:) ;;
        *)
          if [ "$now_epoch" -gt "$deadline" ]; then
            printf 'DEPLOY_RECONCILIATION=deadline-exceeded-unknown\n'
            exit 4
          fi
          ;;
      esac
      printf 'DEPLOY_RECONCILIATION=active\n'
      exit 2
      ;;
  esac
fi

if [ -d "$LOCK_DIR" ] && [ "$(release_lock_value "$LOCK_DIR" token)" = "$LOCK_TOKEN" ]; then
  if [ -d "$LOCK_DIR/.transition.d" ] && [ "$(release_lock_value "$LOCK_DIR/.transition.d" token)" = "$LOCK_TOKEN" ]; then
    printf 'DEPLOY_RECONCILIATION=transitioning\n'
    exit 2
  fi
  printf 'DEPLOY_RECONCILIATION=%s\n' "$(release_lock_value "$LOCK_DIR" state)"
  exit 3
fi

printf 'DEPLOY_RECONCILIATION=unknown\n'
exit 4
