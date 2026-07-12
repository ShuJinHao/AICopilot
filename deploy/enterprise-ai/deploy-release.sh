#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-$SCRIPT_DIR}"
ENV_FILE="${ENV_FILE:-$DEPLOY_DIR/.env}"
COMPOSE_FILE="${COMPOSE_FILE:-$DEPLOY_DIR/docker-compose.yaml}"
# shellcheck source=scripts/release-common.sh
. "$DEPLOY_DIR/scripts/release-common.sh"
RELEASES_DIR="$DEPLOY_DIR/releases"
RELEASE_HISTORY_DIR="$RELEASES_DIR/history"
CURRENT_RELEASE_FILE="$RELEASES_DIR/current-release.env"
PREVIOUS_RELEASE_FILE="$RELEASES_DIR/previous-release.env"
STAGED_RELEASE_FILE="$RELEASES_DIR/staged-release.env"
CURRENT_RELEASE_SUMMARY_FILE="$RELEASES_DIR/current-release.summary.md"
BLOCKED_RELEASE_FILE="$RELEASES_DIR/blocked-release.env"
SUPPORT_MANIFEST_FILE="$DEPLOY_DIR/.aicopilot-support-manifest.sha256"
SUPPORT_DIGEST_FILE="$DEPLOY_DIR/.aicopilot-support-manifest.digest"
RELEASE_LOCK_DIR="${AICOPILOT_RELEASE_LOCK_DIR:-$DEPLOY_DIR/.locks/release.lock.d}"
DEPLOY_LOCK_TOKEN="${DEPLOY_LOCK_TOKEN:-}"
EXPECTED_SUPPORT_DIGEST="${EXPECTED_SUPPORT_DIGEST:-}"
WORKSPACE_ENTRYPOINT="${IIOT_WORKSPACE_DEPLOY_ENTRYPOINT:-}"
WORKSPACE_INVOCATION_ID="${IIOT_WORKSPACE_DEPLOY_INVOCATION_ID:-}"
WORKSPACE_EXPECTED_SHA="${IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA:-}"
WORKSPACE_PLAN_DIGEST="${IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST:-}"
WORKSPACE_PROFILE_DIGEST="${IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST:-}"
SERVICES_MANIFEST_DIGEST="${DEPLOY_SERVICES_MANIFEST_DIGEST:-}"
IMAGE_MANIFEST_DIGEST="${DEPLOY_IMAGE_MANIFEST_DIGEST:-}"
CANDIDATE_DEPLOY_GIT_SHA="${DEPLOY_GIT_SHA:-}"
SERVER_DEADLINE_EPOCH="${DEPLOY_SERVER_DEADLINE_EPOCH:-}"
ACTIVE_SUPPORT_DIGEST=""
RELEASE_LOCK_HELD=false
RELEASE_COMMITTED=false
STATE_TRANSACTION_DIR=""
CONTAINER_UPDATE_STARTED=false
MIGRATION_EXECUTED=false
RECOVERY_SUCCEEDED=false
KEEP_FAILURE_LOCK=false
FROZEN_AICOPILOT_HTTPAPI_IMAGE=""
FROZEN_AICOPILOT_MIGRATION_IMAGE=""
FROZEN_AICOPILOT_DATAWORKER_IMAGE=""
FROZEN_AICOPILOT_RAGWORKER_IMAGE=""
FROZEN_AICOPILOT_WEBUI_IMAGE=""
FROZEN_AICOPILOT_HTTPAPI_IMAGE_TAG=""
FROZEN_AICOPILOT_MIGRATION_IMAGE_TAG=""
FROZEN_AICOPILOT_DATAWORKER_IMAGE_TAG=""
FROZEN_AICOPILOT_RAGWORKER_IMAGE_TAG=""
FROZEN_AICOPILOT_WEBUI_IMAGE_TAG=""
FROZEN_AICOPILOT_HTTPAPI_IMAGE_DIGEST=""
FROZEN_AICOPILOT_MIGRATION_IMAGE_DIGEST=""
FROZEN_AICOPILOT_DATAWORKER_IMAGE_DIGEST=""
FROZEN_AICOPILOT_RAGWORKER_IMAGE_DIGEST=""
FROZEN_AICOPILOT_WEBUI_IMAGE_DIGEST=""
CONFIG_FINGERPRINT=""
RUNTIME_AICOPILOT_HTTPAPI_IMAGE_DIGEST=""
RUNTIME_AICOPILOT_DATAWORKER_IMAGE_DIGEST=""
RUNTIME_AICOPILOT_RAGWORKER_IMAGE_DIGEST=""
RUNTIME_AICOPILOT_WEBUI_IMAGE_DIGEST=""
PREVIOUS_POSTGRES_IMAGE_REF=""
PREVIOUS_POSTGRES_RUNTIME_DIGEST=""
PREVIOUS_RABBITMQ_IMAGE_REF=""
PREVIOUS_RABBITMQ_RUNTIME_DIGEST=""
PREVIOUS_QDRANT_IMAGE_REF=""
PREVIOUS_QDRANT_RUNTIME_DIGEST=""
RUNTIME_POSTGRES_IMAGE_REF=""
RUNTIME_POSTGRES_IMAGE_DIGEST=""
RUNTIME_RABBITMQ_IMAGE_REF=""
RUNTIME_RABBITMQ_IMAGE_DIGEST=""
RUNTIME_QDRANT_IMAGE_REF=""
RUNTIME_QDRANT_IMAGE_DIGEST=""
MIGRATION_COMPLETED=false
UNSAFE_PARTIAL_EXIT=86
INVOCATION_STATE_INITIALIZED=false
INVOCATION_STATE_FILE=""

APP_IMAGE_KEYS=(
  AICOPILOT_HTTPAPI_IMAGE
  AICOPILOT_MIGRATION_IMAGE
  AICOPILOT_DATAWORKER_IMAGE
  AICOPILOT_RAGWORKER_IMAGE
  AICOPILOT_WEBUI_IMAGE
)

INFRA_IMAGE_KEYS=(
  POSTGRES_IMAGE
  RABBITMQ_IMAGE
  QDRANT_IMAGE
)

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

ensure_release_tag() {
  local release_tag="$1"
  if [[ ! "$release_tag" =~ ^sha-[0-9a-f]+$ ]]; then
    printf 'Release tag must match sha-<hex>: %s\n' "$release_tag" >&2
    exit 64
  fi
}

candidate_fail() {
  printf 'AICopilot deployment candidate validation failed: %s\n' "$*" >&2
  exit 65
}

require_candidate_sha256() {
  local value="$1"
  local label="$2"
  [[ "$value" =~ ^[0-9a-f]{64}$ ]] || candidate_fail "$label must be a lowercase 64-character SHA256 digest."
}

candidate_service_name_for_deploy_service() {
  case "$1" in
    aicopilot-httpapi) printf '%s\n' httpapi ;;
    aicopilot-migration) printf '%s\n' migration ;;
    aicopilot-dataworker) printf '%s\n' dataworker ;;
    aicopilot-ragworker) printf '%s\n' ragworker ;;
    aicopilot-webui) printf '%s\n' web ;;
    *) candidate_fail "unsupported normalized service $1" ;;
  esac
}

set_frozen_candidate_image() {
  local key="$1"
  local value="$2"
  case "$key" in
    AICOPILOT_HTTPAPI_IMAGE) FROZEN_AICOPILOT_HTTPAPI_IMAGE="$value" ;;
    AICOPILOT_MIGRATION_IMAGE) FROZEN_AICOPILOT_MIGRATION_IMAGE="$value" ;;
    AICOPILOT_DATAWORKER_IMAGE) FROZEN_AICOPILOT_DATAWORKER_IMAGE="$value" ;;
    AICOPILOT_RAGWORKER_IMAGE) FROZEN_AICOPILOT_RAGWORKER_IMAGE="$value" ;;
    AICOPILOT_WEBUI_IMAGE) FROZEN_AICOPILOT_WEBUI_IMAGE="$value" ;;
    *) candidate_fail "unsupported candidate image key $key" ;;
  esac
}

frozen_candidate_image() {
  case "$1" in
    AICOPILOT_HTTPAPI_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_HTTPAPI_IMAGE" ;;
    AICOPILOT_MIGRATION_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_MIGRATION_IMAGE" ;;
    AICOPILOT_DATAWORKER_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_DATAWORKER_IMAGE" ;;
    AICOPILOT_RAGWORKER_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_RAGWORKER_IMAGE" ;;
    AICOPILOT_WEBUI_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_WEBUI_IMAGE" ;;
    *) candidate_fail "unsupported candidate image key $1" ;;
  esac
}

set_frozen_candidate_image_tag() {
  local key="$1"
  local value="$2"
  case "$key" in
    AICOPILOT_HTTPAPI_IMAGE) FROZEN_AICOPILOT_HTTPAPI_IMAGE_TAG="$value" ;;
    AICOPILOT_MIGRATION_IMAGE) FROZEN_AICOPILOT_MIGRATION_IMAGE_TAG="$value" ;;
    AICOPILOT_DATAWORKER_IMAGE) FROZEN_AICOPILOT_DATAWORKER_IMAGE_TAG="$value" ;;
    AICOPILOT_RAGWORKER_IMAGE) FROZEN_AICOPILOT_RAGWORKER_IMAGE_TAG="$value" ;;
    AICOPILOT_WEBUI_IMAGE) FROZEN_AICOPILOT_WEBUI_IMAGE_TAG="$value" ;;
    *) candidate_fail "unsupported candidate image key $key" ;;
  esac
}

frozen_candidate_image_tag() {
  case "$1" in
    AICOPILOT_HTTPAPI_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_HTTPAPI_IMAGE_TAG" ;;
    AICOPILOT_MIGRATION_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_MIGRATION_IMAGE_TAG" ;;
    AICOPILOT_DATAWORKER_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_DATAWORKER_IMAGE_TAG" ;;
    AICOPILOT_RAGWORKER_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_RAGWORKER_IMAGE_TAG" ;;
    AICOPILOT_WEBUI_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_WEBUI_IMAGE_TAG" ;;
    *) candidate_fail "unsupported candidate image key $1" ;;
  esac
}

set_frozen_candidate_image_digest() {
  local key="$1"
  local value="$2"
  case "$key" in
    AICOPILOT_HTTPAPI_IMAGE) FROZEN_AICOPILOT_HTTPAPI_IMAGE_DIGEST="$value" ;;
    AICOPILOT_MIGRATION_IMAGE) FROZEN_AICOPILOT_MIGRATION_IMAGE_DIGEST="$value" ;;
    AICOPILOT_DATAWORKER_IMAGE) FROZEN_AICOPILOT_DATAWORKER_IMAGE_DIGEST="$value" ;;
    AICOPILOT_RAGWORKER_IMAGE) FROZEN_AICOPILOT_RAGWORKER_IMAGE_DIGEST="$value" ;;
    AICOPILOT_WEBUI_IMAGE) FROZEN_AICOPILOT_WEBUI_IMAGE_DIGEST="$value" ;;
    *) candidate_fail "unsupported candidate image key $key" ;;
  esac
}

frozen_candidate_image_digest() {
  case "$1" in
    AICOPILOT_HTTPAPI_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_HTTPAPI_IMAGE_DIGEST" ;;
    AICOPILOT_MIGRATION_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_MIGRATION_IMAGE_DIGEST" ;;
    AICOPILOT_DATAWORKER_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_DATAWORKER_IMAGE_DIGEST" ;;
    AICOPILOT_RAGWORKER_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_RAGWORKER_IMAGE_DIGEST" ;;
    AICOPILOT_WEBUI_IMAGE) printf '%s\n' "$FROZEN_AICOPILOT_WEBUI_IMAGE_DIGEST" ;;
    *) candidate_fail "unsupported candidate image key $1" ;;
  esac
}

verify_workspace_candidate_basics() {
  local release_sha="${RELEASE_TAG#sha-}"
  local deploy_git_sha="$CANDIDATE_DEPLOY_GIT_SHA"
  local canonical_services=""
  local service
  local key
  local candidate_variable
  local candidate_value
  local candidate_tag_variable
  local candidate_tag_value
  local candidate_digest_variable
  local candidate_digest_value
  local candidate_repository
  local services_temp
  local images_temp
  local actual_services_digest
  local actual_image_digest

  [ "$WORKSPACE_ENTRYPOINT" = "1" ] || candidate_fail "IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 is required."
  [[ "$WORKSPACE_INVOCATION_ID" =~ ^[A-Za-z0-9._:-]+$ ]] || candidate_fail "a safe non-empty invocation id is required."
  [[ "$DEPLOY_LOCK_TOKEN" =~ ^[A-Za-z0-9._:-]+$ ]] || candidate_fail "a safe non-empty DEPLOY_LOCK_TOKEN is required."
  [[ "$WORKSPACE_EXPECTED_SHA" =~ ^[0-9a-f]{40}$ ]] || candidate_fail "IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA must be a lowercase full Git SHA."
  [[ "$deploy_git_sha" =~ ^[0-9a-f]{40}$ ]] || candidate_fail "DEPLOY_GIT_SHA must be a lowercase full Git SHA."
  require_candidate_sha256 "$WORKSPACE_PLAN_DIGEST" "IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST"
  require_candidate_sha256 "$WORKSPACE_PROFILE_DIGEST" "IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST"
  require_candidate_sha256 "$EXPECTED_SUPPORT_DIGEST" "EXPECTED_SUPPORT_DIGEST"
  require_candidate_sha256 "$SERVICES_MANIFEST_DIGEST" "DEPLOY_SERVICES_MANIFEST_DIGEST"
  require_candidate_sha256 "$IMAGE_MANIFEST_DIGEST" "DEPLOY_IMAGE_MANIFEST_DIGEST"
  if [ "$release_sha" != "$deploy_git_sha" ] ||
     [ "$deploy_git_sha" != "$WORKSPACE_EXPECTED_SHA" ]; then
    candidate_fail "release tag SHA, DEPLOY_GIT_SHA and workspace expected SHA must be identical."
  fi

  for service in $SELECTED_SERVICE_NAMES; do
    service="$(candidate_service_name_for_deploy_service "$service")"
    if [ -z "$canonical_services" ]; then
      canonical_services="$service"
    else
      canonical_services="$canonical_services,$service"
    fi
  done
  [ "$REQUESTED_SERVICES" = "$canonical_services" ] || candidate_fail "services must be the canonical frozen list: $canonical_services"

  services_temp="$(mktemp "${TMPDIR:-/tmp}/aicopilot-candidate-services.XXXXXX")"
  images_temp="$(mktemp "${TMPDIR:-/tmp}/aicopilot-candidate-images.XXXXXX")"
  printf '%s\n' "$canonical_services" > "$services_temp"
  : > "$images_temp"
  for key in "${SELECTED_IMAGE_KEYS[@]}"; do
    candidate_variable="DEPLOY_CANDIDATE_${key}"
    candidate_value="${!candidate_variable:-}"
    candidate_tag_variable="${candidate_variable}_TAG"
    candidate_tag_value="${!candidate_tag_variable:-}"
    candidate_digest_variable="${candidate_variable}_DIGEST"
    candidate_digest_value="${!candidate_digest_variable:-}"
    if [[ ! "$candidate_value" =~ ^[A-Za-z0-9._:/@-]+@sha256:[0-9a-f]{64}$ ]] ||
       [[ ! "$candidate_tag_value" =~ ^[A-Za-z0-9._:/-]+:"$RELEASE_TAG"$ ]] ||
       [[ ! "$candidate_digest_value" =~ ^sha256:[0-9a-f]{64}$ ]]; then
      rm -f "$services_temp" "$images_temp"
      candidate_fail "$candidate_variable must include a safe immutable repo@sha256 reference, matching tag and OCI digest."
    fi
    candidate_repository="${candidate_value%@*}"
    [ "$candidate_value" = "$candidate_repository@$candidate_digest_value" ] || candidate_fail "$candidate_variable does not match its OCI digest."
    [ "${candidate_tag_value%:*}" = "$candidate_repository" ] || candidate_fail "$candidate_tag_variable repository does not match the immutable candidate."
    printf '%s=%s\n' "$key" "$candidate_value" >> "$images_temp"
    printf '%s_TAG=%s\n' "$key" "$candidate_tag_value" >> "$images_temp"
    printf '%s_DIGEST=%s\n' "$key" "$candidate_digest_value" >> "$images_temp"
    set_frozen_candidate_image "$key" "$candidate_value"
    set_frozen_candidate_image_tag "$key" "$candidate_tag_value"
    set_frozen_candidate_image_digest "$key" "$candidate_digest_value"
  done
  actual_services_digest="$(release_sha256_file "$services_temp")"
  actual_image_digest="$(release_sha256_file "$images_temp")"
  rm -f "$services_temp" "$images_temp"
  [ "$actual_services_digest" = "$SERVICES_MANIFEST_DIGEST" ] || candidate_fail "services manifest digest drifted."
  [ "$actual_image_digest" = "$IMAGE_MANIFEST_DIGEST" ] || candidate_fail "image manifest digest drifted."

  readonly WORKSPACE_ENTRYPOINT WORKSPACE_INVOCATION_ID WORKSPACE_EXPECTED_SHA WORKSPACE_PLAN_DIGEST WORKSPACE_PROFILE_DIGEST
  readonly EXPECTED_SUPPORT_DIGEST SERVICES_MANIFEST_DIGEST IMAGE_MANIFEST_DIGEST CANDIDATE_DEPLOY_GIT_SHA
  readonly DEPLOY_LOCK_TOKEN RELEASE_TAG REQUESTED_SERVICES
}

verify_reserved_candidate_lock() {
  [ -n "$DEPLOY_LOCK_TOKEN" ] || candidate_fail "DEPLOY_LOCK_TOKEN is required for a formal release."
  [ -d "$RELEASE_LOCK_DIR" ] || candidate_fail "the digest-bound support reservation is missing."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" token)" = "$DEPLOY_LOCK_TOKEN" ] || candidate_fail "support reservation token mismatch."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" state)" = "reserved" ] || candidate_fail "support reservation is not in reserved state."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" workspace-entrypoint)" = "$WORKSPACE_ENTRYPOINT" ] || candidate_fail "workspace entrypoint marker drifted after support reservation."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" invocation-id)" = "$WORKSPACE_INVOCATION_ID" ] || candidate_fail "invocation id drifted after support reservation."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" expected-sha)" = "$WORKSPACE_EXPECTED_SHA" ] || candidate_fail "expected SHA drifted after support reservation."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" plan-digest)" = "$WORKSPACE_PLAN_DIGEST" ] || candidate_fail "plan digest drifted after support reservation."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" profile-digest)" = "$WORKSPACE_PROFILE_DIGEST" ] || candidate_fail "profile digest drifted after support reservation."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" support-digest)" = "$EXPECTED_SUPPORT_DIGEST" ] || candidate_fail "support digest drifted after support reservation."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" services-manifest-digest)" = "$SERVICES_MANIFEST_DIGEST" ] || candidate_fail "services manifest digest drifted after support reservation."
  [ "$(release_lock_value "$RELEASE_LOCK_DIR" image-manifest-digest)" = "$IMAGE_MANIFEST_DIGEST" ] || candidate_fail "image manifest digest drifted after support reservation."
}

load_dotenv() {
  if [ ! -f "$ENV_FILE" ]; then
    printf 'Missing deploy environment file: %s\n' "$ENV_FILE" >&2
    exit 66
  fi

  while IFS= read -r env_line || [ -n "$env_line" ]
  do
    env_line="${env_line//$'\r'/}"

    case "$env_line" in
      ''|'#'*)
        continue
        ;;
      *=*)
        export "$env_line"
        ;;
      *)
        printf 'Invalid env line in %s: %s\n' "$ENV_FILE" "$env_line" >&2
        exit 64
        ;;
    esac
  done < "$ENV_FILE"
}

require_env_value() {
  local key="$1"
  local value="${!key:-}"
  if [ -z "$value" ]; then
    printf 'Missing required value in .env: %s\n' "$key" >&2
    exit 64
  fi
}

image_repository_from_ref() {
  local image_ref="$1"
  local last_segment="${image_ref##*/}"

  if [[ "$image_ref" == *@* ]]; then
    printf '%s\n' "${image_ref%@*}"
    return
  fi

  if [[ "$last_segment" == *:* ]]; then
    printf '%s\n' "${image_ref%:*}"
    return
  fi

  printf '%s\n' "$image_ref"
}

replace_env_value() {
  local key="$1"
  local value="$2"
  local tmp_file
  tmp_file="$(mktemp "$DEPLOY_DIR/.env.XXXXXX")"

  awk -v key="$key" -v value="$value" '
    BEGIN { updated = 0 }
    index($0, key "=") == 1 {
      print key "=" value
      updated = 1
      next
    }
    { print }
    END {
      if (!updated) {
        print key "=" value
      }
    }' "$ENV_FILE" > "$tmp_file"

  mv "$tmp_file" "$ENV_FILE"
}

atomic_copy_file() {
  local source_path="$1"
  local destination_path="$2"
  local temp_path

  temp_path="$(mktemp "$(dirname "$destination_path")/.$(basename "$destination_path").XXXXXX")"
  cp -p "$source_path" "$temp_path"
  mv "$temp_path" "$destination_path"
}

begin_state_transaction() {
  local path
  local key

  STATE_TRANSACTION_DIR="$(mktemp -d "$DEPLOY_DIR/.release-transaction.XXXXXX")"
  for path in \
    "$ENV_FILE" \
    "$CURRENT_RELEASE_FILE" \
    "$PREVIOUS_RELEASE_FILE" \
    "$STAGED_RELEASE_FILE" \
    "$CURRENT_RELEASE_SUMMARY_FILE"; do
    key="$(printf '%s' "$path" | tr '/:' '__')"
    if [ -e "$path" ]; then
      cp -p "$path" "$STATE_TRANSACTION_DIR/$key"
      : > "$STATE_TRANSACTION_DIR/$key.exists"
    fi
  done
}

persist_previous_infra_transaction_identity() {
  local identity_file="$STATE_TRANSACTION_DIR/previous-infra-runtime.env"
  [ -n "$STATE_TRANSACTION_DIR" ] && [ -d "$STATE_TRANSACTION_DIR" ] || return 66
  umask 077
  {
    printf 'PREVIOUS_POSTGRES_IMAGE_REF=%s\n' "$PREVIOUS_POSTGRES_IMAGE_REF"
    printf 'PREVIOUS_POSTGRES_RUNTIME_DIGEST=%s\n' "$PREVIOUS_POSTGRES_RUNTIME_DIGEST"
    printf 'PREVIOUS_RABBITMQ_IMAGE_REF=%s\n' "$PREVIOUS_RABBITMQ_IMAGE_REF"
    printf 'PREVIOUS_RABBITMQ_RUNTIME_DIGEST=%s\n' "$PREVIOUS_RABBITMQ_RUNTIME_DIGEST"
    printf 'PREVIOUS_QDRANT_IMAGE_REF=%s\n' "$PREVIOUS_QDRANT_IMAGE_REF"
    printf 'PREVIOUS_QDRANT_RUNTIME_DIGEST=%s\n' "$PREVIOUS_QDRANT_RUNTIME_DIGEST"
  } > "$identity_file"
}

support_backup_dir() {
  printf '%s/.support-backups/%s\n' "$DEPLOY_DIR" "$DEPLOY_LOCK_TOKEN"
}

restore_support_transaction() {
  local backup_dir
  local paths_file
  local relative_path
  local restore_status=0
  backup_dir="$(support_backup_dir)"
  paths_file="$backup_dir/paths"
  [ -d "$backup_dir" ] && [ -f "$paths_file" ] || return 66
  while IFS= read -r relative_path; do
    [ -n "$relative_path" ] || continue
    release_validate_manifest_target "$DEPLOY_DIR" "$relative_path" || { restore_status=1; continue; }
    rm -f "$DEPLOY_DIR/$relative_path" 2>/dev/null || restore_status=1
  done < "$paths_file"
  if [ -d "$backup_dir/tree" ]; then
    while IFS= read -r relative_path; do
      relative_path="${relative_path#./}"
      [ -n "$relative_path" ] || continue
      release_validate_manifest_target "$backup_dir/tree" "$relative_path" || { restore_status=1; continue; }
      release_validate_manifest_target "$DEPLOY_DIR" "$relative_path" || { restore_status=1; continue; }
      mkdir -p "$(dirname "$DEPLOY_DIR/$relative_path")" || { restore_status=1; continue; }
      cp -p "$backup_dir/tree/$relative_path" "$DEPLOY_DIR/$relative_path" || restore_status=1
    done < <(cd "$backup_dir/tree" && find . -type f -print | LC_ALL=C sort)
  fi
  if [ -f "$backup_dir/installed-manifest" ]; then
    cp -p "$backup_dir/installed-manifest" "$SUPPORT_MANIFEST_FILE" || restore_status=1
  else
    rm -f "$SUPPORT_MANIFEST_FILE" || restore_status=1
  fi
  if [ -f "$backup_dir/installed-digest" ]; then
    cp -p "$backup_dir/installed-digest" "$SUPPORT_DIGEST_FILE" || restore_status=1
  else
    rm -f "$SUPPORT_DIGEST_FILE" || restore_status=1
  fi
  if [ "$restore_status" -eq 0 ] && [ -f "$SUPPORT_MANIFEST_FILE" ]; then
    release_verify_sha256_manifest "$DEPLOY_DIR" "$SUPPORT_MANIFEST_FILE" || restore_status=1
  fi
  [ "$restore_status" -eq 0 ]
}

restore_state_transaction() {
  local path
  local key
  local restore_status=0

  [ -n "$STATE_TRANSACTION_DIR" ] && [ -d "$STATE_TRANSACTION_DIR" ] || return 0
  for path in \
    "$ENV_FILE" \
    "$CURRENT_RELEASE_FILE" \
    "$PREVIOUS_RELEASE_FILE" \
    "$STAGED_RELEASE_FILE" \
    "$CURRENT_RELEASE_SUMMARY_FILE"; do
    key="$(printf '%s' "$path" | tr '/:' '__')"
    if [ -f "$STATE_TRANSACTION_DIR/$key.exists" ]; then
      atomic_copy_file "$STATE_TRANSACTION_DIR/$key" "$path" || restore_status=1
    else
      rm -f "$path" || restore_status=1
    fi
  done
  return "$restore_status"
}

restore_previous_runtime() (
  local key
  local expected_image
  local expected_digest
  if [ "${AICOPILOT_NON_PRODUCTION_MECHANISM_TEST:-false}" = true ] &&
     [ "${AICOPILOT_TEST_FORCE_RECOVERY_FAILURE:-false}" = true ]; then
    printf 'NON_PRODUCTION_MECHANISM_TEST forced previous-runtime recovery failure.\n' >&2
    return 59
  fi
  load_dotenv || return $?
  for key in POSTGRES_IMAGE RABBITMQ_IMAGE QDRANT_IMAGE; do
    expected_image="$(previous_infra_ref "$key")"
    expected_digest="$(previous_infra_digest "$key")"
    [[ "$expected_image" =~ ^[A-Za-z0-9._:/-]+@sha256:[0-9a-f]{64}$ ]] || return 66
    [[ "$expected_digest" =~ ^sha256:[0-9a-f]{64}$ ]] || return 66
    export "$key=$expected_image"
  done
  compose config -q || return $?
  compose pull postgres eventbus qdrant aicopilot-httpapi aicopilot-dataworker aicopilot-ragworker aicopilot-webui || return $?
  compose up -d postgres eventbus qdrant || return $?
  wait_for_compose_service_healthy postgres 180 || return $?
  compose up -d aicopilot-httpapi aicopilot-dataworker aicopilot-ragworker aicopilot-webui || return $?
  compose ps || return $?
  for key in POSTGRES_IMAGE RABBITMQ_IMAGE QDRANT_IMAGE; do
    expected_image="$(previous_infra_ref "$key")"
    expected_digest="$(previous_infra_digest "$key")"
    inspect_runtime_identity_against_ref "$key" "$expected_image" "$expected_digest" || return $?
  done
  for key in AICOPILOT_HTTPAPI_IMAGE AICOPILOT_DATAWORKER_IMAGE AICOPILOT_RAGWORKER_IMAGE AICOPILOT_WEBUI_IMAGE; do
    expected_image="${!key:-}"
    [ -n "$expected_image" ] || return 66
    inspect_runtime_digest_against_ref "$key" "$expected_image" >/dev/null || return $?
  done
  probe_web || return $?
  probe_web_security_headers || return $?
  run_release_security_attestation || return $?
)

persist_blocked_release() {
  local original_status="$1"
  local reason="$2"
  local temp_file
  if [ "${AICOPILOT_NON_PRODUCTION_MECHANISM_TEST:-false}" = true ] &&
     [ "${AICOPILOT_TEST_FORCE_BLOCKED_STATE_WRITE_FAILURE:-false}" = true ]; then
    printf 'NON_PRODUCTION_MECHANISM_TEST forced blocked-state persistence failure.\n' >&2
    return 73
  fi
  mkdir -p "$RELEASES_DIR"
  temp_file="$(mktemp "$RELEASES_DIR/.blocked-release.XXXXXX")"
  umask 077
  {
    printf 'DEPLOY_STATUS=blocked-partial\n'
    printf 'DEPLOY_RELEASE_ID=%s\n' "${RELEASE_TAG:-unknown}"
    printf 'DEPLOY_INVOCATION_ID=%s\n' "${WORKSPACE_INVOCATION_ID:-unknown}"
    printf 'DEPLOY_EXPECTED_SHA=%s\n' "${WORKSPACE_EXPECTED_SHA:-unknown}"
    printf 'DEPLOY_PLAN_DIGEST=%s\n' "${WORKSPACE_PLAN_DIGEST:-unknown}"
    printf 'DEPLOY_FAILURE_EXIT_CODE=%s\n' "$original_status"
    printf 'DEPLOY_FAILURE_REASON=%s\n' "$reason"
    printf 'DEPLOY_MIGRATION_EXECUTED=%s\n' "$MIGRATION_EXECUTED"
    printf 'DEPLOY_TRANSACTION_BACKUP=%s\n' "$STATE_TRANSACTION_DIR"
    printf 'DEPLOY_BLOCKED_AT_UTC=%s\n' "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  } > "$temp_file"
  mv "$temp_file" "$BLOCKED_RELEASE_FILE"
}

finish_deploy_release() {
  local status="$1"
  local final_status="$status"
  local terminal_status
  local state_restore_ok=true
  local blocked_reason=""
  trap - EXIT HUP INT TERM
  if [ "$status" -ne 0 ] && [ "$RELEASE_COMMITTED" != true ] &&
     [ "${VALIDATE_ONLY:-false}" != true ] &&
     { [ "$RELEASE_LOCK_HELD" = true ] || [ -n "$STATE_TRANSACTION_DIR" ]; }; then
    if ! restore_support_transaction; then
      blocked_reason=support-restore-failed
    fi
    if ! restore_state_transaction; then
      state_restore_ok=false
      [ -n "$blocked_reason" ] || blocked_reason=state-restore-failed
    fi
    if [ "$CONTAINER_UPDATE_STARTED" = true ]; then
      if [ "$MIGRATION_EXECUTED" = true ]; then
        [ -n "$blocked_reason" ] || blocked_reason=migration-or-runtime-partial
      elif [ "$state_restore_ok" = true ] && restore_previous_runtime; then
        RECOVERY_SUCCEEDED=true
        printf 'AICopilot failed candidate containers were restored to the previous manifest and revalidated.\n' >&2
      else
        [ -n "$blocked_reason" ] || blocked_reason=container-recovery-failed
      fi
    fi
    if [ -n "$blocked_reason" ]; then
      final_status="$UNSAFE_PARTIAL_EXIT"
      KEEP_FAILURE_LOCK=true
      if [ "$RELEASE_LOCK_HELD" = true ]; then
        release_mark_lock_blocked "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" "$blocked_reason" || true
      fi
      if ! persist_blocked_release "$status" "$blocked_reason"; then
        blocked_reason=blocked-state-persistence-failed
        if [ "$RELEASE_LOCK_HELD" = true ]; then
          release_mark_lock_blocked "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" "$blocked_reason" || true
        fi
        printf '%s\n' "$blocked_reason" > "$STATE_TRANSACTION_DIR/BLOCKED_PERSISTENCE_FAILED" 2>/dev/null || true
        printf 'AICopilot blocked-state persistence failed; transaction and owned lock are retained for manual recovery.\n' >&2
      fi
      printf 'AICopilot automatic retry is blocked; transaction backup retained: %s\n' "$STATE_TRANSACTION_DIR" >&2
    fi
  fi
  if [ "$RELEASE_LOCK_HELD" = true ] && [ "$KEEP_FAILURE_LOCK" != true ]; then
    release_unlock "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" || true
  fi
  if [ -n "$STATE_TRANSACTION_DIR" ] && { [ "$status" -eq 0 ] || [ -z "$blocked_reason" ]; }; then
    rm -rf "$STATE_TRANSACTION_DIR" 2>/dev/null || true
  fi
  if [ "$status" -eq 0 ] || [ -z "$blocked_reason" ]; then
    rm -rf "$(support_backup_dir)" 2>/dev/null || true
  fi
  if [ "$final_status" -eq 0 ]; then
    terminal_status=succeeded
  elif [ "$RELEASE_COMMITTED" = true ]; then
    terminal_status=committed-cleanup-failed
  elif [ "$final_status" -eq "$UNSAFE_PARTIAL_EXIT" ]; then
    terminal_status=unsafe-partial
  else
    terminal_status=failed-safe
  fi
  if ! write_invocation_state "$terminal_status" "$final_status"; then
    printf 'AICopilot could not persist terminal invocation state; remote outcome must be reconciled manually: token=%s\n' "$DEPLOY_LOCK_TOKEN" >&2
  fi
  exit "$final_status"
}

verify_support_release() {
  local manifest_digest
  local recorded_digest
  local lock_digest

  if [ ! -f "$SUPPORT_MANIFEST_FILE" ] && [ "${VALIDATE_ONLY:-false}" = true ] && [ "$ENV_FILE" != "$DEPLOY_DIR/.env" ]; then
    ACTIVE_SUPPORT_DIGEST="source-tree-validation"
    printf 'AICopilot source-tree validate-only: installed support digest verification skipped for external ENV_FILE=%s\n' "$ENV_FILE"
    return
  fi

  [ -f "$SUPPORT_MANIFEST_FILE" ] || {
    printf 'AICopilot support manifest is missing: %s\n' "$SUPPORT_MANIFEST_FILE" >&2
    exit 66
  }
  [ -f "$SUPPORT_DIGEST_FILE" ] || {
    printf 'AICopilot support digest is missing: %s\n' "$SUPPORT_DIGEST_FILE" >&2
    exit 66
  }

  manifest_digest="$(release_sha256_file "$SUPPORT_MANIFEST_FILE")"
  recorded_digest="$(sed -n '1p' "$SUPPORT_DIGEST_FILE")"
  if [ "$manifest_digest" != "$recorded_digest" ]; then
    printf 'Installed AICopilot support manifest digest is inconsistent: manifest=%s recorded=%s\n' "$manifest_digest" "$recorded_digest" >&2
    exit 65
  fi
  if [ -n "$EXPECTED_SUPPORT_DIGEST" ] && [ "$manifest_digest" != "$EXPECTED_SUPPORT_DIGEST" ]; then
    printf 'Installed AICopilot support digest does not match requested release: expected=%s actual=%s\n' "$EXPECTED_SUPPORT_DIGEST" "$manifest_digest" >&2
    exit 65
  fi
  if [ "$RELEASE_LOCK_HELD" = true ]; then
    lock_digest="$(release_lock_value "$RELEASE_LOCK_DIR" support-digest)"
    if [ -n "$lock_digest" ] && [ "$lock_digest" != "$manifest_digest" ]; then
      printf 'AICopilot release lock support digest mismatch: lock=%s installed=%s\n' "$lock_digest" "$manifest_digest" >&2
      exit 65
    fi
  fi
  release_verify_sha256_manifest "$DEPLOY_DIR" "$SUPPORT_MANIFEST_FILE"
  ACTIVE_SUPPORT_DIGEST="$manifest_digest"
}

acquire_release_lock() {
  if [ -z "$DEPLOY_LOCK_TOKEN" ]; then
    DEPLOY_LOCK_TOKEN="manual-$(date +%s)-$$"
  fi

  trap 'finish_deploy_release $?' EXIT
  trap 'finish_deploy_release 129' HUP
  trap 'finish_deploy_release 130' INT
  trap 'finish_deploy_release 143' TERM
  if [ "${VALIDATE_ONLY:-false}" = true ]; then
    release_acquire_lock "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" aicopilot-release active preflight 0 || return $?
  elif [ -d "$RELEASE_LOCK_DIR" ] &&
       [ "$(release_lock_value "$RELEASE_LOCK_DIR" token)" = "$DEPLOY_LOCK_TOKEN" ]; then
    release_adopt_reserved_lock "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" aicopilot-release preflight || return $?
  else
    printf 'Formal AICopilot release reservation disappeared before atomic adoption.\n' >&2
    return 75
  fi
  RELEASE_LOCK_HELD=true
}

prepare_release_directories() {
  mkdir -p "$RELEASE_HISTORY_DIR" "$RELEASES_DIR/invocations"
}

write_invocation_state() {
  local status="$1"
  local exit_code="$2"
  local temp_file
  [ "$INVOCATION_STATE_INITIALIZED" = true ] || return 0
  case "$status" in
    active|succeeded|failed-safe|unsafe-partial|committed-cleanup-failed) ;;
    *) return 64 ;;
  esac
  case "$exit_code" in
    ''|*[!0-9]*) return 64 ;;
  esac
  temp_file="$(mktemp "$RELEASES_DIR/invocations/.${DEPLOY_LOCK_TOKEN}.XXXXXX")"
  umask 077
  {
    printf 'DEPLOY_STATUS=%s\n' "$status"
    printf 'DEPLOY_EXIT_CODE=%s\n' "$exit_code"
    printf 'DEPLOY_LOCK_TOKEN=%s\n' "$DEPLOY_LOCK_TOKEN"
    printf 'DEPLOY_INVOCATION_ID=%s\n' "$WORKSPACE_INVOCATION_ID"
    printf 'DEPLOY_EXPECTED_SHA=%s\n' "$WORKSPACE_EXPECTED_SHA"
    printf 'DEPLOY_SERVER_DEADLINE_EPOCH=%s\n' "$SERVER_DEADLINE_EPOCH"
    printf 'DEPLOY_UPDATED_AT_UTC=%s\n' "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  } > "$temp_file"
  mv "$temp_file" "$INVOCATION_STATE_FILE"
}

initialize_invocation_state() {
  INVOCATION_STATE_FILE="$RELEASES_DIR/invocations/$DEPLOY_LOCK_TOKEN.env"
  INVOCATION_STATE_INITIALIZED=true
  write_invocation_state active 0
}

validate_server_deadline() {
  local now_epoch
  release_require_canonical_decimal "$SERVER_DEADLINE_EPOCH" "DEPLOY_SERVER_DEADLINE_EPOCH" 1 99999999999 || return $?
  now_epoch="$(release_now_epoch)"
  release_require_canonical_decimal "$now_epoch" "Current epoch" 1 99999999999 || return $?
  if [ "$SERVER_DEADLINE_EPOCH" -le "$now_epoch" ] || [ "$SERVER_DEADLINE_EPOCH" -gt $((now_epoch + 86400)) ]; then
    printf 'DEPLOY_SERVER_DEADLINE_EPOCH must be in the future and no more than 24 hours away.\n' >&2
    return 64
  fi
}

ensure_server_deadline() {
  local now_epoch
  now_epoch="$(release_now_epoch)"
  release_require_canonical_decimal "$now_epoch" "Current epoch" 1 99999999999 || return $?
  if [ "$now_epoch" -gt "$SERVER_DEADLINE_EPOCH" ]; then
    printf 'AICopilot server deployment deadline expired before the next mutation phase.\n' >&2
    return 124
  fi
}

safe_release_file_name() {
  printf '%s' "$1" | tr -c 'A-Za-z0-9._-' '-'
}

read_manifest_value() {
  local manifest_path="$1"
  local key="$2"
  sed -n "s/^${key}=//p" "$manifest_path" | tail -n 1
}

compute_config_fingerprint() {
  CONFIG_FINGERPRINT="$({
    awk '
      BEGIN { FS="=" }
      /^[[:space:]]*($|#)/ { next }
      index($0, "=") == 0 { next }
      {
        key=$1
        sub(/\r$/, "", $0)
        if (key ~ /^AICOPILOT_(HTTPAPI|MIGRATION|DATAWORKER|RAGWORKER|WEBUI)_IMAGE$/) next
        if (key ~ /^(DEPLOY_|IIOT_WORKSPACE_DEPLOY_)/) next
        effective[key]=$0
      }
      END { for (key in effective) print effective[key] }
    ' "$ENV_FILE" | LC_ALL=C sort
  } | release_sha256_stream)"
  [[ "$CONFIG_FINGERPRINT" =~ ^[0-9a-f]{64}$ ]] || {
    printf 'Could not compute the canonical deploy configuration fingerprint.\n' >&2
    exit 65
  }
}

selected_services_are_complete() {
  local required
  local count=0
  for required in aicopilot-httpapi aicopilot-migration aicopilot-dataworker aicopilot-ragworker aicopilot-webui; do
    case " $SELECTED_SERVICE_NAMES " in
      *" $required "*) count=$((count + 1)) ;;
      *) return 1 ;;
    esac
  done
  [ "$count" -eq 5 ]
}

deny_partial_config_fingerprint_drift() {
  local current_fingerprint
  [ -f "$CURRENT_RELEASE_FILE" ] || return 0
  current_fingerprint="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_CONFIG_FINGERPRINT)"
  [ -n "$current_fingerprint" ] || return 0
  [ "$current_fingerprint" = "$CONFIG_FINGERPRINT" ] && return 0
  if ! selected_services_are_complete; then
    printf 'AICopilot global deploy configuration fingerprint changed; partial-service publication is denied. Select all services so every runtime and dependency is revalidated atomically.\n' >&2
    return 67
  fi
}

runtime_digest_manifest_key() {
  printf 'DEPLOY_RUNTIME_%s_DIGEST\n' "$1"
}

set_runtime_digest() {
  local key="$1"
  local value="$2"
  case "$key" in
    AICOPILOT_HTTPAPI_IMAGE) RUNTIME_AICOPILOT_HTTPAPI_IMAGE_DIGEST="$value" ;;
    AICOPILOT_DATAWORKER_IMAGE) RUNTIME_AICOPILOT_DATAWORKER_IMAGE_DIGEST="$value" ;;
    AICOPILOT_RAGWORKER_IMAGE) RUNTIME_AICOPILOT_RAGWORKER_IMAGE_DIGEST="$value" ;;
    AICOPILOT_WEBUI_IMAGE) RUNTIME_AICOPILOT_WEBUI_IMAGE_DIGEST="$value" ;;
    AICOPILOT_MIGRATION_IMAGE) ;;
    *) candidate_fail "unsupported runtime image key $key" ;;
  esac
}

runtime_digest() {
  case "$1" in
    AICOPILOT_HTTPAPI_IMAGE) printf '%s\n' "$RUNTIME_AICOPILOT_HTTPAPI_IMAGE_DIGEST" ;;
    AICOPILOT_DATAWORKER_IMAGE) printf '%s\n' "$RUNTIME_AICOPILOT_DATAWORKER_IMAGE_DIGEST" ;;
    AICOPILOT_RAGWORKER_IMAGE) printf '%s\n' "$RUNTIME_AICOPILOT_RAGWORKER_IMAGE_DIGEST" ;;
    AICOPILOT_WEBUI_IMAGE) printf '%s\n' "$RUNTIME_AICOPILOT_WEBUI_IMAGE_DIGEST" ;;
    AICOPILOT_MIGRATION_IMAGE) printf '%s\n' '' ;;
    *) candidate_fail "unsupported runtime image key $1" ;;
  esac
}

compose_service_for_image_key() {
  case "$1" in
    POSTGRES_IMAGE) printf '%s\n' postgres ;;
    RABBITMQ_IMAGE) printf '%s\n' eventbus ;;
    QDRANT_IMAGE) printf '%s\n' qdrant ;;
    AICOPILOT_HTTPAPI_IMAGE) printf '%s\n' aicopilot-httpapi ;;
    AICOPILOT_DATAWORKER_IMAGE) printf '%s\n' aicopilot-dataworker ;;
    AICOPILOT_RAGWORKER_IMAGE) printf '%s\n' aicopilot-ragworker ;;
    AICOPILOT_WEBUI_IMAGE) printf '%s\n' aicopilot-webui ;;
    *) candidate_fail "no runtime container mapping for $1" ;;
  esac
}

set_previous_infra_identity() {
  local key="$1"
  local immutable_ref="$2"
  local runtime_digest="$3"
  case "$key" in
    POSTGRES_IMAGE) PREVIOUS_POSTGRES_IMAGE_REF="$immutable_ref"; PREVIOUS_POSTGRES_RUNTIME_DIGEST="$runtime_digest" ;;
    RABBITMQ_IMAGE) PREVIOUS_RABBITMQ_IMAGE_REF="$immutable_ref"; PREVIOUS_RABBITMQ_RUNTIME_DIGEST="$runtime_digest" ;;
    QDRANT_IMAGE) PREVIOUS_QDRANT_IMAGE_REF="$immutable_ref"; PREVIOUS_QDRANT_RUNTIME_DIGEST="$runtime_digest" ;;
    *) candidate_fail "unsupported previous infrastructure image key $key" ;;
  esac
}

previous_infra_ref() {
  case "$1" in
    POSTGRES_IMAGE) printf '%s\n' "$PREVIOUS_POSTGRES_IMAGE_REF" ;;
    RABBITMQ_IMAGE) printf '%s\n' "$PREVIOUS_RABBITMQ_IMAGE_REF" ;;
    QDRANT_IMAGE) printf '%s\n' "$PREVIOUS_QDRANT_IMAGE_REF" ;;
    *) return 64 ;;
  esac
}

previous_infra_digest() {
  case "$1" in
    POSTGRES_IMAGE) printf '%s\n' "$PREVIOUS_POSTGRES_RUNTIME_DIGEST" ;;
    RABBITMQ_IMAGE) printf '%s\n' "$PREVIOUS_RABBITMQ_RUNTIME_DIGEST" ;;
    QDRANT_IMAGE) printf '%s\n' "$PREVIOUS_QDRANT_RUNTIME_DIGEST" ;;
    *) return 64 ;;
  esac
}

set_runtime_infra_identity() {
  local key="$1"
  local immutable_ref="$2"
  local runtime_digest="$3"
  case "$key" in
    POSTGRES_IMAGE) RUNTIME_POSTGRES_IMAGE_REF="$immutable_ref"; RUNTIME_POSTGRES_IMAGE_DIGEST="$runtime_digest" ;;
    RABBITMQ_IMAGE) RUNTIME_RABBITMQ_IMAGE_REF="$immutable_ref"; RUNTIME_RABBITMQ_IMAGE_DIGEST="$runtime_digest" ;;
    QDRANT_IMAGE) RUNTIME_QDRANT_IMAGE_REF="$immutable_ref"; RUNTIME_QDRANT_IMAGE_DIGEST="$runtime_digest" ;;
    *) candidate_fail "unsupported runtime infrastructure image key $key" ;;
  esac
}

inspect_infra_identity() {
  local key="$1"
  local service
  local container_id
  local configured_ref
  local immutable_ref
  local runtime_digest
  local configured_repository
  local repo_digests
  local candidate_ref
  local candidate_repository

  service="$(compose_service_for_image_key "$key")"
  container_id="$(compose ps -q "$service" | tail -n 1)"
  [ -n "$container_id" ] || return 66
  configured_ref="$(docker inspect --format '{{.Config.Image}}' "$container_id" | tail -n 1 | tr -d '\r')"
  runtime_digest="$(docker inspect --format '{{.Image}}' "$container_id" | tail -n 1 | tr -d '\r')"
  [[ "$configured_ref" =~ ^[A-Za-z0-9._:/@-]+$ ]] || return 65
  [[ "$runtime_digest" =~ ^sha256:[0-9a-f]{64}$ ]] || return 65
  configured_repository="$(image_repository_from_ref "$configured_ref")"
  repo_digests="$(docker image inspect --format '{{range .RepoDigests}}{{println .}}{{end}}' "$runtime_digest")" || return 65
  immutable_ref=""
  while IFS= read -r candidate_ref; do
    candidate_ref="${candidate_ref//$'\r'/}"
    [ -n "$candidate_ref" ] || continue
    [[ "$candidate_ref" =~ ^[A-Za-z0-9._:/-]+@sha256:[0-9a-f]{64}$ ]] || return 65
    candidate_repository="${candidate_ref%@*}"
    [ "$candidate_repository" = "$configured_repository" ] || continue
    if [ -n "$immutable_ref" ] && [ "$immutable_ref" != "$candidate_ref" ]; then
      printf 'Infrastructure image has multiple RepoDigests for the configured repository: %s\n' "$configured_repository" >&2
      return 65
    fi
    immutable_ref="$candidate_ref"
  done <<< "$repo_digests"
  [ -n "$immutable_ref" ] || return 65
  if [[ "$configured_ref" == *@sha256:* ]] && [ "$configured_ref" != "$immutable_ref" ]; then
    printf 'Configured immutable infrastructure image does not match its image RepoDigest: configured=%s actual=%s\n' \
      "$configured_ref" "$immutable_ref" >&2
    return 65
  fi
  printf '%s|%s\n' "$immutable_ref" "$runtime_digest"
}

freeze_previous_infra_identity() {
  local key
  local identity
  local immutable_ref
  local runtime_digest

  PREVIOUS_POSTGRES_IMAGE_REF=""
  PREVIOUS_POSTGRES_RUNTIME_DIGEST=""
  PREVIOUS_RABBITMQ_IMAGE_REF=""
  PREVIOUS_RABBITMQ_RUNTIME_DIGEST=""
  PREVIOUS_QDRANT_IMAGE_REF=""
  PREVIOUS_QDRANT_RUNTIME_DIGEST=""

  for key in POSTGRES_IMAGE RABBITMQ_IMAGE QDRANT_IMAGE; do
    if ! identity="$(inspect_infra_identity "$key")"; then
      printf 'Could not freeze previous infrastructure runtime identity for %s before the release transaction.\n' "$key" >&2
      return 65
    fi
    IFS='|' read -r immutable_ref runtime_digest <<< "$identity"
    set_previous_infra_identity "$key" "$immutable_ref" "$runtime_digest"
    set_runtime_infra_identity "$key" "$immutable_ref" "$runtime_digest"
  done
}

collect_runtime_infra_identity() {
  local key
  local identity
  local immutable_ref
  local runtime_digest

  for key in POSTGRES_IMAGE RABBITMQ_IMAGE QDRANT_IMAGE; do
    identity="$(inspect_infra_identity "$key")" || return $?
    IFS='|' read -r immutable_ref runtime_digest <<< "$identity"
    set_runtime_infra_identity "$key" "$immutable_ref" "$runtime_digest"
  done
}

inspect_runtime_identity_against_ref() {
  local key="$1"
  local expected_ref="$2"
  local expected_digest="$3"
  local actual_digest

  actual_digest="$(inspect_runtime_digest_against_ref "$key" "$expected_ref")" || return $?
  [ "$actual_digest" = "$expected_digest" ] || {
    printf 'Runtime identity mismatch for %s: expected=%s actual=%s\n' "$key" "$expected_digest" "$actual_digest" >&2
    return 65
  }
}

inspect_runtime_digest_against_ref() {
  local key="$1"
  local expected_image="$2"
  local service
  local container_id
  local configured_image
  local actual_digest
  local state_before
  local state_after
  local running
  local restarting
  local oom_killed
  local restart_count
  local health_status
  local running_after
  local restarting_after
  local oom_killed_after
  local restart_count_after
  local health_status_after
  local stability_seconds="${AICOPILOT_RUNTIME_STABILITY_SECONDS:-5}"
  service="$(compose_service_for_image_key "$key")"
  container_id="$(compose ps -q "$service" | tail -n 1)"
  [ -n "$container_id" ] || return 1
  configured_image="$(docker inspect --format '{{.Config.Image}}' "$container_id" | tail -n 1 | tr -d '\r')"
  actual_digest="$(docker inspect --format '{{.Image}}' "$container_id" | tail -n 1 | tr -d '\r')"
  [ "$configured_image" = "$expected_image" ] || return 1
  [[ "$actual_digest" =~ ^sha256:[0-9a-f]{64}$ ]] || return 1
  release_require_canonical_decimal "$stability_seconds" "Runtime stability window" 0 60 || return $?
  state_before="$(docker inspect --format '{{.State.Running}}|{{.State.Restarting}}|{{.State.OOMKilled}}|{{.RestartCount}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container_id" | tail -n 1 | tr -d '\r')"
  IFS='|' read -r running restarting oom_killed restart_count health_status <<< "$state_before"
  [ "$running" = true ] && [ "$restarting" = false ] && [ "$oom_killed" = false ] || return 1
  release_require_canonical_decimal "$restart_count" "Container restart count" 0 999999 || return $?
  [ "$health_status" = none ] || [ "$health_status" = healthy ] || return 1
  [ "$stability_seconds" -eq 0 ] || sleep "$stability_seconds"
  state_after="$(docker inspect --format '{{.State.Running}}|{{.State.Restarting}}|{{.State.OOMKilled}}|{{.RestartCount}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container_id" | tail -n 1 | tr -d '\r')"
  IFS='|' read -r running_after restarting_after oom_killed_after restart_count_after health_status_after <<< "$state_after"
  [ "$running_after" = true ] && [ "$restarting_after" = false ] && [ "$oom_killed_after" = false ] || return 1
  [ "$restart_count_after" = "$restart_count" ] || return 1
  [ "$health_status_after" = none ] || [ "$health_status_after" = healthy ] || return 1
  printf '%s\n' "$actual_digest"
}

inspect_selected_runtime_digest() {
  local key="$1"
  inspect_runtime_digest_against_ref "$key" "$(frozen_candidate_image "$key")"
}

load_runtime_facts_from_manifest() {
  local manifest_path="$1"
  local key
  local manifest_key
  local value
  for key in AICOPILOT_HTTPAPI_IMAGE AICOPILOT_DATAWORKER_IMAGE AICOPILOT_RAGWORKER_IMAGE AICOPILOT_WEBUI_IMAGE; do
    manifest_key="$(runtime_digest_manifest_key "$key")"
    value="$(read_manifest_value "$manifest_path" "$manifest_key")"
    set_runtime_digest "$key" "$value"
  done
  MIGRATION_COMPLETED="$(read_manifest_value "$manifest_path" DEPLOY_MIGRATION_COMPLETED)"
  [ -n "$MIGRATION_COMPLETED" ] || MIGRATION_COMPLETED=false
}

collect_selected_runtime_facts() {
  local key
  local digest
  for key in "${SELECTED_IMAGE_KEYS[@]}"; do
    if [ "$key" = AICOPILOT_MIGRATION_IMAGE ]; then
      MIGRATION_COMPLETED=true
      continue
    fi
    if ! digest="$(inspect_selected_runtime_digest "$key")"; then
      printf 'Runtime OCI digest attestation failed for %s.\n' "$key" >&2
      return 65
    fi
    set_runtime_digest "$key" "$digest"
  done
}

load_release_images_from_manifest() {
  local manifest_path="$1"
  local key
  local value

  for key in "${APP_IMAGE_KEYS[@]}"
  do
    value="$(read_manifest_value "$manifest_path" "$key")"
    if [ -z "$value" ]; then
      printf 'Release manifest is missing %s: %s\n' "$key" "$manifest_path" >&2
      exit 66
    fi

    export "$key=$value"
  done
}

apply_app_image_values_to_env() {
  local key

  for key in "${APP_IMAGE_KEYS[@]}"
  do
    replace_env_value "$key" "${!key:-}"
  done
}

apply_candidate_image_values() {
  local key
  local candidate_value

  for key in "${SELECTED_IMAGE_KEYS[@]}"; do
    candidate_value="$(frozen_candidate_image "$key")"
    [ -n "$candidate_value" ] || candidate_fail "missing frozen candidate image for $key"
    export "$key=$candidate_value"
    replace_env_value "$key" "$candidate_value"
  done
}

write_release_manifest() {
  local output_path="$1"
  local release_id="$2"
  local deploy_git_sha="$3"
  local deploy_triggered_by="$4"
  local deployed_at_utc="$5"
  local deployed_services="$6"
  local support_digest="$7"
  local key

  umask 077
  {
    printf 'DEPLOY_RELEASE_ID=%s\n' "$release_id"
    printf 'DEPLOY_GIT_SHA=%s\n' "$deploy_git_sha"
    printf 'DEPLOY_TRIGGERED_BY=%s\n' "$deploy_triggered_by"
    printf 'DEPLOYED_AT_UTC=%s\n' "$deployed_at_utc"
    printf 'DEPLOY_SERVICES=%s\n' "$deployed_services"
    printf 'DEPLOY_CANDIDATE=true\n'
    printf 'DEPLOY_INVOCATION_ID=%s\n' "$WORKSPACE_INVOCATION_ID"
    printf 'DEPLOY_EXPECTED_SHA=%s\n' "$WORKSPACE_EXPECTED_SHA"
    printf 'DEPLOY_PLAN_DIGEST=%s\n' "$WORKSPACE_PLAN_DIGEST"
    printf 'DEPLOY_PROFILE_DIGEST=%s\n' "$WORKSPACE_PROFILE_DIGEST"
    printf 'DEPLOY_SERVICES_MANIFEST_DIGEST=%s\n' "$SERVICES_MANIFEST_DIGEST"
    printf 'DEPLOY_IMAGE_MANIFEST_DIGEST=%s\n' "$IMAGE_MANIFEST_DIGEST"
    printf 'DEPLOY_CONFIG_FINGERPRINT=%s\n' "$CONFIG_FINGERPRINT"
    printf 'DEPLOY_SUPPORT_DIGEST=%s\n' "$support_digest"
    printf 'DEPLOY_MIGRATION_COMPLETED=%s\n' "$MIGRATION_COMPLETED"
    printf 'DEPLOY_RUNTIME_AICOPILOT_HTTPAPI_IMAGE_DIGEST=%s\n' "$RUNTIME_AICOPILOT_HTTPAPI_IMAGE_DIGEST"
    printf 'DEPLOY_RUNTIME_AICOPILOT_DATAWORKER_IMAGE_DIGEST=%s\n' "$RUNTIME_AICOPILOT_DATAWORKER_IMAGE_DIGEST"
    printf 'DEPLOY_RUNTIME_AICOPILOT_RAGWORKER_IMAGE_DIGEST=%s\n' "$RUNTIME_AICOPILOT_RAGWORKER_IMAGE_DIGEST"
    printf 'DEPLOY_RUNTIME_AICOPILOT_WEBUI_IMAGE_DIGEST=%s\n' "$RUNTIME_AICOPILOT_WEBUI_IMAGE_DIGEST"
    printf 'DEPLOY_INFRA_POSTGRES_IMAGE_REF=%s\n' "$RUNTIME_POSTGRES_IMAGE_REF"
    printf 'DEPLOY_INFRA_POSTGRES_RUNTIME_DIGEST=%s\n' "$RUNTIME_POSTGRES_IMAGE_DIGEST"
    printf 'DEPLOY_INFRA_RABBITMQ_IMAGE_REF=%s\n' "$RUNTIME_RABBITMQ_IMAGE_REF"
    printf 'DEPLOY_INFRA_RABBITMQ_RUNTIME_DIGEST=%s\n' "$RUNTIME_RABBITMQ_IMAGE_DIGEST"
    printf 'DEPLOY_INFRA_QDRANT_IMAGE_REF=%s\n' "$RUNTIME_QDRANT_IMAGE_REF"
    printf 'DEPLOY_INFRA_QDRANT_RUNTIME_DIGEST=%s\n' "$RUNTIME_QDRANT_IMAGE_DIGEST"

    for key in "${APP_IMAGE_KEYS[@]}"
    do
      printf '%s=%s\n' "$key" "${!key:-}"
    done
  } > "$output_path"
}

record_release_history() {
  local source_file="$1"
  local release_id="$2"
  local history_timestamp
  local safe_release_id
  local history_file

  history_timestamp="$(date -u +"%Y%m%dT%H%M%SZ")"
  safe_release_id="$(safe_release_file_name "$release_id")"
  history_file="$RELEASE_HISTORY_DIR/$history_timestamp-$safe_release_id-$(safe_release_file_name "$DEPLOY_LOCK_TOKEN").env"
  cp "$source_file" "$history_file"
  printf '%s\n' "$history_file"
}

write_release_summary() {
  local output_path="$1"
  local release_id="$2"
  local deploy_git_sha="$3"
  local deploy_triggered_by="$4"
  local deployed_at_utc="$5"
  local deployed_services="$6"
  local release_notes="${7:-}"
  local support_digest="${8:-}"

  umask 077
  {
    printf '### AICopilot deploy\n\n'
    printf -- '- Release tag: `%s`\n' "$release_id"
    printf -- '- Git SHA: `%s`\n' "$deploy_git_sha"
    printf -- '- Triggered by: `%s`\n' "$deploy_triggered_by"
    printf -- '- Deployed at UTC: `%s`\n' "$deployed_at_utc"
    printf -- '- Services: `%s`\n' "${deployed_services:-all}"
    printf -- '- Frozen candidate: `true`\n'
    printf -- '- Invocation id: `%s`\n' "$WORKSPACE_INVOCATION_ID"
    printf -- '- Expected SHA: `%s`\n' "$WORKSPACE_EXPECTED_SHA"
    printf -- '- Plan digest: `%s`\n' "$WORKSPACE_PLAN_DIGEST"
    printf -- '- Profile digest: `%s`\n' "$WORKSPACE_PROFILE_DIGEST"
    printf -- '- Services manifest digest: `%s`\n' "$SERVICES_MANIFEST_DIGEST"
    printf -- '- Image manifest digest: `%s`\n' "$IMAGE_MANIFEST_DIGEST"
    printf -- '- Config fingerprint (values never recorded): `%s`\n' "$CONFIG_FINGERPRINT"
    printf -- '- Support digest: `%s`\n' "$support_digest"
    printf -- '- Migration completed for candidate: `%s`\n' "$MIGRATION_COMPLETED"
    printf -- '- Runtime HttpApi image id: `%s`\n' "$RUNTIME_AICOPILOT_HTTPAPI_IMAGE_DIGEST"
    printf -- '- Runtime DataWorker image id: `%s`\n' "$RUNTIME_AICOPILOT_DATAWORKER_IMAGE_DIGEST"
    printf -- '- Runtime RagWorker image id: `%s`\n' "$RUNTIME_AICOPILOT_RAGWORKER_IMAGE_DIGEST"
    printf -- '- Runtime Web image id: `%s`\n' "$RUNTIME_AICOPILOT_WEBUI_IMAGE_DIGEST"
    printf -- '- Runtime PostgreSQL immutable image: `%s` (`%s`)\n' "$RUNTIME_POSTGRES_IMAGE_REF" "$RUNTIME_POSTGRES_IMAGE_DIGEST"
    printf -- '- Runtime RabbitMQ immutable image: `%s` (`%s`)\n' "$RUNTIME_RABBITMQ_IMAGE_REF" "$RUNTIME_RABBITMQ_IMAGE_DIGEST"
    printf -- '- Runtime Qdrant immutable image: `%s` (`%s`)\n' "$RUNTIME_QDRANT_IMAGE_REF" "$RUNTIME_QDRANT_IMAGE_DIGEST"
    printf '\n#### Changes\n'

    if [ -n "$release_notes" ]; then
      printf '%s\n' "$release_notes" | sed '/^[[:space:]]*$/d; s/^/- /'
    else
      printf -- '- No git summary available.\n'
    fi
  } > "$output_path"
}

release_request_is_current() {
  local current_release_id
  local current_support_digest
  local current_expected_sha
  local current_plan_digest
  local current_profile_digest
  local current_services_digest
  local current_image_digest
  local current_config_digest
  local key
  local image_ref
  local candidate_value
  local recorded_runtime_digest
  local actual_runtime_digest
  local recorded_infra_ref
  local recorded_infra_digest
  local actual_infra_identity

  [ -f "$CURRENT_RELEASE_FILE" ] || return 1
  current_release_id="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_RELEASE_ID)"
  current_support_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_SUPPORT_DIGEST)"
  current_expected_sha="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_EXPECTED_SHA)"
  current_plan_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_PLAN_DIGEST)"
  current_profile_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_PROFILE_DIGEST)"
  current_services_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_SERVICES_MANIFEST_DIGEST)"
  current_image_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_IMAGE_MANIFEST_DIGEST)"
  current_config_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_CONFIG_FINGERPRINT)"
  [ "$current_release_id" = "$RELEASE_TAG" ] || return 1
  [ "$current_support_digest" = "$ACTIVE_SUPPORT_DIGEST" ] || return 1
  [ "$current_expected_sha" = "$WORKSPACE_EXPECTED_SHA" ] || return 1
  [ "$current_plan_digest" = "$WORKSPACE_PLAN_DIGEST" ] || return 1
  [ "$current_profile_digest" = "$WORKSPACE_PROFILE_DIGEST" ] || return 1
  [ "$current_services_digest" = "$SERVICES_MANIFEST_DIGEST" ] || return 1
  [ "$current_image_digest" = "$IMAGE_MANIFEST_DIGEST" ] || return 1
  [ "$current_config_digest" = "$CONFIG_FINGERPRINT" ] || return 1

  for key in POSTGRES_IMAGE RABBITMQ_IMAGE QDRANT_IMAGE; do
    case "$key" in
      POSTGRES_IMAGE)
        recorded_infra_ref="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_INFRA_POSTGRES_IMAGE_REF)"
        recorded_infra_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_INFRA_POSTGRES_RUNTIME_DIGEST)"
        ;;
      RABBITMQ_IMAGE)
        recorded_infra_ref="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_INFRA_RABBITMQ_IMAGE_REF)"
        recorded_infra_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_INFRA_RABBITMQ_RUNTIME_DIGEST)"
        ;;
      QDRANT_IMAGE)
        recorded_infra_ref="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_INFRA_QDRANT_IMAGE_REF)"
        recorded_infra_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_INFRA_QDRANT_RUNTIME_DIGEST)"
        ;;
    esac
    actual_infra_identity="$(inspect_infra_identity "$key")" || return 1
    [ "$actual_infra_identity" = "$recorded_infra_ref|$recorded_infra_digest" ] || return 1
  done

  for key in "${SELECTED_IMAGE_KEYS[@]}"; do
    image_ref="$(read_manifest_value "$CURRENT_RELEASE_FILE" "$key")"
    candidate_value="$(frozen_candidate_image "$key")"
    [ -n "$candidate_value" ] || return 1
    [ "$image_ref" = "$candidate_value" ] || return 1
    if [ "$key" = AICOPILOT_MIGRATION_IMAGE ]; then
      [ "$(read_manifest_value "$CURRENT_RELEASE_FILE" DEPLOY_MIGRATION_COMPLETED)" = true ] || return 1
      continue
    fi
    recorded_runtime_digest="$(read_manifest_value "$CURRENT_RELEASE_FILE" "$(runtime_digest_manifest_key "$key")")"
    [ -n "$recorded_runtime_digest" ] || return 1
    actual_runtime_digest="$(inspect_selected_runtime_digest "$key")" || return 1
    [ "$actual_runtime_digest" = "$recorded_runtime_digest" ] || return 1
  done
  return 0
}

ensure_explicit_registry_image() {
  local key="$1"
  local image_ref="${!key:-}"
  local image_registry="${image_ref%%/*}"

  case "$image_ref" in
    docker.io/*|registry-1.docker.io/*|postgres:*|rabbitmq:*|qdrant/*|node:*|nginx:*)
      printf 'Image must be mirrored to Harbor, not pulled from Docker Hub: %s=%s\n' "$key" "$image_ref" >&2
      exit 64
      ;;
  esac

  if [ "$image_registry" = "$image_ref" ]; then
    printf 'Image must include an explicit Harbor registry: %s=%s\n' "$key" "$image_ref" >&2
    exit 64
  fi

  case "$image_registry" in
    *.*|*:*|localhost)
      ;;
    *)
      printf 'Image must include an explicit Harbor registry: %s=%s\n' "$key" "$image_ref" >&2
      exit 64
      ;;
  esac
}

apply_release_tag_to_app_images() {
  local release_tag="$1"
  local key
  local image_ref
  local image_repository

  for key in "${APP_IMAGE_KEYS[@]}"
  do
    require_env_value "$key"
    image_ref="${!key}"
    image_repository="$(image_repository_from_ref "$image_ref")"
    replace_env_value "$key" "$image_repository:$release_tag"
  done
}

apply_release_tag_to_image_keys() {
  local release_tag="$1"
  shift
  local key
  local image_ref
  local image_repository

  for key in "$@"
  do
    require_env_value "$key"
    image_ref="${!key}"
    image_repository="$(image_repository_from_ref "$image_ref")"
    replace_env_value "$key" "$image_repository:$release_tag"
  done
}

ensure_image_policy() {
  local key

  for key in "${APP_IMAGE_KEYS[@]}" "${INFRA_IMAGE_KEYS[@]}"
  do
    require_env_value "$key"
    ensure_explicit_registry_image "$key"
  done
}

is_truthy() {
  case "${1:-}" in
    true|TRUE|True|1|yes|YES|Yes)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

read_file_mode() {
  stat -c '%a' "$1" 2>/dev/null || stat -f '%Lp' "$1" 2>/dev/null || printf ''
}

ensure_env_file_permissions() {
  local mode
  mode="$(read_file_mode "$ENV_FILE")"
  if [ -z "$mode" ]; then
    printf 'Unable to read deploy environment file mode: %s\n' "$ENV_FILE" >&2
    exit 66
  fi

  case "$mode" in
    400|600)
      ;;
    *)
      printf 'Deploy environment file must be owner-only readable/writable (400 or 600): %s mode=%s\n' "$ENV_FILE" "$mode" >&2
      exit 64
      ;;
  esac
}

is_template_placeholder_value() {
  local key="$1"
  local value="$2"
  local lower
  lower="$(printf '%s' "$value" | tr '[:upper:]' '[:lower:]')"

  if [ "$key" = "AICOPILOT_MODEL_SMOKE_API_KEY" ] &&
     [ "$value" = "dummy-key" ] &&
     is_truthy "${AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY:-false}"; then
    return 1
  fi

  case "$lower" in
    *change_me*|*change-me*|*replace-with*|*internal.example*|*'<tag>'*|*'<git-sha>'*|*'<'*'>'*|dummy-key|123456|password)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

ensure_no_template_placeholders() {
  local env_line
  local key
  local value

  while IFS= read -r env_line || [ -n "$env_line" ]
  do
    env_line="${env_line//$'\r'/}"

    case "$env_line" in
      ''|'#'*)
        continue
        ;;
      *=*)
        key="${env_line%%=*}"
        value="${env_line#*=}"
        if is_template_placeholder_value "$key" "$value"; then
          printf 'Deploy .env still contains template or weak placeholder value: %s\n' "$key" >&2
          exit 64
        fi
        ;;
    esac
  done < "$ENV_FILE"
}

require_secret_value() {
  local key="$1"
  local min_length="$2"
  local value="${!key:-}"

  if [ -z "$value" ]; then
    printf 'Missing required secret in .env: %s\n' "$key" >&2
    exit 64
  fi

  if is_template_placeholder_value "$key" "$value"; then
    printf 'Secret in .env uses a weak or template value: %s\n' "$key" >&2
    exit 64
  fi

  if [ "${#value}" -lt "$min_length" ]; then
    printf 'Secret in .env is too short: %s requires at least %s characters.\n' "$key" "$min_length" >&2
    exit 64
  fi
}

require_http_url_value() {
  local key="$1"
  local value="${!key:-}"

  if [ -z "$value" ]; then
    printf 'Missing required HTTP URL in .env: %s\n' "$key" >&2
    exit 64
  fi

  case "$value" in
    http://*)
      ;;
    https://*)
      printf 'AICopilot current deployment is HTTP-only; %s must not use HTTPS until a certificate plan is approved.\n' "$key" >&2
      exit 64
      ;;
    *)
      printf 'AICopilot deployment URL must start with http:// for current HTTP-only mode: %s=%s\n' "$key" "$value" >&2
      exit 64
      ;;
  esac
}

extract_http_url_host() {
  local url="$1"
  local without_scheme="${url#http://}"
  local authority="${without_scheme%%/*}"
  local host

  case "$authority" in
    \[*\]*)
      host="${authority#\[}"
      host="${host%%\]*}"
      ;;
    *)
      host="${authority%%:*}"
      ;;
  esac

  printf '%s\n' "$host" | tr '[:upper:]' '[:lower:]' | sed 's/\.$//'
}

is_allowed_intranet_http_oidc_host() {
  local url="$1"
  local host
  host="$(extract_http_url_host "$url")"

  case "$host" in
    localhost|127.*|::1|*.internal.example|*.internal|*.lan|*.local)
      return 0
      ;;
    10.*|192.168.*)
      return 0
      ;;
  esac

  if [[ "$host" =~ ^172\.(1[6-9]|2[0-9]|3[0-1])\. ]]; then
    return 0
  fi

  return 1
}

require_intranet_http_oidc_issuer() {
  require_http_url_value CLOUD_OIDC_ISSUER

  if ! is_allowed_intranet_http_oidc_host "$CLOUD_OIDC_ISSUER"; then
    printf 'HTTP-only Cloud OIDC issuer must be loopback, private IPv4, or a reserved intranet DNS suffix (.internal.example, .internal, .lan, .local): %s\n' "$CLOUD_OIDC_ISSUER" >&2
    exit 64
  fi
}

ensure_http_only_environment() {
  require_http_url_value AICOPILOT_PUBLIC_URL
  require_http_url_value CLOUD_PLATFORM_URL

  if [ -n "${CLOUD_AI_READ_BASE_URL:-}" ]; then
    require_http_url_value CLOUD_AI_READ_BASE_URL
  fi

  if [ -n "${CLOUD_IDENTITY_STATUS_BASE_URL:-}" ]; then
    require_http_url_value CLOUD_IDENTITY_STATUS_BASE_URL
  fi

  if [ -n "${AICOPILOT_MODEL_SMOKE_BASE_URL:-}" ]; then
    require_http_url_value AICOPILOT_MODEL_SMOKE_BASE_URL
  fi

  if is_truthy "${CLOUD_OIDC_ENABLED:-false}"; then
    require_intranet_http_oidc_issuer
    if ! is_truthy "${ALLOW_INTRANET_HTTP_OIDC:-false}"; then
      printf 'HTTP-only Cloud OIDC requires ALLOW_INTRANET_HTTP_OIDC=true.\n' >&2
      exit 64
    fi
    if [ "${CLOUD_OIDC_REQUIRE_HTTPS_METADATA:-true}" != "false" ]; then
      printf 'HTTP-only Cloud OIDC requires CLOUD_OIDC_REQUIRE_HTTPS_METADATA=false.\n' >&2
      exit 64
    fi
  fi
}

ensure_required_secrets() {
  require_secret_value POSTGRES_PASSWORD 16
  require_secret_value RABBITMQ_PASSWORD 16
  require_secret_value QDRANT_KEY 16
  require_secret_value AICOPILOT_BOOTSTRAP_ADMIN_PASSWORD 12
  require_secret_value AICOPILOT_API_KEY_ENCRYPTION_KEY 32
  require_secret_value AICOPILOT_JWT_SECRET_KEY 64

  if is_truthy "${CLOUD_AI_READ_ENABLED:-false}" || is_truthy "${CLOUD_IDENTITY_STATUS_ENABLED:-false}"; then
    require_secret_value CLOUD_AI_SERVICE_ACCOUNT_TOKEN 32
  fi

  if is_truthy "${DATA_ANALYSIS_CLOUD_READONLY_ENABLED:-false}"; then
    require_secret_value DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING 32
    if ! is_truthy "${DATA_ANALYSIS_CLOUD_READONLY_CREDENTIAL_VERIFIED:-false}"; then
      printf 'DATA_ANALYSIS_CLOUD_READONLY_ENABLED=true requires DATA_ANALYSIS_CLOUD_READONLY_CREDENTIAL_VERIFIED=true.\n' >&2
      exit 64
    fi
  fi
}

validate_deploy_environment() {
  validate_deploy_environment_readonly
  check_release_state_preflight
}

validate_deploy_environment_readonly() {
  ensure_env_file_permissions
  ensure_no_template_placeholders
  ensure_http_only_environment
  ensure_required_secrets
}

check_release_state_preflight() {
  local check_script="$DEPLOY_DIR/scripts/check-release-state-access.sh"

  if [ ! -x "$check_script" ]; then
    printf 'AICopilot release state preflight script is missing or not executable: %s\n' "$check_script" >&2
    exit 66
  fi

  "$check_script"
}

ensure_cloud_readonly_network() {
  local network="${DATA_ANALYSIS_CLOUD_READONLY_DOCKER_NETWORK:-enterprise-ai-cloud-readonly}"
  local cloud_project="${DATA_ANALYSIS_CLOUD_READONLY_CLOUD_COMPOSE_PROJECT:-deploy}"
  local cloud_service="${DATA_ANALYSIS_CLOUD_READONLY_CLOUD_POSTGRES_SERVICE:-postgres}"
  local host_alias="${DATA_ANALYSIS_CLOUD_READONLY_DB_HOST_ALIAS:-cloud-postgres}"
  local cloud_container

  if ! docker network inspect "$network" >/dev/null 2>&1; then
    docker network create --driver bridge "$network" >/dev/null
    printf 'Created Cloud readonly Docker network: %s\n' "$network"
  fi

  if ! is_truthy "${DATA_ANALYSIS_CLOUD_READONLY_ENABLED:-false}"; then
    return
  fi

  cloud_container="$(
    docker ps \
      --filter "label=com.docker.compose.project=$cloud_project" \
      --filter "label=com.docker.compose.service=$cloud_service" \
      --format '{{.ID}}' |
      head -n 1
  )"

  if [ -z "$cloud_container" ]; then
    printf 'Direct Cloud readonly DB is enabled, but Cloud PostgreSQL container was not found: project=%s service=%s\n' "$cloud_project" "$cloud_service" >&2
    exit 66
  fi

  if docker inspect "$cloud_container" \
    --format '{{range $name, $_ := .NetworkSettings.Networks}}{{println $name}}{{end}}' |
    grep -Fx "$network" >/dev/null; then
    printf 'Cloud PostgreSQL container is already attached to network %s.\n' "$network"
    return
  fi

  docker network connect --alias "$host_alias" "$network" "$cloud_container"
  printf 'Attached Cloud PostgreSQL container to network %s as %s.\n' "$network" "$host_alias"
}

check_cloud_readonly_preflight() {
  local check_script="$DEPLOY_DIR/scripts/check-cloud-readonly-grants.sh"

  if ! is_truthy "${DATA_ANALYSIS_CLOUD_READONLY_ENABLED:-false}"; then
    printf 'CloudReadOnly direct DB is disabled; skipping readonly grant preflight.\n'
    return
  fi

  if [ ! -x "$check_script" ]; then
    printf 'CloudReadOnly direct DB is enabled, but preflight script is missing or not executable: %s\n' "$check_script" >&2
    exit 66
  fi

  printf 'Running CloudReadOnly readonly grant preflight.\n'
  "$check_script" --env-file "$ENV_FILE"
}

check_model_provider_preflight() {
  local check_script="$DEPLOY_DIR/scripts/check-model-provider-openai.sh"

  if ! is_truthy "${AICOPILOT_MODEL_SMOKE_ENABLED:-false}"; then
    printf 'Model provider smoke check is disabled; skipping model preflight.\n'
    return
  fi

  if [ ! -x "$check_script" ]; then
    printf 'Model provider smoke check is enabled, but preflight script is missing or not executable: %s\n' "$check_script" >&2
    exit 66
  fi

  printf 'Running model provider smoke preflight.\n'
  "$check_script" --env-file "$ENV_FILE"
}

check_model_secret_migration_preflight() {
  local check_script="$DEPLOY_DIR/scripts/check-model-secret-migration.sh"
  local status

  if [ ! -x "$check_script" ]; then
    printf 'Model secret migration check script is missing or not executable: %s\n' "$check_script" >&2
    exit 66
  fi

  printf 'Running model secret migration preflight.\n'
  set +e
  "$check_script" --env-file "$ENV_FILE" --compose-file "$COMPOSE_FILE"
  status=$?
  set -e

  if [ "$status" -ne 0 ]; then
    printf 'AICopilot model secret migration preflight failed before starting runtime services.\n' >&2
    printf 'Run ./deploy-release.sh %s --services migration, or ask an administrator to re-enter affected API keys.\n' "$DEPLOY_RELEASE_ID" >&2
    exit "$status"
  fi
}

wait_for_compose_service_healthy() {
  local service="$1"
  local timeout_seconds="${2:-180}"
  local elapsed_seconds=0
  local container_id
  local status

  printf 'Waiting for %s to become healthy.\n' "$service"
  while [ "$elapsed_seconds" -lt "$timeout_seconds" ]; do
    container_id="$(compose ps -q "$service" 2>/dev/null | head -n 1 || true)"
    if [ -n "$container_id" ]; then
      status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_id" 2>/dev/null || true)"
      case "$status" in
        healthy)
          printf '%s is healthy.\n' "$service"
          return 0
          ;;
        running)
          printf '%s is running.\n' "$service"
          return 0
          ;;
      esac
    fi

    sleep 2
    elapsed_seconds=$((elapsed_seconds + 2))
  done

  printf 'Timed out waiting for %s to become healthy after %s seconds.\n' "$service" "$timeout_seconds" >&2
  exit 65
}

run_release_security_attestation() {
  local check_script="$DEPLOY_DIR/scripts/check-release-security-attestation.sh"
  local web_port="${AICOPILOT_WEB_PORT:-82}"
  local web_url="http://127.0.0.1:${web_port}/"

  if [ ! -x "$check_script" ]; then
    printf 'Release security attestation script is missing or not executable: %s\n' "$check_script" >&2
    exit 66
  fi

  printf 'Running release security attestation.\n'
  "$check_script" \
    --env-file "$ENV_FILE" \
    --compose-file "$COMPOSE_FILE" \
    --web-url "$web_url"
}

probe_web() {
  local web_port="${AICOPILOT_WEB_PORT:-82}"
  local url="http://127.0.0.1:${web_port}/"
  local attempt=1
  local max_attempts=18
  local status_code

  while [ "$attempt" -le "$max_attempts" ]
  do
    status_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 10 "$url" || true)"
    if [ "$status_code" = "200" ]; then
      printf 'AICopilot web probe succeeded: %s -> %s\n' "$url" "$status_code"
      return 0
    fi

    printf 'AICopilot web probe attempt %s/%s failed: %s -> %s\n' "$attempt" "$max_attempts" "$url" "${status_code:-curl-error}" >&2
    sleep 5
    attempt=$((attempt + 1))
  done

  printf 'AICopilot web probe failed after %s attempts: %s\n' "$max_attempts" "$url" >&2
  exit 1
}

require_response_header() {
  local headers_file="$1"
  local header_pattern="$2"
  local header_description="$3"

  if ! grep -Eiq "$header_pattern" "$headers_file"; then
    printf 'AICopilot web security header missing or invalid: %s\n' "$header_description" >&2
    cat "$headers_file" >&2
    exit 1
  fi
}

probe_web_security_headers() {
  local web_port="${AICOPILOT_WEB_PORT:-82}"
  local url="http://127.0.0.1:${web_port}/"
  local headers_file

  headers_file="$(mktemp "$DEPLOY_DIR/web-headers.XXXXXX")"
  if ! curl --silent --show-error --fail --head --max-time 10 "$url" | tr -d '\r' > "$headers_file"; then
    printf 'AICopilot web security header probe failed: %s\n' "$url" >&2
    rm -f "$headers_file"
    exit 1
  fi

  require_response_header "$headers_file" '^X-Content-Type-Options:[[:space:]]*nosniff[[:space:]]*$' 'X-Content-Type-Options: nosniff'
  require_response_header "$headers_file" '^X-Frame-Options:[[:space:]]*DENY[[:space:]]*$' 'X-Frame-Options: DENY'
  require_response_header "$headers_file" '^Referrer-Policy:[[:space:]]*no-referrer[[:space:]]*$' 'Referrer-Policy: no-referrer'
  require_response_header "$headers_file" '^Permissions-Policy:[[:space:]].*camera=\(\)' 'Permissions-Policy'
  require_response_header "$headers_file" "^Content-Security-Policy:[[:space:]].*default-src 'self'" 'Content-Security-Policy'

  if grep -Eiq '^Strict-Transport-Security:' "$headers_file"; then
    printf 'AICopilot HTTP-only deployment must not emit Strict-Transport-Security until HTTPS is explicitly approved.\n' >&2
    cat "$headers_file" >&2
    rm -f "$headers_file"
    exit 1
  fi

  rm -f "$headers_file"
  printf 'AICopilot web HTTP-only security header probe succeeded: %s\n' "$url"
}

RELEASE_TAG="${RELEASE_TAG:-}"
REQUESTED_SERVICES="${DEPLOY_SERVICES:-}"
VALIDATE_ONLY=false
CHECK_CURRENT_ONLY=false

while [ "$#" -gt 0 ]
do
  case "$1" in
    --validate-only)
      VALIDATE_ONLY=true
      ;;
    --check-current)
      CHECK_CURRENT_ONLY=true
      ;;
    --services)
      shift
      REQUESTED_SERVICES="${1:-}"
      ;;
    --services=*)
      REQUESTED_SERVICES="${1#--services=}"
      ;;
    -*)
      printf 'Unknown deploy-release option: %s\n' "$1" >&2
      exit 64
      ;;
    *)
      if [ -n "$RELEASE_TAG" ]; then
        if [ "$RELEASE_TAG" != "$1" ]; then
          printf 'Unexpected deploy-release argument: %s\n' "$1" >&2
          exit 64
        fi
      else
        RELEASE_TAG="$1"
      fi
      ;;
  esac
  shift
done

if [ "$VALIDATE_ONLY" = true ] && [ "$CHECK_CURRENT_ONLY" = true ]; then
  printf 'Use either --validate-only or --check-current, not both.\n' >&2
  exit 64
fi

if [ "$VALIDATE_ONLY" = true ]; then
  cd "$DEPLOY_DIR"
  DEPLOY_LOCK_TOKEN="validate-$(date +%s)-$$"
  acquire_release_lock
  verify_support_release
  load_dotenv
  validate_deploy_environment
  printf 'AICopilot deploy environment validation passed: env=%s supportDigest=%s\n' "$ENV_FILE" "$ACTIVE_SUPPORT_DIGEST"
  exit 0
fi

ensure_release_tag "$RELEASE_TAG"

normalize_services() {
  local services_input="${1:-}"
  local normalized_services=""
  local service
  local normalized

  if [ -z "$services_input" ]; then
    printf '%s\n' "aicopilot-httpapi aicopilot-migration aicopilot-dataworker aicopilot-ragworker aicopilot-webui"
    return
  fi

  for service in $(printf '%s' "$services_input" | tr ',' ' ')
  do
    case "$service" in
      httpapi|aicopilot-httpapi)
        normalized=aicopilot-httpapi
        ;;
      migration|aicopilot-migration)
        normalized=aicopilot-migration
        ;;
      dataworker|aicopilot-dataworker)
        normalized=aicopilot-dataworker
        ;;
      ragworker|aicopilot-ragworker)
        normalized=aicopilot-ragworker
        ;;
      web|webui|aicopilot-webui)
        normalized=aicopilot-webui
        ;;
      *)
        printf 'Unsupported deploy service: %s\n' "$service" >&2
        exit 64
        ;;
    esac

    case " $normalized_services " in
      *" $normalized "*)
        ;;
      *)
        normalized_services="$normalized_services $normalized"
        ;;
    esac
  done

  printf '%s\n' "$(printf '%s' "$normalized_services" | awk '{$1=$1; print}')"
}

image_key_for_service() {
  case "$1" in
    aicopilot-httpapi)
      printf '%s\n' AICOPILOT_HTTPAPI_IMAGE
      ;;
    aicopilot-migration)
      printf '%s\n' AICOPILOT_MIGRATION_IMAGE
      ;;
    aicopilot-dataworker)
      printf '%s\n' AICOPILOT_DATAWORKER_IMAGE
      ;;
    aicopilot-ragworker)
      printf '%s\n' AICOPILOT_RAGWORKER_IMAGE
      ;;
    aicopilot-webui)
      printf '%s\n' AICOPILOT_WEBUI_IMAGE
      ;;
    *)
      printf 'Unsupported deploy service: %s\n' "$1" >&2
      exit 64
      ;;
  esac
}

SELECTED_SERVICE_NAMES="$(normalize_services "$REQUESTED_SERVICES")"
SELECTED_IMAGE_KEYS=()
RUNTIME_SELECTED_SERVICES=()
RUN_MIGRATION=false
BACKEND_RUNTIME_SELECTED=false
HTTPAPI_SELECTED=false
WEBUI_SELECTED=false
for service in $SELECTED_SERVICE_NAMES
do
  SELECTED_IMAGE_KEYS+=("$(image_key_for_service "$service")")
  if [ "$service" = "aicopilot-migration" ]; then
    RUN_MIGRATION=true
  else
    RUNTIME_SELECTED_SERVICES+=("$service")
  fi

  if [ "$service" = "aicopilot-httpapi" ]; then
    BACKEND_RUNTIME_SELECTED=true
    HTTPAPI_SELECTED=true
  elif [ "$service" = "aicopilot-dataworker" ] || [ "$service" = "aicopilot-ragworker" ]; then
    BACKEND_RUNTIME_SELECTED=true
  elif [ "$service" = "aicopilot-webui" ]; then
    WEBUI_SELECTED=true
  fi
done

if [ "$CHECK_CURRENT_ONLY" != true ] &&
   [ -n "$REQUESTED_SERVICES" ] &&
   [ "$BACKEND_RUNTIME_SELECTED" = "true" ] &&
   [ "$RUN_MIGRATION" != "true" ]; then
  printf 'AICopilot backend runtime deploys must include migration so model and embedding secrets are revalidated with the current encryption key before runtime starts.\n' >&2
  printf 'Use --services migration,httpapi,dataworker,ragworker as applicable. Web-only deploys may omit migration.\n' >&2
  exit 64
fi

# All candidate identity checks happen before lock adoption, release-state writes,
# .env changes, image pulls, compose updates, or any other container mutation.
verify_workspace_candidate_basics
unsafe_support_marker="$(release_find_unsafe_partial_marker "$DEPLOY_DIR")"
if [ -n "$unsafe_support_marker" ]; then
  printf 'AICopilot deployment is blocked by unresolved unsafe support state: %s\n' "$unsafe_support_marker" >&2
  exit "$UNSAFE_PARTIAL_EXIT"
fi
if [ "$CHECK_CURRENT_ONLY" != true ]; then
  validate_server_deadline || exit $?
  verify_reserved_candidate_lock
  if [ -f "$BLOCKED_RELEASE_FILE" ]; then
    printf 'AICopilot automatic deployment is blocked by unresolved partial state: %s\n' "$BLOCKED_RELEASE_FILE" >&2
    exit 78
  fi
  orphan_transaction="$(find "$DEPLOY_DIR" -maxdepth 1 -type d -name '.release-transaction.*' -print -quit 2>/dev/null || true)"
  if [ -n "$orphan_transaction" ]; then
    printf 'AICopilot automatic deployment is blocked by an unresolved transaction backup: %s\n' "$orphan_transaction" >&2
    exit 79
  fi
fi

command -v docker >/dev/null
command -v curl >/dev/null
docker compose version >/dev/null

cd "$DEPLOY_DIR"
verify_support_release
load_dotenv
validate_deploy_environment_readonly
ensure_image_policy
compute_config_fingerprint
deny_partial_config_fingerprint_drift || exit $?

if [ "$CHECK_CURRENT_ONLY" = true ]; then
  if [ -f "$BLOCKED_RELEASE_FILE" ]; then
    printf 'AICopilot read-only current check found unresolved partial state: %s\n' "$BLOCKED_RELEASE_FILE" >&2
    exit 78
  fi
  orphan_transaction="$(find "$DEPLOY_DIR" -maxdepth 1 -type d -name '.release-transaction.*' -print -quit 2>/dev/null || true)"
  if [ -n "$orphan_transaction" ]; then
    printf 'AICopilot read-only current check found an unresolved transaction backup: %s\n' "$orphan_transaction" >&2
    exit 79
  fi
  if release_request_is_current; then
    compose config -q
    compose ps
    probe_web
    probe_web_security_headers
    run_release_security_attestation
    printf 'AICopilot release is already current and healthy; read-only check made no changes: release=%s supportDigest=%s\n' \
      "$RELEASE_TAG" "$ACTIVE_SUPPORT_DIGEST"
    exit 0
  fi
  printf 'AICopilot release is not current for the exact plan/services/images/support/config/runtime candidate: release=%s supportDigest=%s\n' \
    "$RELEASE_TAG" "$ACTIVE_SUPPORT_DIGEST" >&2
  exit 3
fi

acquire_release_lock
prepare_release_directories
initialize_invocation_state
ensure_server_deadline
validate_deploy_environment
ensure_cloud_readonly_network
check_cloud_readonly_preflight
check_model_provider_preflight

if release_request_is_current; then
  release_update_lock_phase "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" idempotency-check
  if [ "${AICOPILOT_ALLOW_REDEPLOY_SAME_SHA:-false}" != "true" ]; then
    compose config -q
    compose ps
    probe_web
    probe_web_security_headers
    run_release_security_attestation
    printf 'AICopilot release is already current and healthy; no deployment was repeated: release=%s supportDigest=%s\n' \
      "$RELEASE_TAG" "$ACTIVE_SUPPORT_DIGEST"
    exit 0
  fi
fi

if ! freeze_previous_infra_identity; then
  if [ -f "$CURRENT_RELEASE_FILE" ]; then
    printf 'AICopilot cannot start a release transaction without freezing all current infrastructure runtime identities.\n' >&2
    exit 65
  fi
  printf 'No previous AICopilot infrastructure runtime identity exists; any post-container failure will be blocked for manual recovery.\n' >&2
fi
begin_state_transaction
persist_previous_infra_transaction_identity
ensure_server_deadline
release_update_lock_phase "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" preparing-state
if [ -f "$CURRENT_RELEASE_FILE" ]; then
  load_runtime_facts_from_manifest "$CURRENT_RELEASE_FILE"
fi
if [ -z "$REQUESTED_SERVICES" ]; then
  apply_candidate_image_values
else
  if [ -f "$CURRENT_RELEASE_FILE" ]; then
    load_release_images_from_manifest "$CURRENT_RELEASE_FILE"
    apply_app_image_values_to_env
  else
    printf 'No current release manifest found; using .env as the initial image baseline for selected-service deploy: %s\n' "$CURRENT_RELEASE_FILE" >&2
  fi

  apply_candidate_image_values
fi
load_dotenv
validate_deploy_environment
ensure_image_policy
ensure_cloud_readonly_network
check_cloud_readonly_preflight
check_model_provider_preflight
DEPLOY_RELEASE_ID="$RELEASE_TAG"
DEPLOY_GIT_SHA_VALUE="$CANDIDATE_DEPLOY_GIT_SHA"
DEPLOY_TRIGGERED_BY_VALUE="${DEPLOY_TRIGGERED_BY:-manual}"
DEPLOYED_AT_UTC_VALUE="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
DEPLOY_RELEASE_NOTES_VALUE="${DEPLOY_RELEASE_NOTES:-}"
write_release_manifest \
  "$STAGED_RELEASE_FILE" \
  "$DEPLOY_RELEASE_ID" \
  "$DEPLOY_GIT_SHA_VALUE" \
  "$DEPLOY_TRIGGERED_BY_VALUE" \
  "$DEPLOYED_AT_UTC_VALUE" \
  "$SELECTED_SERVICE_NAMES" \
  "$ACTIVE_SUPPORT_DIGEST"
release_update_lock_phase "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" container-update
ensure_server_deadline
compose config -q
if [ -z "$REQUESTED_SERVICES" ]; then
  compose pull
  CONTAINER_UPDATE_STARTED=true
  compose up -d --remove-orphans postgres eventbus qdrant
  wait_for_compose_service_healthy postgres 180
  MIGRATION_EXECUTED=true
  compose up --no-deps --abort-on-container-exit --exit-code-from aicopilot-migration aicopilot-migration
  check_model_secret_migration_preflight
  compose up -d aicopilot-httpapi aicopilot-dataworker aicopilot-ragworker aicopilot-webui
else
  compose pull $SELECTED_SERVICE_NAMES
  CONTAINER_UPDATE_STARTED=true
  compose up -d postgres eventbus qdrant
  wait_for_compose_service_healthy postgres 180
  if [ "$RUN_MIGRATION" = "true" ]; then
    MIGRATION_EXECUTED=true
    compose up --no-deps --abort-on-container-exit --exit-code-from aicopilot-migration aicopilot-migration
    check_model_secret_migration_preflight
  elif [ "$BACKEND_RUNTIME_SELECTED" = "true" ]; then
    check_model_secret_migration_preflight
  fi
  if [ "${#RUNTIME_SELECTED_SERVICES[@]}" -gt 0 ]; then
    if [ "$BACKEND_RUNTIME_SELECTED" = "true" ]; then
      compose up -d "${RUNTIME_SELECTED_SERVICES[@]}"
    else
      compose up -d --no-deps "${RUNTIME_SELECTED_SERVICES[@]}"
    fi
  fi
  if [ "$HTTPAPI_SELECTED" = "true" ] && [ "$WEBUI_SELECTED" != "true" ]; then
    printf 'Recreating aicopilot-webui to refresh nginx upstream after httpapi redeploy.\n'
    compose up -d --no-deps --force-recreate aicopilot-webui
  fi
fi
compose ps
if [ "${AICOPILOT_NON_PRODUCTION_MECHANISM_TEST:-false}" = true ] &&
   [ -n "${AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE:-}" ]; then
  case "$AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE" in
    *[!0-9]*|'') printf 'Invalid non-production fault injection exit code.\n' >&2; exit 64 ;;
  esac
  printf 'NON_PRODUCTION_MECHANISM_TEST forced failure after container update.\n' >&2
  exit "$AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE"
fi
probe_web
probe_web_security_headers
attestation_log="$(mktemp "$DEPLOY_DIR/release-security-attestation.XXXXXX")"
if run_release_security_attestation > "$attestation_log" 2>&1; then
  cat "$attestation_log"
else
  attestation_status=$?
  cat "$attestation_log" >&2
  rm -f "$attestation_log"
  exit "$attestation_status"
fi

collect_selected_runtime_facts
collect_runtime_infra_identity || {
  printf 'Runtime infrastructure OCI identity attestation failed before release-state commit.\n' >&2
  exit 65
}
write_release_manifest \
  "$STAGED_RELEASE_FILE" \
  "$DEPLOY_RELEASE_ID" \
  "$DEPLOY_GIT_SHA_VALUE" \
  "$DEPLOY_TRIGGERED_BY_VALUE" \
  "$DEPLOYED_AT_UTC_VALUE" \
  "$SELECTED_SERVICE_NAMES" \
  "$ACTIVE_SUPPORT_DIGEST"

pending_summary_file="$STATE_TRANSACTION_DIR/current-release.summary.md"
write_release_summary \
  "$pending_summary_file" \
  "$DEPLOY_RELEASE_ID" \
  "$DEPLOY_GIT_SHA_VALUE" \
  "$DEPLOY_TRIGGERED_BY_VALUE" \
  "$DEPLOYED_AT_UTC_VALUE" \
  "$SELECTED_SERVICE_NAMES" \
  "$DEPLOY_RELEASE_NOTES_VALUE" \
  "$ACTIVE_SUPPORT_DIGEST"

{
  printf '\n'
  printf '#### Release Security Attestation\n\n'
  cat "$attestation_log"
} >> "$pending_summary_file"
rm -f "$attestation_log"

ensure_server_deadline
release_update_lock_phase "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" committing-release-state
if [ -f "$CURRENT_RELEASE_FILE" ]; then
  atomic_copy_file "$CURRENT_RELEASE_FILE" "$PREVIOUS_RELEASE_FILE"
fi
atomic_copy_file "$STAGED_RELEASE_FILE" "$CURRENT_RELEASE_FILE"
atomic_copy_file "$pending_summary_file" "$CURRENT_RELEASE_SUMMARY_FILE"
RELEASE_COMMITTED=true
history_file="$(record_release_history "$CURRENT_RELEASE_FILE" "$DEPLOY_RELEASE_ID")"

ensure_server_deadline
release_update_lock_phase "$RELEASE_LOCK_DIR" "$DEPLOY_LOCK_TOKEN" post-release-cleanup
cleanup_log="$(mktemp "$DEPLOY_DIR/post-release-cleanup.XXXXXX")"
set +e
ENV_FILE="$ENV_FILE" DEPLOY_DIR="$DEPLOY_DIR" "$SCRIPT_DIR/post-release-cleanup.sh" --release-tag "$DEPLOY_RELEASE_ID" 2>&1 | tee "$cleanup_log"
cleanup_status=${PIPESTATUS[0]}
set -e
{
  printf '\n'
  cat "$cleanup_log"
} >> "$CURRENT_RELEASE_SUMMARY_FILE"
rm -f "$cleanup_log"

history_summary_file="${history_file%.env}.summary.md"
atomic_copy_file "$CURRENT_RELEASE_SUMMARY_FILE" "$history_summary_file"

if [ "$cleanup_status" -ne 0 ]; then
  printf 'AICopilot deployed, but post-release cleanup failed: %s\n' "$RELEASE_TAG" >&2
  printf 'Current release summary: %s\n' "$CURRENT_RELEASE_SUMMARY_FILE" >&2
  exit "$cleanup_status"
fi

printf 'AICopilot deploy completed for release tag: %s\n' "$RELEASE_TAG"
printf 'Current release manifest: %s\n' "$CURRENT_RELEASE_FILE"
printf 'Current release summary: %s\n' "$CURRENT_RELEASE_SUMMARY_FILE"
printf 'Release history record: %s\n' "$history_file"
printf 'Release history summary: %s\n' "$history_summary_file"
