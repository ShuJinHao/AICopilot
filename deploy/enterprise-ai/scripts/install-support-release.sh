#!/usr/bin/env bash
set -euo pipefail

DEPLOY_DIR="${1:-}"
STAGING_DIR="${2:-}"
LOCK_TOKEN="${3:-}"
EXPECTED_DIGEST="${4:-}"
RESERVATION_TTL_SECONDS="${5:-900}"
WORKSPACE_ENTRYPOINT="${6:-}"
WORKSPACE_INVOCATION_ID="${7:-}"
WORKSPACE_EXPECTED_SHA="${8:-}"
WORKSPACE_PLAN_DIGEST="${9:-}"
SERVICES_MANIFEST_DIGEST="${10:-}"
IMAGE_MANIFEST_DIGEST="${11:-}"
WORKSPACE_PROFILE_DIGEST="${12:-}"

[ -n "$DEPLOY_DIR" ] || { printf 'Deploy directory is required.\n' >&2; exit 64; }
[ -n "$STAGING_DIR" ] || { printf 'Support staging directory is required.\n' >&2; exit 64; }
[ -n "$LOCK_TOKEN" ] || { printf 'Release lock token is required.\n' >&2; exit 64; }
[ -n "$EXPECTED_DIGEST" ] || { printf 'Support digest is required.\n' >&2; exit 64; }
[ "$WORKSPACE_ENTRYPOINT" = "1" ] || { printf 'Support install requires the workspace deployment entrypoint marker.\n' >&2; exit 64; }
[[ "$WORKSPACE_INVOCATION_ID" =~ ^[A-Za-z0-9._:-]+$ ]] || { printf 'Support install requires a safe workspace invocation id.\n' >&2; exit 64; }
[[ "$WORKSPACE_EXPECTED_SHA" =~ ^[0-9a-f]{40}$ ]] || { printf 'Support install requires a lowercase full expected Git SHA.\n' >&2; exit 64; }
[[ "$WORKSPACE_PLAN_DIGEST" =~ ^[0-9a-f]{64}$ ]] || { printf 'Support install requires a lowercase SHA256 plan digest.\n' >&2; exit 64; }
[[ "$EXPECTED_DIGEST" =~ ^[0-9a-f]{64}$ ]] || { printf 'Support install requires a lowercase SHA256 support digest.\n' >&2; exit 64; }
[[ "$SERVICES_MANIFEST_DIGEST" =~ ^[0-9a-f]{64}$ ]] || { printf 'Support install requires a lowercase SHA256 services manifest digest.\n' >&2; exit 64; }
[[ "$IMAGE_MANIFEST_DIGEST" =~ ^[0-9a-f]{64}$ ]] || { printf 'Support install requires a lowercase SHA256 image manifest digest.\n' >&2; exit 64; }
[[ "$WORKSPACE_PROFILE_DIGEST" =~ ^[0-9a-f]{64}$ ]] || { printf 'Support install requires a lowercase SHA256 profile digest.\n' >&2; exit 64; }
[[ "$DEPLOY_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] || { printf 'Deploy directory must be a safe absolute path.\n' >&2; exit 64; }
[[ "$STAGING_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] || { printf 'Staging directory must be a safe absolute path.\n' >&2; exit 64; }
case "$DEPLOY_DIR:$STAGING_DIR" in
  *'/../'*|*'/..:'*|*'://'*|/:*) printf 'Unsafe deploy or staging directory.\n' >&2; exit 64 ;;
esac

MANIFEST_FILE="$STAGING_DIR/.aicopilot-support-manifest.sha256"
COMMON_SCRIPT="$STAGING_DIR/scripts/release-common.sh"
RELEASE_LOCK_DIR="$DEPLOY_DIR/.locks/release.lock.d"
LOCK_ACQUIRED=false
INSTALL_COMMITTED=false
SUPPORT_MUTATION_STARTED=false
SUPPORT_RESTORE_FAILED=false
SUPPORT_BACKUP_DIR="$DEPLOY_DIR/.support-backups/$LOCK_TOKEN"
SUPPORT_PATH_LIST="$STAGING_DIR/.support-install-paths"

[ -f "$MANIFEST_FILE" ] || { printf 'Support manifest is missing: %s\n' "$MANIFEST_FILE" >&2; exit 66; }
[ -f "$COMMON_SCRIPT" ] || { printf 'Shared release helper is missing: %s\n' "$COMMON_SCRIPT" >&2; exit 66; }
# shellcheck source=release-common.sh
. "$COMMON_SCRIPT"

restore_support_backup() {
  local relative_path
  local restore_status=0
  [ "$SUPPORT_MUTATION_STARTED" = true ] || return 0
  while IFS= read -r relative_path; do
    [ -n "$relative_path" ] || continue
    release_validate_manifest_path "$relative_path" || { restore_status=1; continue; }
    rm -f "$DEPLOY_DIR/$relative_path" "${DEPLOY_DIR}/${relative_path}.aicopilot-stage.$LOCK_TOKEN" 2>/dev/null || restore_status=1
  done < "$SUPPORT_PATH_LIST"
  if [ -d "$SUPPORT_BACKUP_DIR/tree" ]; then
    while IFS= read -r relative_path; do
      relative_path="${relative_path#./}"
      [ -n "$relative_path" ] || continue
      mkdir -p "$(dirname "$DEPLOY_DIR/$relative_path")" || { restore_status=1; continue; }
      cp -p "$SUPPORT_BACKUP_DIR/tree/$relative_path" "$DEPLOY_DIR/$relative_path" || restore_status=1
    done < <(cd "$SUPPORT_BACKUP_DIR/tree" && find . -type f -print | LC_ALL=C sort)
  fi
  if [ -f "$SUPPORT_BACKUP_DIR/installed-manifest" ]; then
    cp -p "$SUPPORT_BACKUP_DIR/installed-manifest" "$DEPLOY_DIR/.aicopilot-support-manifest.sha256" || restore_status=1
  else
    rm -f "$DEPLOY_DIR/.aicopilot-support-manifest.sha256" || restore_status=1
  fi
  if [ -f "$SUPPORT_BACKUP_DIR/installed-digest" ]; then
    cp -p "$SUPPORT_BACKUP_DIR/installed-digest" "$DEPLOY_DIR/.aicopilot-support-manifest.digest" || restore_status=1
  else
    rm -f "$DEPLOY_DIR/.aicopilot-support-manifest.digest" || restore_status=1
  fi
  [ "$restore_status" -eq 0 ]
}

cleanup_failed_install() {
  local status="$1"
  trap - EXIT HUP INT TERM
  if [ "$INSTALL_COMMITTED" != true ] && ! restore_support_backup; then
    SUPPORT_RESTORE_FAILED=true
    printf 'AICopilot support install restore failed; backup retained: %s\n' "$SUPPORT_BACKUP_DIR" >&2
  fi
  if [ "$LOCK_ACQUIRED" = true ] && [ "$INSTALL_COMMITTED" != true ]; then
    if [ "$SUPPORT_RESTORE_FAILED" = true ]; then
      printf 'Support reservation retained because restore failed: %s\n' "$RELEASE_LOCK_DIR" >&2
    else
      release_unlock "$RELEASE_LOCK_DIR" "$LOCK_TOKEN" || true
    fi
  fi
  rm -rf "$STAGING_DIR" 2>/dev/null || true
  exit "$status"
}

trap 'cleanup_failed_install $?' EXIT
trap 'cleanup_failed_install 129' HUP
trap 'cleanup_failed_install 130' INT
trap 'cleanup_failed_install 143' TERM

actual_digest="$(release_sha256_file "$MANIFEST_FILE")"
if [ "$actual_digest" != "$EXPECTED_DIGEST" ]; then
  printf 'Support manifest digest mismatch before install: expected=%s actual=%s\n' "$EXPECTED_DIGEST" "$actual_digest" >&2
  exit 65
fi
release_verify_sha256_manifest "$STAGING_DIR" "$MANIFEST_FILE"

: > "$SUPPORT_PATH_LIST"
while read -r checksum relative_path; do
  [ -n "$checksum" ] || continue
  relative_path="${relative_path#\*}"
  relative_path="${relative_path# }"
  release_validate_manifest_path "$relative_path"
  printf '%s\n' "$relative_path" >> "$SUPPORT_PATH_LIST"
  case "$relative_path" in
    *.sh) bash -n "$STAGING_DIR/$relative_path" ;;
  esac
done < "$MANIFEST_FILE"

if [ -f "$DEPLOY_DIR/.aicopilot-support-manifest.sha256" ]; then
  while read -r old_checksum old_relative_path; do
    [ -n "$old_checksum" ] || continue
    old_relative_path="${old_relative_path#\*}"
    old_relative_path="${old_relative_path# }"
    release_validate_manifest_path "$old_relative_path"
    release_validate_manifest_path "$old_relative_path"
    printf '%s\n' "$old_relative_path" >> "$SUPPORT_PATH_LIST"
  done < "$DEPLOY_DIR/.aicopilot-support-manifest.sha256"
fi
LC_ALL=C sort -u "$SUPPORT_PATH_LIST" -o "$SUPPORT_PATH_LIST"

release_acquire_lock \
  "$RELEASE_LOCK_DIR" \
  "$LOCK_TOKEN" \
  aicopilot-release \
  reserved \
  support-install \
  "$RESERVATION_TTL_SECONDS"
LOCK_ACQUIRED=true
printf '%s\n' "$WORKSPACE_ENTRYPOINT" > "$RELEASE_LOCK_DIR/workspace-entrypoint"
printf '%s\n' "$WORKSPACE_INVOCATION_ID" > "$RELEASE_LOCK_DIR/invocation-id"
printf '%s\n' "$WORKSPACE_EXPECTED_SHA" > "$RELEASE_LOCK_DIR/expected-sha"
printf '%s\n' "$WORKSPACE_PLAN_DIGEST" > "$RELEASE_LOCK_DIR/plan-digest"
printf '%s\n' "$WORKSPACE_PROFILE_DIGEST" > "$RELEASE_LOCK_DIR/profile-digest"
printf '%s\n' "$EXPECTED_DIGEST" > "$RELEASE_LOCK_DIR/support-digest"
printf '%s\n' "$SERVICES_MANIFEST_DIGEST" > "$RELEASE_LOCK_DIR/services-manifest-digest"
printf '%s\n' "$IMAGE_MANIFEST_DIGEST" > "$RELEASE_LOCK_DIR/image-manifest-digest"

mkdir -p "$SUPPORT_BACKUP_DIR/tree"
if [ -f "$DEPLOY_DIR/.aicopilot-support-manifest.sha256" ]; then
  cp -p "$DEPLOY_DIR/.aicopilot-support-manifest.sha256" "$SUPPORT_BACKUP_DIR/installed-manifest"
fi
if [ -f "$DEPLOY_DIR/.aicopilot-support-manifest.digest" ]; then
  cp -p "$DEPLOY_DIR/.aicopilot-support-manifest.digest" "$SUPPORT_BACKUP_DIR/installed-digest"
fi
while IFS= read -r relative_path; do
  [ -n "$relative_path" ] || continue
  if [ -f "$DEPLOY_DIR/$relative_path" ]; then
    mkdir -p "$(dirname "$SUPPORT_BACKUP_DIR/tree/$relative_path")"
    cp -p "$DEPLOY_DIR/$relative_path" "$SUPPORT_BACKUP_DIR/tree/$relative_path"
  fi
done < "$SUPPORT_PATH_LIST"
SUPPORT_MUTATION_STARTED=true

while read -r checksum relative_path; do
  [ -n "$checksum" ] || continue
  relative_path="${relative_path#\*}"
  relative_path="${relative_path# }"
  release_validate_manifest_path "$relative_path"

  destination="$DEPLOY_DIR/$relative_path"
  staged_destination="${destination}.aicopilot-stage.$LOCK_TOKEN"
  mkdir -p "$(dirname "$destination")"
  cp -p "$STAGING_DIR/$relative_path" "$staged_destination"
done < "$MANIFEST_FILE"

while read -r checksum relative_path; do
  [ -n "$checksum" ] || continue
  relative_path="${relative_path#\*}"
  relative_path="${relative_path# }"
  destination="$DEPLOY_DIR/$relative_path"
  staged_destination="${destination}.aicopilot-stage.$LOCK_TOKEN"
  mv "$staged_destination" "$destination"
done < "$MANIFEST_FILE"

chmod +x "$DEPLOY_DIR/deploy-release.sh" "$DEPLOY_DIR/post-release-cleanup.sh" "$DEPLOY_DIR/harbor-retention.sh"
find "$DEPLOY_DIR/scripts" -maxdepth 1 -type f -name '*.sh' -exec chmod +x {} +

if [ -f "$DEPLOY_DIR/.aicopilot-support-manifest.sha256" ]; then
  while read -r old_checksum old_relative_path; do
    [ -n "$old_checksum" ] || continue
    old_relative_path="${old_relative_path#\*}"
    old_relative_path="${old_relative_path# }"
    if ! awk -v target="$old_relative_path" '{ path=$2; sub(/^\*/, "", path); if (path == target) found=1 } END { exit(found ? 0 : 1) }' "$MANIFEST_FILE"; then
      case "$old_relative_path" in
        scripts/*|cloud-readonly/*)
          rm -f "$DEPLOY_DIR/$old_relative_path"
          ;;
      esac
    fi
  done < "$DEPLOY_DIR/.aicopilot-support-manifest.sha256"
fi

manifest_temp="$DEPLOY_DIR/.aicopilot-support-manifest.sha256.aicopilot-stage.$LOCK_TOKEN"
digest_temp="$DEPLOY_DIR/.aicopilot-support-manifest.digest.aicopilot-stage.$LOCK_TOKEN"
cp "$MANIFEST_FILE" "$manifest_temp"
printf '%s\n' "$EXPECTED_DIGEST" > "$digest_temp"
mv "$manifest_temp" "$DEPLOY_DIR/.aicopilot-support-manifest.sha256"
mv "$digest_temp" "$DEPLOY_DIR/.aicopilot-support-manifest.digest"
release_update_lock_phase "$RELEASE_LOCK_DIR" "$LOCK_TOKEN" support-ready

DEPLOY_DIR="$DEPLOY_DIR" "$DEPLOY_DIR/scripts/check-release-state-access.sh"
release_verify_sha256_manifest "$DEPLOY_DIR" "$DEPLOY_DIR/.aicopilot-support-manifest.sha256"

INSTALL_COMMITTED=true
rm -rf "$SUPPORT_BACKUP_DIR"
rm -rf "$STAGING_DIR"
trap - EXIT HUP INT TERM
printf 'AICopilot support release installed and reserved: digest=%s token=%s invocation=%s expectedSha=%s planDigest=%s\n' \
  "$EXPECTED_DIGEST" "$LOCK_TOKEN" "$WORKSPACE_INVOCATION_ID" "$WORKSPACE_EXPECTED_SHA" "$WORKSPACE_PLAN_DIGEST"
