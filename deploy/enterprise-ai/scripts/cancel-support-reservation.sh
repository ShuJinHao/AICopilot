#!/usr/bin/env bash
set -euo pipefail

DEPLOY_DIR="${1:-}"
LOCK_TOKEN="${2:-}"
[[ "$DEPLOY_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] || { printf 'Cancel requires a safe absolute deploy directory.\n' >&2; exit 64; }
[[ "$LOCK_TOKEN" =~ ^[A-Za-z0-9._:-]+$ ]] || { printf 'Cancel requires a safe reservation token.\n' >&2; exit 64; }

COMMON_SCRIPT="$DEPLOY_DIR/scripts/release-common.sh"
LOCK_DIR="$DEPLOY_DIR/.locks/release.lock.d"
BACKUP_DIR="$DEPLOY_DIR/.support-backups/$LOCK_TOKEN"
PATHS_FILE="$BACKUP_DIR/paths"
MANIFEST_FILE="$DEPLOY_DIR/.aicopilot-support-manifest.sha256"
DIGEST_FILE="$DEPLOY_DIR/.aicopilot-support-manifest.digest"
[ -f "$COMMON_SCRIPT" ] || { printf 'Cancel helper is unavailable: %s\n' "$COMMON_SCRIPT" >&2; exit 66; }
# shellcheck source=release-common.sh
. "$COMMON_SCRIPT"

unsafe_cancel() {
  local reason="$1"
  release_mark_unsafe_partial "$DEPLOY_DIR" "$LOCK_TOKEN" "$reason" || true
  release_mark_lock_blocked "$LOCK_DIR" "$LOCK_TOKEN" "$reason" || true
  printf 'AICopilot reservation cancellation is unsafe-partial; transition and backup are retained: reason=%s\n' "$reason" >&2
  exit 86
}

if [ ! -d "$LOCK_DIR" ]; then
  printf 'AICopilot reservation already absent: token=%s\n' "$LOCK_TOKEN"
  exit 0
fi
if ! release_begin_lock_transition "$LOCK_DIR" "$LOCK_TOKEN" reserved; then
  printf 'AICopilot reservation was not cancelled because it is active, owned by another token, or transitioning: token=%s\n' "$LOCK_TOKEN" >&2
  exit 3
fi

restore_status=0
if [ ! -d "$BACKUP_DIR" ] || [ ! -f "$PATHS_FILE" ]; then
  printf 'AICopilot reservation backup is missing; cancellation is blocked: %s\n' "$BACKUP_DIR" >&2
  unsafe_cancel support-cancel-backup-missing
fi
while IFS= read -r relative_path; do
  [ -n "$relative_path" ] || continue
  release_validate_manifest_target "$DEPLOY_DIR" "$relative_path" || { restore_status=1; continue; }
  rm -f "$DEPLOY_DIR/$relative_path" 2>/dev/null || restore_status=1
done < "$PATHS_FILE"
if [ -d "$BACKUP_DIR/tree" ]; then
  while IFS= read -r relative_path; do
    relative_path="${relative_path#./}"
    [ -n "$relative_path" ] || continue
    release_validate_manifest_target "$BACKUP_DIR/tree" "$relative_path" || { restore_status=1; continue; }
    release_validate_manifest_target "$DEPLOY_DIR" "$relative_path" || { restore_status=1; continue; }
    mkdir -p "$(dirname "$DEPLOY_DIR/$relative_path")" || { restore_status=1; continue; }
    cp -p "$BACKUP_DIR/tree/$relative_path" "$DEPLOY_DIR/$relative_path" || restore_status=1
  done < <(cd "$BACKUP_DIR/tree" && find . -type f -print | LC_ALL=C sort)
fi
if [ -f "$BACKUP_DIR/installed-manifest" ]; then
  cp -p "$BACKUP_DIR/installed-manifest" "$MANIFEST_FILE" || restore_status=1
else
  rm -f "$MANIFEST_FILE" || restore_status=1
fi
if [ -f "$BACKUP_DIR/installed-digest" ]; then
  cp -p "$BACKUP_DIR/installed-digest" "$DIGEST_FILE" || restore_status=1
else
  rm -f "$DIGEST_FILE" || restore_status=1
fi
if [ "$restore_status" -ne 0 ]; then
  unsafe_cancel support-cancel-restore-failed
fi
if [ -f "$MANIFEST_FILE" ]; then
  release_verify_sha256_manifest "$DEPLOY_DIR" "$MANIFEST_FILE" || unsafe_cancel support-cancel-verification-failed
fi

release_unlock "$LOCK_DIR" "$LOCK_TOKEN" || unsafe_cancel support-cancel-unlock-failed
rm -rf "$BACKUP_DIR"
printf 'AICopilot reserved support was atomically cancelled and restored: token=%s\n' "$LOCK_TOKEN"
