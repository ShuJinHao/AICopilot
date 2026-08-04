#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
RUNNER_SOURCE="$PROJECT_ROOT/deploy/enterprise-ai/runner/iiot-release-runner.sh"
TEST_ROOT="$(mktemp -d /tmp/aicopilot-migration-runner-guard.XXXXXX)"
BIN_DIR="$TEST_ROOT/bin"
SERVER_DIR="$TEST_ROOT/server"
DOCKER_LOG="$TEST_ROOT/docker.log"
MIGRATION_INVOCATION_FILE="$TEST_ROOT/migration-invocation.txt"

cleanup() {
  rm -rf "$TEST_ROOT"
}
trap cleanup EXIT

fail() {
  printf 'AICopilot migration runner guard test failed: %s\n' "$1" >&2
  exit 1
}

mkdir -p "$BIN_DIR" "$SERVER_DIR/runner" "$SERVER_DIR/releases/routine-history" \
  "$SERVER_DIR/releases/routine-incoming" "$SERVER_DIR/.locks" "$SERVER_DIR/backups/postgres"
cp "$RUNNER_SOURCE" "$SERVER_DIR/runner/iiot-release-runner.sh"
chmod 700 "$SERVER_DIR/runner/iiot-release-runner.sh"
sed -n '/^commit_migration_state()/,/^}/p' "$RUNNER_SOURCE" | \
  grep -Fq 'MIGRATION_STARTED=0' || \
  fail 'committed migration state does not clear the in-memory partial flag'
grep -Fq 'migration_is_unresolved' "$RUNNER_SOURCE" || \
  fail 'runner exit handling does not derive partial state from durable markers'

cat > "$BIN_DIR/docker" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "${FAKE_DOCKER_LOG:?}"
case "${1:-}" in
  info)
    exit 0
    ;;
  inspect)
    if [[ "$*" == *'.State.ExitCode'* ]]; then
      printf '%s\n' "${FAKE_MIGRATION_EXIT_CODE:-0}"
    elif [ "${FAKE_RUNTIME_RECOVERY_UNHEALTHY:-false}" = true ] &&
         [ -f "${FAKE_RECOVERY_PHASE_FILE:?}" ]; then
      printf 'false|false|false|0|unhealthy\n'
    elif [ "${FAKE_RUNTIME_RECOVERY_RESTART_UNSTABLE:-false}" = true ] &&
         [ -f "${FAKE_RECOVERY_PHASE_FILE:?}" ]; then
      count=0
      [ ! -f "${FAKE_RECOVERY_INSPECT_COUNT_FILE:?}" ] ||
        count="$(cat "$FAKE_RECOVERY_INSPECT_COUNT_FILE")"
      count=$((count + 1))
      printf '%s\n' "$count" > "$FAKE_RECOVERY_INSPECT_COUNT_FILE"
      printf 'true|false|false|%s|healthy\n' "$count"
    else
      printf 'true|false|false|0|healthy\n'
    fi
    exit 0
    ;;
  logs)
    if [ "${FAKE_MIGRATION_MARKER_MODE:-success}" = success ]; then
      invocation_id="$(sed -n '1p' "${FAKE_MIGRATION_INVOCATION_FILE:?}")"
      printf 'aicopilot_migration_result=success invocation_id=%s\n' "$invocation_id"
    else
      printf 'aicopilot_migration_result=failure exception=InvalidOperationException\n'
    fi
    exit 0
    ;;
  compose)
    shift
    while [ "$#" -gt 0 ]; do
      case "$1" in
        --env-file|-f) shift 2 ;;
        *) command="$1"; shift; break ;;
      esac
    done
    case "$command" in
      version|config|pull|start)
        exit 0
        ;;
      stop)
        if [ "${FAKE_RUNTIME_QUIESCE_FAIL:-false}" = true ]; then
          : > "${FAKE_RECOVERY_PHASE_FILE:?}"
          exit 19
        fi
        exit 0
        ;;
      ps)
        while [ "${1:-}" = -a ] || [ "${1:-}" = -q ]; do shift; done
        printf 'container-%s\n' "${1:?service required}"
        exit 0
        ;;
      exec)
        if [[ "$*" == *pg_dump* ]]; then
          if [ "${FAKE_DATABASE_BACKUP_FAIL:-false}" = true ]; then
            exit 31
          fi
          printf 'fake-postgres-dump\n'
        fi
        exit 0
        ;;
      up)
        if [[ "$*" == *'-d --no-deps'* ]] &&
           [ "${FAKE_RUNTIME_RECOVERY_UP_FAIL:-false}" = true ]; then
          exit 23
        fi
        if [[ "$*" == *aicopilot-migration* ]] &&
           [ -n "${AICOPILOT_MIGRATION_INVOCATION_ID:-}" ]; then
          printf '%s\n' "$AICOPILOT_MIGRATION_INVOCATION_ID" > \
            "${FAKE_MIGRATION_INVOCATION_FILE:?}"
        fi
        exit 0
        ;;
    esac
    ;;
esac
printf 'unsupported fake docker invocation: %s\n' "$*" >&2
exit 2
EOF

cat > "$BIN_DIR/curl" <<'EOF'
#!/usr/bin/env bash
if [ "${FAKE_RUNTIME_RECOVERY_HTTP_FAIL:-false}" = true ] &&
   [ -f "${FAKE_RECOVERY_PHASE_FILE:?}" ]; then
  printf '503'
else
  printf '200'
fi
EOF
cat > "$BIN_DIR/flock" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
chmod +x "$BIN_DIR/docker" "$BIN_DIR/curl" "$BIN_DIR/flock"

cat > "$SERVER_DIR/.env" <<'EOF'
AICOPILOT_HTTPAPI_IMAGE=registry.test/enterprise-ai/aicopilot-httpapi:old
AICOPILOT_MIGRATION_IMAGE=registry.test/enterprise-ai/aicopilot-migration:old
AICOPILOT_DATAWORKER_IMAGE=registry.test/enterprise-ai/aicopilot-dataworker:old
AICOPILOT_RAGWORKER_IMAGE=registry.test/enterprise-ai/aicopilot-ragworker:old
AICOPILOT_WEBUI_IMAGE=registry.test/enterprise-ai/aicopilot-webui:old
POSTGRES_USER=aicopilot
POSTGRES_PASSWORD=test-password
POSTGRES_DB=aicopilot
POSTGRES_IMAGE=postgres:old
RABBITMQ_IMAGE=rabbitmq:old
QDRANT_IMAGE=qdrant:old
EOF
chmod 600 "$SERVER_DIR/.env"
printf 'services: {}\n' > "$SERVER_DIR/docker-compose.yaml"

cat > "$SERVER_DIR/releases/current-images.env" <<'EOF'
AICOPILOT_HTTPAPI_IMAGE=registry.test/enterprise-ai/aicopilot-httpapi:old
AICOPILOT_MIGRATION_IMAGE=registry.test/enterprise-ai/aicopilot-migration:old
AICOPILOT_DATAWORKER_IMAGE=registry.test/enterprise-ai/aicopilot-dataworker:old
AICOPILOT_RAGWORKER_IMAGE=registry.test/enterprise-ai/aicopilot-ragworker:old
AICOPILOT_WEBUI_IMAGE=registry.test/enterprise-ai/aicopilot-webui:old
EOF
cat > "$SERVER_DIR/releases/routine-current.env" <<'EOF'
RUNNER_PROTOCOL=1
TARGET=AICopilot
INVOCATION_ID=baseline
GIT_SHA=0000000000000000000000000000000000000000
SERVICES=httpapi,migration,dataworker,ragworker,web
REQUEST_DIGEST=baseline
STATUS=healthy
EOF
chmod 600 "$SERVER_DIR/releases/current-images.env" "$SERVER_DIR/releases/routine-current.env"

make_request() {
  local invocation_id="$1"
  local sha="$2"
  local digest_character="$3"
  local services="${4:-httpapi,migration,dataworker,ragworker,web}"
  local runner_sha256="${5:-}"
  local body="$TEST_ROOT/$invocation_id.body"
  local request="$TEST_ROOT/$invocation_id.env"
  local image_digest
  image_digest="$(printf '%064d' 0 | tr '0' "$digest_character")"
  if [ -z "$runner_sha256" ]; then
    runner_sha256="$(sha256sum "$SERVER_DIR/runner/iiot-release-runner.sh" | awk '{print $1}')"
  fi
  {
    printf 'PROTOCOL=1\n'
    printf 'TARGET=AICopilot\n'
    printf 'INVOCATION_ID=%s\n' "$invocation_id"
    printf 'GIT_SHA=%s\n' "$sha"
    printf 'RELEASE_TAG=sha-%s\n' "$sha"
    printf 'SERVICES=%s\n' "$services"
    printf 'RUNNER_SHA256=%s\n' "$runner_sha256"
    printf 'AICOPILOT_HTTPAPI_IMAGE=registry.test/enterprise-ai/aicopilot-httpapi@sha256:%s\n' "$image_digest"
    printf 'AICOPILOT_MIGRATION_IMAGE=registry.test/enterprise-ai/aicopilot-migration@sha256:%s\n' "$image_digest"
    printf 'AICOPILOT_DATAWORKER_IMAGE=registry.test/enterprise-ai/aicopilot-dataworker@sha256:%s\n' "$image_digest"
    printf 'AICOPILOT_RAGWORKER_IMAGE=registry.test/enterprise-ai/aicopilot-ragworker@sha256:%s\n' "$image_digest"
    printf 'AICOPILOT_WEBUI_IMAGE=registry.test/enterprise-ai/aicopilot-webui@sha256:%s\n' "$image_digest"
  } > "$body"
  cp "$body" "$request"
  printf 'REQUEST_DIGEST=%s\n' "$(sha256sum "$body" | awk '{print $1}')" >> "$request"
  printf '%s\n' "$request"
}

run_request() {
  local request="$1"
  rm -f "$TEST_ROOT/recovery-phase" "$TEST_ROOT/recovery-inspect-count"
  PATH="$BIN_DIR:$PATH" \
  FAKE_DOCKER_LOG="$DOCKER_LOG" \
  FAKE_MIGRATION_INVOCATION_FILE="$MIGRATION_INVOCATION_FILE" \
  FAKE_RECOVERY_PHASE_FILE="$TEST_ROOT/recovery-phase" \
  FAKE_RECOVERY_INSPECT_COUNT_FILE="$TEST_ROOT/recovery-inspect-count" \
  IIOT_RUNNER_HEALTH_ATTEMPTS=1 \
  IIOT_RUNNER_HEALTH_INTERVAL_SECONDS=0 \
    "$SERVER_DIR/runner/iiot-release-runner.sh" \
      --target aicopilot --expected-user "$(id -un)" --request-stdin < "$request"
}

: > "$DOCKER_LOG"
partial_invocation='migration-guard-partial-group'
partial_request="$(make_request "$partial_invocation" 5555555555555555555555555555555555555555 e 'migration,dataworker')"
set +e
FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
  run_request "$partial_request" > "$TEST_ROOT/partial-group.log" 2>&1
partial_status=$?
set -e
[ "$partial_status" -eq 65 ] || \
  fail 'partial AICopilot migration group was not rejected before mutation'
grep -Fq 'migration requires the complete runtime group' "$TEST_ROOT/partial-group.log" || \
  fail 'partial AICopilot migration rejection did not identify the runtime closure'
grep -Eq 'pull| stop | up | start | exec ' "$DOCKER_LOG" && \
  fail 'partial AICopilot migration request mutated containers'
[ ! -f "$SERVER_DIR/releases/routine-history/$partial_invocation.migration-started.env" ] || \
  fail 'partial AICopilot migration request wrote a started marker'

: > "$DOCKER_LOG"
runner_drift_invocation='migration-guard-runner-drift'
runner_drift_request="$(make_request "$runner_drift_invocation" 7777777777777777777777777777777777777777 a 'web' "$(printf '%064d' 0)")"
set +e
FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
  run_request "$runner_drift_request" > "$TEST_ROOT/runner-drift.log" 2>&1
runner_drift_status=$?
set -e
[ "$runner_drift_status" -eq 78 ] || \
  fail 'runner byte drift was not rejected by the signed request'
grep -Fq 'running AICopilot runner bytes do not match the prepared request' \
  "$TEST_ROOT/runner-drift.log" || fail 'runner byte drift rejection was ambiguous'
grep -Eq 'compose .* (pull|stop|up|start|exec)( |$)' "$DOCKER_LOG" && \
  fail 'runner byte drift reached Docker mutation'

: > "$DOCKER_LOG"
success_invocation='migration-guard-success'
success_request="$(make_request "$success_invocation" 1111111111111111111111111111111111111111 a)"
FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 run_request "$success_request"
stop_line="$(grep -n 'compose .* stop .*aicopilot-httpapi' "$DOCKER_LOG" | head -n 1 | cut -d: -f1)"
backup_line="$(grep -n 'compose .* exec .*pg_dump' "$DOCKER_LOG" | head -n 1 | cut -d: -f1)"
[ -n "$stop_line" ] && [ -n "$backup_line" ] && [ "$stop_line" -lt "$backup_line" ] || \
  fail 'final PostgreSQL dump was not taken after all old runtimes were quiesced'
grep -Fq "INVOCATION_ID=$success_invocation" "$SERVER_DIR/releases/routine-current.env" || \
  fail 'valid migration proof did not commit current state'
[ -f "$SERVER_DIR/releases/routine-history/$success_invocation.migration-committed.env" ] || \
  fail 'valid migration proof did not commit the durable migration state'
[ ! -f "$SERVER_DIR/releases/routine-history/$success_invocation.migration-started.env" ] || \
  fail 'valid migration proof left an unresolved migration-started marker'

sed 's/^STATUS=migration-committed$/STATUS=migration-started/' \
  "$SERVER_DIR/releases/routine-history/$success_invocation.migration-committed.env" > \
  "$SERVER_DIR/releases/routine-history/$success_invocation.migration-started.env"
FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
  run_request "$success_request" > "$TEST_ROOT/committed-reconcile.log" 2>&1
grep -Fq 'runner_migration_state=reconciled-committed' \
  "$TEST_ROOT/committed-reconcile.log" || \
  fail 'exact committed migration counterpart was not reconciled'
[ ! -f "$SERVER_DIR/releases/routine-history/$success_invocation.migration-started.env" ] || \
  fail 'reconciled committed migration left a stale started marker'

cp "$SERVER_DIR/releases/current-images.env" "$TEST_ROOT/current-images.before"
cp "$SERVER_DIR/releases/routine-current.env" "$TEST_ROOT/routine-current.before"

: > "$DOCKER_LOG"
quiesce_failure_invocation='migration-guard-quiesce-failure'
quiesce_failure_request="$(make_request "$quiesce_failure_invocation" 4444444444444444444444444444444444444444 d)"
set +e
FAKE_RUNTIME_QUIESCE_FAIL=true FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
  run_request "$quiesce_failure_request" > "$TEST_ROOT/quiesce-failure.log" 2>&1
quiesce_failure_status=$?
set -e
[ "$quiesce_failure_status" -ne 0 ] && [ "$quiesce_failure_status" -ne 86 ] || \
  fail 'pre-migration quiesce failure was misclassified as an unsafe migration partial'
cmp -s "$TEST_ROOT/current-images.before" "$SERVER_DIR/releases/current-images.env" || \
  fail 'pre-migration quiesce failure promoted candidate images'
cmp -s "$TEST_ROOT/routine-current.before" "$SERVER_DIR/releases/routine-current.env" || \
  fail 'pre-migration quiesce failure rewrote routine-current state'
[ ! -f "$SERVER_DIR/releases/routine-history/$quiesce_failure_invocation.migration-started.env" ] || \
  fail 'pre-migration quiesce failure wrote a migration-started marker'
grep -Fq 'ROLLBACK_STATUS=completed' \
  "$SERVER_DIR/releases/routine-history/$quiesce_failure_invocation.failed.env" || \
  fail 'pre-migration quiesce failure did not restore the previous runtime'

: > "$DOCKER_LOG"
backup_failure_invocation='migration-guard-backup-failure'
backup_failure_request="$(make_request "$backup_failure_invocation" 8888888888888888888888888888888888888888 c)"
set +e
FAKE_DATABASE_BACKUP_FAIL=true FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
  run_request "$backup_failure_request" > "$TEST_ROOT/backup-failure.log" 2>&1
backup_failure_status=$?
set -e
[ "$backup_failure_status" -ne 0 ] && [ "$backup_failure_status" -ne 86 ] || \
  fail 'post-quiesce backup failure was misclassified as an unsafe migration partial'
grep -Fq 'ROLLBACK_STATUS=completed' \
  "$SERVER_DIR/releases/routine-history/$backup_failure_invocation.failed.env" || \
  fail 'post-quiesce backup failure did not restore the previous runtime'
[ ! -f "$SERVER_DIR/releases/routine-history/$backup_failure_invocation.migration-started.env" ] || \
  fail 'post-quiesce backup failure wrote a migration-started marker'
if find "$SERVER_DIR/backups/postgres" -maxdepth 1 -type f -name '*.partial' -print -quit | grep -q .; then
  fail 'post-quiesce backup failure retained a partial database dump'
fi

for recovery_mode in unhealthy up-failed http-failed restart-unstable; do
  : > "$DOCKER_LOG"
  recovery_invocation="migration-guard-quiesce-recovery-$recovery_mode"
  recovery_request="$(make_request "$recovery_invocation" 6666666666666666666666666666666666666666 f)"
  set +e
  case "$recovery_mode" in
    unhealthy)
      FAKE_RUNTIME_QUIESCE_FAIL=true FAKE_RUNTIME_RECOVERY_UNHEALTHY=true \
        FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
        run_request "$recovery_request" > "$TEST_ROOT/recovery-$recovery_mode.log" 2>&1
      ;;
    up-failed)
      FAKE_RUNTIME_QUIESCE_FAIL=true FAKE_RUNTIME_RECOVERY_UP_FAIL=true \
        FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
        run_request "$recovery_request" > "$TEST_ROOT/recovery-$recovery_mode.log" 2>&1
      ;;
    http-failed)
      FAKE_RUNTIME_QUIESCE_FAIL=true FAKE_RUNTIME_RECOVERY_HTTP_FAIL=true \
        FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
        run_request "$recovery_request" > "$TEST_ROOT/recovery-$recovery_mode.log" 2>&1
      ;;
    restart-unstable)
      FAKE_RUNTIME_QUIESCE_FAIL=true FAKE_RUNTIME_RECOVERY_RESTART_UNSTABLE=true \
        FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
        run_request "$recovery_request" > "$TEST_ROOT/recovery-$recovery_mode.log" 2>&1
      ;;
  esac
  recovery_status=$?
  set -e
  [ "$recovery_status" -eq 86 ] || \
    fail "unverified runtime recovery was not frozen: mode=$recovery_mode status=$recovery_status"
  grep -Fq 'STATUS=blocked-partial' \
    "$SERVER_DIR/releases/routine-history/$recovery_invocation.failed.env" || \
    fail "unverified runtime recovery was not marked blocked-partial: mode=$recovery_mode"
  grep -Fq 'RESUME_ALLOWED=false' \
    "$SERVER_DIR/releases/routine-history/$recovery_invocation.failed.env" || \
    fail "unverified runtime recovery allowed resume: mode=$recovery_mode"
  cmp -s "$TEST_ROOT/current-images.before" "$SERVER_DIR/releases/current-images.env" || \
    fail "unverified runtime recovery promoted candidate images: mode=$recovery_mode"
  cmp -s "$TEST_ROOT/routine-current.before" "$SERVER_DIR/releases/routine-current.env" || \
    fail "unverified runtime recovery rewrote routine-current state: mode=$recovery_mode"
done

: > "$DOCKER_LOG"
marker_failure_invocation='migration-guard-marker-failure'
marker_failure_request="$(make_request "$marker_failure_invocation" 2222222222222222222222222222222222222222 b)"
set +e
FAKE_MIGRATION_MARKER_MODE=failure FAKE_MIGRATION_EXIT_CODE=0 \
  run_request "$marker_failure_request" > "$TEST_ROOT/marker-failure.log" 2>&1
marker_failure_status=$?
set -e
[ "$marker_failure_status" -eq 86 ] || fail 'compose zero without success marker was not blocked as partial'
cmp -s "$TEST_ROOT/current-images.before" "$SERVER_DIR/releases/current-images.env" || \
  fail 'missing marker promoted candidate images'
cmp -s "$TEST_ROOT/routine-current.before" "$SERVER_DIR/releases/routine-current.env" || \
  fail 'missing marker rewrote routine-current state'
grep -Fq 'up -d --no-deps' "$DOCKER_LOG" && fail 'missing marker updated a runtime service'
grep -Fq 'STATUS=blocked-partial' \
  "$SERVER_DIR/releases/routine-history/$marker_failure_invocation.failed.env" || \
  fail 'missing marker failure evidence was not marked blocked-partial'
grep -Fq 'ROLLBACK_STATUS=blocked-migration-started' \
  "$SERVER_DIR/releases/routine-history/$marker_failure_invocation.failed.env" || \
  fail 'missing marker failure evidence claimed an unsafe runtime rollback'
grep -Fq 'RESUME_ALLOWED=false' \
  "$SERVER_DIR/releases/routine-history/$marker_failure_invocation.failed.env" || \
  fail 'missing marker failure evidence allowed automatic resume'
[ -f "$SERVER_DIR/releases/routine-history/$marker_failure_invocation.migration-started.env" ] || \
  fail 'missing marker failure did not retain the durable migration-started state'
[ "$(sed -n '1p' "$MIGRATION_INVOCATION_FILE")" = "$marker_failure_invocation" ] || \
  fail 'compose did not receive the exact migration invocation id'

: > "$DOCKER_LOG"
set +e
FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 \
  run_request "$marker_failure_request" > "$TEST_ROOT/blocked-retry.log" 2>&1
blocked_retry_status=$?
set -e
[ "$blocked_retry_status" -eq 86 ] || fail 'unresolved migration state allowed automatic retry'
grep -Fq 'unresolved AICopilot migration state blocks automatic retry' \
  "$TEST_ROOT/blocked-retry.log" || fail 'blocked retry did not identify the unresolved migration state'
grep -Eq 'pull| stop | up ' "$DOCKER_LOG" && fail 'blocked retry mutated containers'
grep -Fq 'STATUS=blocked-partial' \
  "$SERVER_DIR/releases/routine-history/$marker_failure_invocation.failed.env" || \
  fail 'blocked retry weakened the original blocked-partial failure evidence'
rm -f "$SERVER_DIR/releases/routine-history/$marker_failure_invocation.migration-started.env"

: > "$DOCKER_LOG"
exit_failure_invocation='migration-guard-exit-failure'
exit_failure_request="$(make_request "$exit_failure_invocation" 3333333333333333333333333333333333333333 c)"
set +e
FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=17 \
  run_request "$exit_failure_request" > "$TEST_ROOT/exit-failure.log" 2>&1
exit_failure_status=$?
set -e
[ "$exit_failure_status" -eq 86 ] || fail 'nonzero migration exit was not blocked as partial'
cmp -s "$TEST_ROOT/current-images.before" "$SERVER_DIR/releases/current-images.env" || \
  fail 'nonzero migration exit promoted candidate images'
cmp -s "$TEST_ROOT/routine-current.before" "$SERVER_DIR/releases/routine-current.env" || \
  fail 'nonzero migration exit rewrote routine-current state'
grep -Fq 'up -d --no-deps' "$DOCKER_LOG" && fail 'nonzero migration exit updated a runtime service'
[ -f "$SERVER_DIR/releases/routine-history/$exit_failure_invocation.migration-started.env" ] || \
  fail 'nonzero migration exit did not retain the durable migration-started state'

printf 'AICopilot migration runner guard tests passed.\n'
