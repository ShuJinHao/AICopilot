#!/usr/bin/env bash
set -Eeuo pipefail

RUNNER_PROTOCOL_VERSION=1
TARGET=""
EXPECTED_USER=""
MODE=""

usage() {
  cat <<'EOF'
Usage:
  iiot-release-runner.sh --target cloud|aicopilot --expected-user USER --doctor
  iiot-release-runner.sh --target cloud|aicopilot --expected-user USER --request-stdin

The runner is installed once on the server. Routine releases send one small,
digest-bound request over stdin; application releases never replace this file.
EOF
}

fail() {
  local status="${2:-64}"
  printf 'runner_error=%s\n' "$1" >&2
  exit "$status"
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --target)
      shift
      TARGET="${1:-}"
      ;;
    --expected-user)
      shift
      EXPECTED_USER="${1:-}"
      ;;
    --doctor)
      MODE=doctor
      ;;
    --request-stdin)
      MODE=request
      ;;
    --version)
      printf '%s\n' "$RUNNER_PROTOCOL_VERSION"
      exit 0
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "unknown option: $1"
      ;;
  esac
  shift
done

[ -n "$MODE" ] || fail "choose --doctor or --request-stdin"
[[ "$EXPECTED_USER" =~ ^[A-Za-z_][A-Za-z0-9_-]*$ ]] || fail "invalid expected user"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$DEPLOY_DIR/.env"
RELEASES_DIR="$DEPLOY_DIR/releases"
HISTORY_DIR="$RELEASES_DIR/routine-history"
INCOMING_DIR="$RELEASES_DIR/routine-incoming"
CURRENT_IMAGES_FILE="$RELEASES_DIR/current-images.env"
CURRENT_STATE_FILE="$RELEASES_DIR/routine-current.env"
LOCK_DIR="$DEPLOY_DIR/.locks"
LOCK_FILE="$LOCK_DIR/routine-release.lock"
COMPOSE_FILE=""
TARGET_NAME=""
MIGRATION_SERVICE=""
LEGACY_LOCKS=()
ALL_SERVICES=()

case "$TARGET" in
  cloud)
    TARGET_NAME=Cloud
    COMPOSE_FILE="$DEPLOY_DIR/docker-compose.prod.yml"
    MIGRATION_SERVICE=iiot-migration
    ALL_SERVICES=(httpapi gateway dataworker migration web)
    LEGACY_LOCKS=(/data/iiot-platform/.locks/cloud-release.lock.d "$DEPLOY_DIR/.cloud-release.lock.d")
    ;;
  aicopilot)
    TARGET_NAME=AICopilot
    COMPOSE_FILE="$DEPLOY_DIR/docker-compose.yaml"
    MIGRATION_SERVICE=aicopilot-migration
    ALL_SERVICES=(httpapi migration dataworker ragworker web)
    LEGACY_LOCKS=("$DEPLOY_DIR/.locks/release.lock.d")
    ;;
  *)
    fail "target must be cloud or aicopilot"
    ;;
esac

image_key_for_service() {
  case "$TARGET:$1" in
    cloud:httpapi) printf '%s\n' IIOT_HTTPAPI_IMAGE ;;
    cloud:gateway) printf '%s\n' IIOT_GATEWAY_IMAGE ;;
    cloud:dataworker) printf '%s\n' IIOT_DATAWORKER_IMAGE ;;
    cloud:migration) printf '%s\n' IIOT_MIGRATION_IMAGE ;;
    cloud:web) printf '%s\n' IIOT_WEB_IMAGE ;;
    aicopilot:httpapi) printf '%s\n' AICOPILOT_HTTPAPI_IMAGE ;;
    aicopilot:migration) printf '%s\n' AICOPILOT_MIGRATION_IMAGE ;;
    aicopilot:dataworker) printf '%s\n' AICOPILOT_DATAWORKER_IMAGE ;;
    aicopilot:ragworker) printf '%s\n' AICOPILOT_RAGWORKER_IMAGE ;;
    aicopilot:web) printf '%s\n' AICOPILOT_WEBUI_IMAGE ;;
    *) return 1 ;;
  esac
}

compose_name_for_service() {
  case "$TARGET:$1" in
    cloud:httpapi) printf '%s\n' iiot-httpapi ;;
    cloud:gateway) printf '%s\n' iiot-gateway ;;
    cloud:dataworker) printf '%s\n' iiot-dataworker ;;
    cloud:migration) printf '%s\n' iiot-migration ;;
    cloud:web) printf '%s\n' iiot-web ;;
    aicopilot:httpapi) printf '%s\n' aicopilot-httpapi ;;
    aicopilot:migration) printf '%s\n' aicopilot-migration ;;
    aicopilot:dataworker) printf '%s\n' aicopilot-dataworker ;;
    aicopilot:ragworker) printf '%s\n' aicopilot-ragworker ;;
    aicopilot:web) printf '%s\n' aicopilot-webui ;;
    *) return 1 ;;
  esac
}

service_is_runtime() {
  [ "$1" != migration ]
}

request_image_for_key() {
  case "$1" in
    IIOT_HTTPAPI_IMAGE) printf '%s\n' "${IIOT_HTTPAPI_IMAGE:-}" ;;
    IIOT_GATEWAY_IMAGE) printf '%s\n' "${IIOT_GATEWAY_IMAGE:-}" ;;
    IIOT_DATAWORKER_IMAGE) printf '%s\n' "${IIOT_DATAWORKER_IMAGE:-}" ;;
    IIOT_MIGRATION_IMAGE) printf '%s\n' "${IIOT_MIGRATION_IMAGE:-}" ;;
    IIOT_WEB_IMAGE) printf '%s\n' "${IIOT_WEB_IMAGE:-}" ;;
    AICOPILOT_HTTPAPI_IMAGE) printf '%s\n' "${AICOPILOT_HTTPAPI_IMAGE:-}" ;;
    AICOPILOT_MIGRATION_IMAGE) printf '%s\n' "${AICOPILOT_MIGRATION_IMAGE:-}" ;;
    AICOPILOT_DATAWORKER_IMAGE) printf '%s\n' "${AICOPILOT_DATAWORKER_IMAGE:-}" ;;
    AICOPILOT_RAGWORKER_IMAGE) printf '%s\n' "${AICOPILOT_RAGWORKER_IMAGE:-}" ;;
    AICOPILOT_WEBUI_IMAGE) printf '%s\n' "${AICOPILOT_WEBUI_IMAGE:-}" ;;
    *) return 1 ;;
  esac
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command is missing: $1" 69
}

path_mode() {
  if stat -c '%a' "$1" >/dev/null 2>&1; then
    stat -c '%a' "$1"
  else
    stat -f '%Lp' "$1"
  fi
}

read_env_value() {
  local file="$1"
  local key="$2"
  local line value
  [ -f "$file" ] || return 1
  line="$(awk -v wanted="$key" 'index($0, wanted "=") == 1 { print; exit }' "$file")"
  [ -n "$line" ] || return 1
  value="${line#*=}"
  if [[ "$value" == \"*\" && "$value" == *\" ]]; then
    value="${value:1:${#value}-2}"
  elif [[ "$value" == \'*\' && "$value" == *\' ]]; then
    value="${value:1:${#value}-2}"
  fi
  printf '%s\n' "$value"
}

migration_committed_matches_started() {
  local started="$1"
  local committed="$2"
  local key
  [ -f "$started" ] && [ -f "$committed" ] || return 1
  [ "$(read_env_value "$started" STATUS || true)" = migration-started ] || return 1
  [ "$(read_env_value "$committed" STATUS || true)" = migration-committed ] || return 1
  for key in RUNNER_PROTOCOL TARGET INVOCATION_ID GIT_SHA REQUEST_DIGEST \
    DATABASE_BACKUP DATABASE_BACKUP_SHA256 RESUME_ALLOWED; do
    [ "$(read_env_value "$started" "$key" || true)" = \
      "$(read_env_value "$committed" "$key" || true)" ] || return 1
  done
}

migration_is_unresolved() {
  local started committed
  [ "$TARGET" = aicopilot ] && [ "$MIGRATION_STARTED" -eq 1 ] || return 1
  started="$HISTORY_DIR/${INVOCATION_ID}.migration-started.env"
  committed="$HISTORY_DIR/${INVOCATION_ID}.migration-committed.env"
  migration_committed_matches_started "$started" "$committed" && return 1
  return 0
}

atomic_copy() {
  local source="$1"
  local destination="$2"
  local temp
  temp="$(mktemp "$(dirname "$destination")/.routine-copy.XXXXXX")"
  cp "$source" "$temp"
  chmod 600 "$temp"
  mv -f "$temp" "$destination"
}

write_migration_started_state() {
  local temp
  [ "$TARGET" = aicopilot ] || return 0
  MIGRATION_STATE_FILE="$HISTORY_DIR/${INVOCATION_ID}.migration-started.env"
  temp="$(mktemp "$HISTORY_DIR/.migration-started.XXXXXX")"
  {
    printf 'RUNNER_PROTOCOL=%s\n' "$RUNNER_PROTOCOL_VERSION"
    printf 'TARGET=%s\n' "$TARGET_NAME"
    printf 'INVOCATION_ID=%s\n' "$INVOCATION_ID"
    printf 'GIT_SHA=%s\n' "$GIT_SHA"
    printf 'REQUEST_DIGEST=%s\n' "$REQUEST_DIGEST"
    printf 'DATABASE_BACKUP=%s\n' "$BACKUP_FILE"
    printf 'DATABASE_BACKUP_SHA256=%s\n' "$BACKUP_SHA256"
    printf 'STATUS=migration-started\n'
    printf 'RESUME_ALLOWED=false\n'
    printf 'STARTED_AT_UTC=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } > "$temp"
  chmod 600 "$temp"
  mv -f "$temp" "$MIGRATION_STATE_FILE"
  MIGRATION_STARTED=1
}

commit_migration_state() {
  local committed temp
  [ "$TARGET" = aicopilot ] && [ "$MIGRATION_STARTED" -eq 1 ] || return 0
  committed="$HISTORY_DIR/${INVOCATION_ID}.migration-committed.env"
  temp="$(mktemp "$HISTORY_DIR/.migration-committed.XXXXXX")"
  {
    printf 'RUNNER_PROTOCOL=%s\n' "$RUNNER_PROTOCOL_VERSION"
    printf 'TARGET=%s\n' "$TARGET_NAME"
    printf 'INVOCATION_ID=%s\n' "$INVOCATION_ID"
    printf 'GIT_SHA=%s\n' "$GIT_SHA"
    printf 'REQUEST_DIGEST=%s\n' "$REQUEST_DIGEST"
    printf 'DATABASE_BACKUP=%s\n' "$BACKUP_FILE"
    printf 'DATABASE_BACKUP_SHA256=%s\n' "$BACKUP_SHA256"
    printf 'STATUS=migration-committed\n'
    printf 'RESUME_ALLOWED=false\n'
    printf 'COMMITTED_AT_UTC=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } > "$temp"
  chmod 600 "$temp"
  mv -f "$temp" "$committed"
  MIGRATION_STARTED=0
  rm -f "$MIGRATION_STATE_FILE"
  MIGRATION_STATE_FILE="$committed"
}

validate_general_image_ref() {
  local value="$1"
  [[ "$value" =~ ^[A-Za-z0-9._:/@-]+$ ]] || return 1
  [[ "$value" == */* ]] || return 1
}

validate_immutable_image_ref() {
  local value="$1"
  validate_general_image_ref "$value" || return 1
  [[ "$value" =~ @sha256:[0-9a-f]{64}$ ]]
}

doctor_common() {
  local actual_user
  actual_user="$(id -un)"
  [ "$actual_user" = "$EXPECTED_USER" ] || fail "runner user mismatch: expected=$EXPECTED_USER actual=$actual_user" 77
  [ "$(id -u)" -ne 0 ] || fail "routine application deploy must not run as root" 77
  [ -d "$DEPLOY_DIR" ] && [ -x "$DEPLOY_DIR" ] || fail "deploy root is inaccessible: $DEPLOY_DIR" 73
  [ -r "$ENV_FILE" ] || fail "operator .env is missing or unreadable: $ENV_FILE" 66
  env_mode="$(path_mode "$ENV_FILE" | sed 's/.*\(...\)$/\1/')"
  [[ "$env_mode" =~ ^[0-7]00$ ]] || fail "operator .env must not grant group/other permissions: path=$ENV_FILE mode=$env_mode" 77
  [ -r "$COMPOSE_FILE" ] || fail "compose file is missing or unreadable: $COMPOSE_FILE" 66
  for directory in "$RELEASES_DIR" "$HISTORY_DIR" "$INCOMING_DIR" "$LOCK_DIR" "$DEPLOY_DIR/backups/postgres"; do
    [ -d "$directory" ] || fail "bootstrap directory is missing: $directory" 73
    [ -w "$directory" ] && [ -x "$directory" ] || fail "bootstrap directory is not writable by $EXPECTED_USER: $directory" 73
  done
  require_command docker
  require_command curl
  require_command flock
  require_command sha256sum
  docker info >/dev/null 2>&1 || fail "docker daemon is unavailable to $EXPECTED_USER; verify docker group membership" 77
  docker compose version >/dev/null 2>&1 || fail "docker compose v2 is unavailable" 69
}

compose_with_images() {
  local images_file="$1"
  shift
  if [ "$TARGET" = aicopilot ]; then
    AICOPILOT_MIGRATION_INVOCATION_ID="${INVOCATION_ID:-unbound}" \
      docker compose --env-file "$ENV_FILE" --env-file "$images_file" -f "$COMPOSE_FILE" "$@"
  else
    docker compose --env-file "$ENV_FILE" --env-file "$images_file" -f "$COMPOSE_FILE" "$@"
  fi
}

compose_without_images() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

verify_aicopilot_migration_completion() {
  local images_file="$1"
  local container exit_code logs expected_marker
  [ "$TARGET" = aicopilot ] || return 0

  container="$(compose_with_images "$images_file" ps -a -q "$MIGRATION_SERVICE" 2>/dev/null || true)"
  [[ "$container" =~ ^[A-Za-z0-9._-]+$ ]] || \
    fail "migration container identity is missing or ambiguous" 70
  exit_code="$(docker inspect --format '{{.State.ExitCode}}' "$container" 2>/dev/null || true)"
  [ "$exit_code" = 0 ] || fail "migration container exit code is not zero: ${exit_code:-unknown}" 70
  logs="$(docker logs "$container" 2>&1)" || fail "migration container logs are unreadable" 70
  expected_marker="aicopilot_migration_result=success invocation_id=$INVOCATION_ID"
  grep -Fqx "$expected_marker" <<< "$logs" || \
    fail "migration success marker is missing for invocation $INVOCATION_ID" 70
  printf 'runner_migration=verified container=%s exit_code=0 invocation_id=%s\n' \
    "$container" "$INVOCATION_ID"
}

if [ "$MODE" = doctor ]; then
  doctor_common
  if [ -f "$CURRENT_IMAGES_FILE" ]; then
    compose_with_images "$CURRENT_IMAGES_FILE" config --quiet
  else
    compose_without_images config --quiet
  fi
  for legacy_lock in "${LEGACY_LOCKS[@]}"; do
    if [ -d "$legacy_lock" ]; then
      fail "legacy deployment lock is present; inspect it once before using the routine runner: $legacy_lock" 75
    fi
  done
  printf 'runner_doctor=passed target=%s version=%s user=%s deploy_root=%s\n' \
    "$TARGET_NAME" "$RUNNER_PROTOCOL_VERSION" "$EXPECTED_USER" "$DEPLOY_DIR"
  exit 0
fi

doctor_common

REQUEST_FILE="$(mktemp "$INCOMING_DIR/.request.XXXXXX")"
REQUEST_BODY_FILE="$(mktemp "$INCOMING_DIR/.request-body.XXXXXX")"
CANDIDATE_IMAGES_FILE="$(mktemp "$INCOMING_DIR/.candidate-images.XXXXXX")"
PREVIOUS_IMAGES_FILE="$(mktemp "$INCOMING_DIR/.previous-images.XXXXXX")"
FAILURE_STATE_FILE=""
ROLLOUT_STARTED=0
ROLLBACK_REQUIRED=0
MIGRATION_STARTED=0
MIGRATION_COMPLETED=0
MIGRATION_STATE_FILE=""
BACKUP_FILE=none
BACKUP_SHA256=none
RUNTIME_COMPOSE_SERVICES=()
REQUEST_PROTOCOL=""
REQUEST_TARGET=""
INVOCATION_ID=""
GIT_SHA=""
RELEASE_TAG=""
SERVICES=""
REQUEST_DIGEST=""
RUNNER_SHA256=""
REQUEST_SEEN=" "
SELECTED=" "

cleanup_files() {
  rm -f "$REQUEST_FILE" "$REQUEST_BODY_FILE" "$CANDIDATE_IMAGES_FILE" "$PREVIOUS_IMAGES_FILE"
}

write_failure_state() {
  local original_status="$1"
  local runner_status="$2"
  local rollback_status="$3"
  local invocation="${INVOCATION_ID:-unknown}"
  local failure_status=failed
  local resume_allowed=true
  if [ "$TARGET" = aicopilot ] && \
     { migration_is_unresolved || [ "$runner_status" -eq 86 ]; }; then
    failure_status=blocked-partial
    resume_allowed=false
  fi
  FAILURE_STATE_FILE="$HISTORY_DIR/${invocation}.failed.env"
  {
    printf 'RUNNER_PROTOCOL=%s\n' "$RUNNER_PROTOCOL_VERSION"
    printf 'TARGET=%s\n' "$TARGET_NAME"
    printf 'INVOCATION_ID=%s\n' "$invocation"
    printf 'GIT_SHA=%s\n' "${GIT_SHA:-unknown}"
    printf 'SERVICES=%s\n' "${SERVICES:-unknown}"
    printf 'REQUEST_DIGEST=%s\n' "${REQUEST_DIGEST:-unknown}"
    printf 'STATUS=%s\n' "$failure_status"
    printf 'EXIT_CODE=%s\n' "$original_status"
    printf 'RUNNER_EXIT_CODE=%s\n' "$runner_status"
    printf 'ROLLBACK_STATUS=%s\n' "$rollback_status"
    printf 'RESUME_ALLOWED=%s\n' "$resume_allowed"
    printf 'MIGRATION_STARTED=%s\n' "$MIGRATION_STARTED"
    printf 'MIGRATION_COMPLETED=%s\n' "$MIGRATION_COMPLETED"
    printf 'DATABASE_BACKUP=%s\n' "${BACKUP_FILE:-none}"
    printf 'DATABASE_BACKUP_SHA256=%s\n' "${BACKUP_SHA256:-none}"
    printf 'FINISHED_AT_UTC=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  } > "$FAILURE_STATE_FILE"
  chmod 600 "$FAILURE_STATE_FILE"
}

on_exit() {
  local status=$?
  local runner_status=$status
  local rollback_status=not-required
  trap - EXIT HUP INT TERM
  set +e
  if [ "$status" -ne 0 ] && migration_is_unresolved; then
    runner_status=86
    rollback_status=blocked-migration-started
  elif [ "$status" -ne 0 ] && [ "$ROLLBACK_REQUIRED" -eq 1 ] && [ "$ROLLOUT_STARTED" -eq 1 ]; then
    rollback_status=failed
    if [ -s "$PREVIOUS_IMAGES_FILE" ]; then
      printf 'runner_phase=rollback target=%s\n' "$TARGET_NAME" >&2
      if [ "${#RUNTIME_COMPOSE_SERVICES[@]}" -gt 0 ] && \
         compose_with_images "$PREVIOUS_IMAGES_FILE" up -d --no-deps "${RUNTIME_COMPOSE_SERVICES[@]}"; then
        if verify_runtime_recovery "$PREVIOUS_IMAGES_FILE"; then
          rollback_status=completed
        else
          rollback_status=failed-unverified
          [ "$TARGET" != aicopilot ] || runner_status=86
        fi
      else
        [ "$TARGET" != aicopilot ] || runner_status=86
      fi
    fi
  fi
  if [ "$status" -ne 0 ]; then
    write_failure_state "$status" "$runner_status" "$rollback_status"
    printf 'runner_result=failed target=%s exit_code=%s rollback=%s evidence=%s\n' \
      "$TARGET_NAME" "$runner_status" "$rollback_status" "$FAILURE_STATE_FILE" >&2
  fi
  cleanup_files
  exit "$runner_status"
}
trap on_exit EXIT
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

cat > "$REQUEST_FILE"
chmod 600 "$REQUEST_FILE"
[ -s "$REQUEST_FILE" ] || fail "empty deployment request" 65

allowed_request_key() {
  case "$1" in
    PROTOCOL|TARGET|INVOCATION_ID|GIT_SHA|RELEASE_TAG|SERVICES|RUNNER_SHA256|REQUEST_DIGEST|\
    IIOT_HTTPAPI_IMAGE|IIOT_GATEWAY_IMAGE|IIOT_DATAWORKER_IMAGE|IIOT_MIGRATION_IMAGE|IIOT_WEB_IMAGE|\
    AICOPILOT_HTTPAPI_IMAGE|AICOPILOT_MIGRATION_IMAGE|AICOPILOT_DATAWORKER_IMAGE|AICOPILOT_RAGWORKER_IMAGE|AICOPILOT_WEBUI_IMAGE)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

while IFS= read -r line || [ -n "$line" ]; do
  line="${line%$'\r'}"
  [ -n "$line" ] || continue
  [[ "$line" == *=* ]] || fail "request line is not KEY=VALUE" 65
  key="${line%%=*}"
  value="${line#*=}"
  [[ "$key" =~ ^[A-Z0-9_]+$ ]] || fail "invalid request key" 65
  allowed_request_key "$key" || fail "unsupported request key: $key" 65
  case "$REQUEST_SEEN" in
    *" $key "*) fail "duplicate request key: $key" 65 ;;
  esac
  REQUEST_SEEN="$REQUEST_SEEN$key "
  case "$key" in
    PROTOCOL) REQUEST_PROTOCOL="$value" ;;
    TARGET) REQUEST_TARGET="$value" ;;
    INVOCATION_ID) INVOCATION_ID="$value" ;;
    GIT_SHA) GIT_SHA="$value" ;;
    RELEASE_TAG) RELEASE_TAG="$value" ;;
    SERVICES) SERVICES="$value" ;;
    RUNNER_SHA256) RUNNER_SHA256="$value" ;;
    REQUEST_DIGEST) REQUEST_DIGEST="$value" ;;
    *) printf -v "$key" '%s' "$value" ;;
  esac
  if [ "$key" != REQUEST_DIGEST ]; then
    printf '%s=%s\n' "$key" "$value" >> "$REQUEST_BODY_FILE"
  fi
done < "$REQUEST_FILE"

[ "$REQUEST_PROTOCOL" = "$RUNNER_PROTOCOL_VERSION" ] || fail "request protocol mismatch" 65
[ "$REQUEST_TARGET" = "$TARGET_NAME" ] || fail "request target mismatch" 65
[[ "$INVOCATION_ID" =~ ^[A-Za-z0-9._-]{1,96}$ ]] || fail "invalid invocation id" 65
[[ "$GIT_SHA" =~ ^[0-9a-f]{40}([0-9a-f]{24})?$ ]] || fail "invalid git SHA" 65
[ "$RELEASE_TAG" = "sha-$GIT_SHA" ] || fail "release tag is not bound to git SHA" 65
[[ "$REQUEST_DIGEST" =~ ^[0-9a-f]{64}$ ]] || fail "invalid request digest" 65
actual_request_digest="$(sha256sum "$REQUEST_BODY_FILE" | awk '{print $1}')"
[ "$actual_request_digest" = "$REQUEST_DIGEST" ] || fail "request digest mismatch" 65
if [ "$TARGET" = aicopilot ]; then
  [[ "$RUNNER_SHA256" =~ ^[0-9a-f]{64}$ ]] || fail "AICopilot request is missing the prepared runner SHA-256" 65
  running_runner_path="${BASH_SOURCE[0]}"
  if [ -r "/proc/$$/fd/255" ]; then
    running_runner_path="/proc/$$/fd/255"
  fi
  actual_runner_sha256="$(sha256sum "$running_runner_path" | awk '{print $1}')"
  [ "$actual_runner_sha256" = "$RUNNER_SHA256" ] || \
    fail "running AICopilot runner bytes do not match the prepared request" 78
fi

IFS=',' read -r -a REQUESTED_SERVICES <<< "$SERVICES"
[ "${#REQUESTED_SERVICES[@]}" -gt 0 ] || fail "no services selected" 65
for service in "${REQUESTED_SERVICES[@]}"; do
  [[ "$service" =~ ^[a-z]+$ ]] || fail "invalid service token: $service" 65
  case "$SELECTED" in *" $service "*) fail "duplicate service: $service" 65 ;; esac
  key="$(image_key_for_service "$service" || true)"
  [ -n "$key" ] || fail "service is not allowed for $TARGET_NAME: $service" 65
  case "$REQUEST_SEEN" in
    *" $key "*) ;;
    *) fail "request is missing selected image key: $key" 65 ;;
  esac
  SELECTED="$SELECTED$service "
  image_ref="$(request_image_for_key "$key")"
  validate_immutable_image_ref "$image_ref" || fail "selected image is not an immutable OCI reference: $key" 65
  if service_is_runtime "$service"; then
    RUNTIME_COMPOSE_SERVICES+=("$(compose_name_for_service "$service")")
  fi
done

if [ "$TARGET" = aicopilot ] && \
   { [[ "$SELECTED" == *" httpapi "* ]] || [[ "$SELECTED" == *" dataworker "* ]] || [[ "$SELECTED" == *" ragworker "* ]]; } && \
   [[ "$SELECTED" != *" migration "* ]]; then
  fail "AICopilot backend runtime releases must include migration" 65
fi
if [ "$TARGET" = aicopilot ] && [[ "$SELECTED" == *" migration "* ]]; then
  for required_runtime in httpapi dataworker ragworker web; do
    [[ "$SELECTED" == *" $required_runtime "* ]] || \
      fail "AICopilot migration requires the complete runtime group: httpapi,migration,dataworker,ragworker,web" 65
  done
fi

mkdir -p "$RELEASES_DIR" "$HISTORY_DIR" "$INCOMING_DIR" "$LOCK_DIR" "$DEPLOY_DIR/backups/postgres"
exec 9> "$LOCK_FILE"
flock -n 9 || fail "another routine deployment is active: $LOCK_FILE" 75
for legacy_lock in "${LEGACY_LOCKS[@]}"; do
  [ ! -d "$legacy_lock" ] || fail "legacy deployment lock is present: $legacy_lock" 75
done
unresolved_migration=""
while IFS= read -r migration_started_file; do
  [ -n "$migration_started_file" ] || continue
  migration_committed_file="${migration_started_file%.migration-started.env}.migration-committed.env"
  if migration_committed_matches_started "$migration_started_file" "$migration_committed_file"; then
    rm -f "$migration_started_file"
    printf 'runner_migration_state=reconciled-committed started=%s committed=%s\n' \
      "$migration_started_file" "$migration_committed_file"
    continue
  fi
  unresolved_migration="$migration_started_file"
  break
done < <(find "$HISTORY_DIR" -maxdepth 1 -type f -name '*.migration-started.env' -print 2>/dev/null | LC_ALL=C sort)
if [ -n "$unresolved_migration" ]; then
  MIGRATION_STATE_FILE="$unresolved_migration"
  MIGRATION_STARTED=1
  BACKUP_FILE="$(read_env_value "$unresolved_migration" DATABASE_BACKUP || printf none)"
  BACKUP_SHA256="$(read_env_value "$unresolved_migration" DATABASE_BACKUP_SHA256 || printf none)"
  fail "unresolved AICopilot migration state blocks automatic retry: $unresolved_migration" 86
fi

: > "$PREVIOUS_IMAGES_FILE"
: > "$CANDIDATE_IMAGES_FILE"
for service in "${ALL_SERVICES[@]}"; do
  key="$(image_key_for_service "$service")"
  previous_ref=""
  if [ -f "$CURRENT_IMAGES_FILE" ]; then
    previous_ref="$(read_env_value "$CURRENT_IMAGES_FILE" "$key" || true)"
  fi
  if [ -z "$previous_ref" ]; then
    previous_ref="$(read_env_value "$ENV_FILE" "$key" || true)"
  fi
  validate_general_image_ref "$previous_ref" || fail "initial image baseline is missing or invalid: $key" 66
  printf '%s=%s\n' "$key" "$previous_ref" >> "$PREVIOUS_IMAGES_FILE"
  if [[ "$SELECTED" == *" $service "* ]]; then
    printf '%s=%s\n' "$key" "$(request_image_for_key "$key")" >> "$CANDIDATE_IMAGES_FILE"
  else
    printf '%s=%s\n' "$key" "$previous_ref" >> "$CANDIDATE_IMAGES_FILE"
  fi
done
chmod 600 "$PREVIOUS_IMAGES_FILE" "$CANDIDATE_IMAGES_FILE"
compose_with_images "$CANDIDATE_IMAGES_FILE" config --quiet

runtime_health_snapshot() {
  local images_file="$1"
  local service_name="$2"
  local container state running restarting oom_killed restart_count health
  container="$(compose_with_images "$images_file" ps -q "$service_name" 2>/dev/null || true)"
  [ -n "$container" ] || return 1
  state="$(docker inspect --format '{{.State.Running}}|{{.State.Restarting}}|{{.State.OOMKilled}}|{{.RestartCount}}|{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container" 2>/dev/null || true)"
  IFS='|' read -r running restarting oom_killed restart_count health <<< "$state"
  [ "$running" = true ] && [ "$restarting" = false ] && [ "$oom_killed" = false ] || return 1
  [[ "$restart_count" =~ ^[0-9]+$ ]] || return 1
  { [ "$health" = none ] || [ "$health" = healthy ]; } || return 1
  printf '%s|%s\n' "$container" "$restart_count"
}

service_running_and_healthy() {
  runtime_health_snapshot "$1" "$2" >/dev/null
}

recovery_http_endpoint_stable() {
  local url="$1"
  local health_attempts="$2"
  local health_interval="$3"
  local attempt first_status second_status
  for attempt in $(seq 1 "$health_attempts"); do
    first_status="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 5 "$url" || true)"
    if [ "$first_status" = 200 ]; then
      sleep "$health_interval"
      second_status="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 5 "$url" || true)"
      [ "$second_status" = 200 ] && return 0
    fi
    sleep "$health_interval"
  done
  return 1
}

wait_service() {
  local images_file="$1"
  local service_name="$2"
  local attempt
  local health_attempts="${IIOT_RUNNER_HEALTH_ATTEMPTS:-90}"
  local health_interval="${IIOT_RUNNER_HEALTH_INTERVAL_SECONDS:-2}"
  [[ "$health_attempts" =~ ^[1-9][0-9]*$ ]] || fail "IIOT_RUNNER_HEALTH_ATTEMPTS must be a positive integer" 64
  [ "$health_attempts" -le 300 ] || fail "IIOT_RUNNER_HEALTH_ATTEMPTS must not exceed 300" 64
  [[ "$health_interval" =~ ^[0-9]+$ ]] || fail "IIOT_RUNNER_HEALTH_INTERVAL_SECONDS must be a non-negative integer" 64
  [ "$health_interval" -le 30 ] || fail "IIOT_RUNNER_HEALTH_INTERVAL_SECONDS must not exceed 30" 64
  for attempt in $(seq 1 "$health_attempts"); do
    if service_running_and_healthy "$images_file" "$service_name"; then
      sleep "$health_interval"
      service_running_and_healthy "$images_file" "$service_name" && return 0
    fi
    sleep "$health_interval"
  done
  fail "service did not become running/healthy: $service_name" 70
}

verify_runtime_recovery() {
  local images_file="$1"
  local service_name attempt recovered snapshot_before snapshot_after ai_port
  local health_attempts="${IIOT_RUNNER_HEALTH_ATTEMPTS:-90}"
  local health_interval="${IIOT_RUNNER_HEALTH_INTERVAL_SECONDS:-2}"
  [[ "$health_attempts" =~ ^[1-9][0-9]*$ ]] || return 1
  [ "$health_attempts" -le 300 ] || return 1
  [[ "$health_interval" =~ ^[0-9]+$ ]] || return 1
  [ "$health_interval" -le 30 ] || return 1
  for service_name in "${RUNTIME_COMPOSE_SERVICES[@]}"; do
    recovered=0
    for attempt in $(seq 1 "$health_attempts"); do
      if snapshot_before="$(runtime_health_snapshot "$images_file" "$service_name")"; then
        sleep "$health_interval"
        if snapshot_after="$(runtime_health_snapshot "$images_file" "$service_name")" &&
           [ "$snapshot_after" = "$snapshot_before" ]; then
          recovered=1
          break
        fi
      fi
      sleep "$health_interval"
    done
    [ "$recovered" -eq 1 ] || return 1
  done
  if [ "$TARGET" = aicopilot ]; then
    ai_port="$(read_env_value "$ENV_FILE" AICOPILOT_WEB_PORT || true)"
    recovery_http_endpoint_stable \
      "http://127.0.0.1:${ai_port:-82}/" "$health_attempts" "$health_interval" || return 1
    recovery_http_endpoint_stable \
      "http://127.0.0.1:${ai_port:-82}/api/identity/cloud-oidc/status" \
      "$health_attempts" "$health_interval" || return 1
  fi
}

probe_http() {
  local url="$1"
  local health_attempts="${IIOT_RUNNER_HEALTH_ATTEMPTS:-90}"
  local health_interval="${IIOT_RUNNER_HEALTH_INTERVAL_SECONDS:-2}"
  local attempt status_code
  for attempt in $(seq 1 "$health_attempts"); do
    status_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 5 "$url" || true)"
    if [ "$status_code" = 200 ]; then
      printf 'runner_probe=passed url=%s status=200\n' "$url"
      return 0
    fi
    sleep "$health_interval"
  done
  fail "HTTP health probe failed: url=$url status=${status_code:-curl-error}" 70
}

if [ -f "$CURRENT_STATE_FILE" ] && \
   [ "$(read_env_value "$CURRENT_STATE_FILE" REQUEST_DIGEST || true)" = "$REQUEST_DIGEST" ]; then
  already_healthy=1
  for runtime_service in "${RUNTIME_COMPOSE_SERVICES[@]}"; do
    service_running_and_healthy "$CANDIDATE_IMAGES_FILE" "$runtime_service" || already_healthy=0
  done
  if [ "$already_healthy" -eq 1 ]; then
    printf 'runner_result=already-current target=%s sha=%s services=%s\n' \
      "$TARGET_NAME" "$GIT_SHA" "$SERVICES"
    exit 0
  fi
fi

ensure_infrastructure() {
  local service_name="$1"
  local container
  container="$(compose_with_images "$CANDIDATE_IMAGES_FILE" ps -q "$service_name" 2>/dev/null || true)"
  if [ -z "$container" ]; then
    compose_with_images "$CANDIDATE_IMAGES_FILE" up -d "$service_name"
  elif ! service_running_and_healthy "$CANDIDATE_IMAGES_FILE" "$service_name"; then
    compose_with_images "$CANDIDATE_IMAGES_FILE" start "$service_name"
  fi
  wait_service "$CANDIDATE_IMAGES_FILE" "$service_name"
}

printf 'runner_phase=preflight target=%s sha=%s services=%s\n' \
  "$TARGET_NAME" "$GIT_SHA" "$SERVICES"

if [ "$TARGET" = cloud ]; then
  ensure_infrastructure postgres
  ensure_infrastructure redis-cache
  if [ "${#RUNTIME_COMPOSE_SERVICES[@]}" -gt 0 ]; then
    ensure_infrastructure rabbitmq
  fi
else
  ensure_infrastructure postgres
  if [ "${#RUNTIME_COMPOSE_SERVICES[@]}" -gt 0 ]; then
    ensure_infrastructure eventbus
    ensure_infrastructure qdrant
  fi
fi

SELECTED_COMPOSE_SERVICES=()
for service in "${REQUESTED_SERVICES[@]}"; do
  SELECTED_COMPOSE_SERVICES+=("$(compose_name_for_service "$service")")
done

printf 'runner_phase=pull target=%s\n' "$TARGET_NAME"
compose_with_images "$CANDIDATE_IMAGES_FILE" pull "${SELECTED_COMPOSE_SERVICES[@]}"

if [[ "$SELECTED" == *" migration "* ]]; then
  if [ "$TARGET" = aicopilot ]; then
    printf 'runner_phase=runtime-quiesce target=%s services=%s\n' \
      "$TARGET_NAME" "${RUNTIME_COMPOSE_SERVICES[*]}"
    ROLLOUT_STARTED=1
    ROLLBACK_REQUIRED=1
    compose_with_images "$PREVIOUS_IMAGES_FILE" stop "${RUNTIME_COMPOSE_SERVICES[@]}"
  fi

  backup_dir="$DEPLOY_DIR/backups/postgres"
  BACKUP_FILE="$backup_dir/${TARGET}-${INVOCATION_ID}.dump"
  backup_temp="${BACKUP_FILE}.partial"
  rm -f "$backup_temp"
  printf 'runner_phase=database-backup target=%s file=%s\n' "$TARGET_NAME" "$BACKUP_FILE"
  if [ "$TARGET" = cloud ]; then
    if ! compose_with_images "$CANDIDATE_IMAGES_FILE" exec -T postgres \
      pg_dump -h 127.0.0.1 -Fc -U postgres -d iiot-db > "$backup_temp"; then
      rm -f "$backup_temp"
      fail "database backup command failed" 74
    fi
  else
    postgres_user="$(read_env_value "$ENV_FILE" POSTGRES_USER || true)"
    postgres_db="$(read_env_value "$ENV_FILE" POSTGRES_DB || true)"
    postgres_password="$(read_env_value "$ENV_FILE" POSTGRES_PASSWORD || true)"
    [ -n "$postgres_user" ] && [ -n "$postgres_db" ] && [ -n "$postgres_password" ] || \
      fail "POSTGRES_USER/POSTGRES_DB/POSTGRES_PASSWORD are required for migration backup" 66
    if ! compose_with_images "$CANDIDATE_IMAGES_FILE" exec -T -e "PGPASSWORD=$postgres_password" postgres \
      pg_dump -h 127.0.0.1 -Fc -U "$postgres_user" -d "$postgres_db" > "$backup_temp"; then
      rm -f "$backup_temp"
      fail "database backup command failed" 74
    fi
  fi
  [ -s "$backup_temp" ] || fail "database backup is empty" 74
  mv "$backup_temp" "$BACKUP_FILE"
  chmod 600 "$BACKUP_FILE"
  BACKUP_SHA256="$(sha256sum "$BACKUP_FILE" | awk '{print $1}')"
  printf '%s  %s\n' "$BACKUP_SHA256" "$(basename "$BACKUP_FILE")" > "$BACKUP_FILE.sha256"
  chmod 600 "$BACKUP_FILE.sha256"

  write_migration_started_state

  printf 'runner_phase=migration target=%s\n' "$TARGET_NAME"
  if ! compose_with_images "$CANDIDATE_IMAGES_FILE" up --no-deps --abort-on-container-exit \
    --exit-code-from "$MIGRATION_SERVICE" "$MIGRATION_SERVICE"; then
    fail "compose migration command failed" 70
  fi
  verify_aicopilot_migration_completion "$CANDIDATE_IMAGES_FILE"
  if [ "$TARGET" = aicopilot ]; then
    MIGRATION_COMPLETED=1
  fi
fi

if [ "${#RUNTIME_COMPOSE_SERVICES[@]}" -gt 0 ]; then
  printf 'runner_phase=rollout target=%s services=%s\n' "$TARGET_NAME" "${RUNTIME_COMPOSE_SERVICES[*]}"
  ROLLOUT_STARTED=1
  ROLLBACK_REQUIRED=1
  compose_with_images "$CANDIDATE_IMAGES_FILE" up -d --no-deps "${RUNTIME_COMPOSE_SERVICES[@]}"
  for runtime_service in "${RUNTIME_COMPOSE_SERVICES[@]}"; do
    wait_service "$CANDIDATE_IMAGES_FILE" "$runtime_service"
  done
  if [ "$TARGET" = cloud ] && \
     { [[ "$SELECTED" == *" httpapi "* ]] || [[ "$SELECTED" == *" gateway "* ]] || [[ "$SELECTED" == *" web "* ]]; }; then
    cloud_port="$(read_env_value "$ENV_FILE" GATEWAY_HTTP_PORT || true)"
    probe_http "http://127.0.0.1:${cloud_port:-80}/internal/healthz"
  elif [ "$TARGET" = aicopilot ] && \
       { [[ "$SELECTED" == *" httpapi "* ]] || [[ "$SELECTED" == *" web "* ]]; }; then
    ai_port="$(read_env_value "$ENV_FILE" AICOPILOT_WEB_PORT || true)"
    probe_http "http://127.0.0.1:${ai_port:-82}/"
  fi
fi

state_temp="$(mktemp "$RELEASES_DIR/.routine-state.XXXXXX")"
{
  printf 'RUNNER_PROTOCOL=%s\n' "$RUNNER_PROTOCOL_VERSION"
  printf 'TARGET=%s\n' "$TARGET_NAME"
  printf 'INVOCATION_ID=%s\n' "$INVOCATION_ID"
  printf 'GIT_SHA=%s\n' "$GIT_SHA"
  printf 'RELEASE_TAG=%s\n' "$RELEASE_TAG"
  printf 'SERVICES=%s\n' "$SERVICES"
  printf 'REQUEST_DIGEST=%s\n' "$REQUEST_DIGEST"
  printf 'DATABASE_BACKUP=%s\n' "$BACKUP_FILE"
  printf 'DATABASE_BACKUP_SHA256=%s\n' "$BACKUP_SHA256"
  printf 'STATUS=healthy\n'
  printf 'FINISHED_AT_UTC=%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} > "$state_temp"
chmod 600 "$state_temp"

atomic_copy "$CANDIDATE_IMAGES_FILE" "$CURRENT_IMAGES_FILE"
atomic_copy "$state_temp" "$CURRENT_STATE_FILE"
atomic_copy "$state_temp" "$HISTORY_DIR/${INVOCATION_ID}.env"
rm -f "$state_temp"
ROLLBACK_REQUIRED=0
commit_migration_state

printf 'runner_result=success target=%s sha=%s services=%s state=%s\n' \
  "$TARGET_NAME" "$GIT_SHA" "$SERVICES" "$CURRENT_STATE_FILE"
