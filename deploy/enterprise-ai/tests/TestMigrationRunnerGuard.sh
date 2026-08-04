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
    else
      printf 'true|healthy\n'
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
      ps)
        while [ "${1:-}" = -a ] || [ "${1:-}" = -q ]; do shift; done
        printf 'container-%s\n' "${1:?service required}"
        exit 0
        ;;
      exec)
        if [[ "$*" == *pg_dump* ]]; then
          printf 'fake-postgres-dump\n'
        fi
        exit 0
        ;;
      up)
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
printf '200'
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
  local body="$TEST_ROOT/$invocation_id.body"
  local request="$TEST_ROOT/$invocation_id.env"
  local image_digest
  image_digest="$(printf '%064d' 0 | tr '0' "$digest_character")"
  {
    printf 'PROTOCOL=1\n'
    printf 'TARGET=AICopilot\n'
    printf 'INVOCATION_ID=%s\n' "$invocation_id"
    printf 'GIT_SHA=%s\n' "$sha"
    printf 'RELEASE_TAG=sha-%s\n' "$sha"
    printf 'SERVICES=httpapi,migration,dataworker,ragworker,web\n'
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
  PATH="$BIN_DIR:$PATH" \
  FAKE_DOCKER_LOG="$DOCKER_LOG" \
  FAKE_MIGRATION_INVOCATION_FILE="$MIGRATION_INVOCATION_FILE" \
  IIOT_RUNNER_HEALTH_ATTEMPTS=1 \
  IIOT_RUNNER_HEALTH_INTERVAL_SECONDS=0 \
    "$SERVER_DIR/runner/iiot-release-runner.sh" \
      --target aicopilot --expected-user "$(id -un)" --request-stdin < "$request"
}

success_invocation='migration-guard-success'
success_request="$(make_request "$success_invocation" 1111111111111111111111111111111111111111 a)"
FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=0 run_request "$success_request"
grep -Fq "INVOCATION_ID=$success_invocation" "$SERVER_DIR/releases/routine-current.env" || \
  fail 'valid migration proof did not commit current state'

cp "$SERVER_DIR/releases/current-images.env" "$TEST_ROOT/current-images.before"
cp "$SERVER_DIR/releases/routine-current.env" "$TEST_ROOT/routine-current.before"
: > "$DOCKER_LOG"
marker_failure_invocation='migration-guard-marker-failure'
marker_failure_request="$(make_request "$marker_failure_invocation" 2222222222222222222222222222222222222222 b)"
set +e
FAKE_MIGRATION_MARKER_MODE=failure FAKE_MIGRATION_EXIT_CODE=0 \
  run_request "$marker_failure_request" > "$TEST_ROOT/marker-failure.log" 2>&1
marker_failure_status=$?
set -e
[ "$marker_failure_status" -ne 0 ] || fail 'compose zero without success marker was accepted'
cmp -s "$TEST_ROOT/current-images.before" "$SERVER_DIR/releases/current-images.env" || \
  fail 'missing marker promoted candidate images'
cmp -s "$TEST_ROOT/routine-current.before" "$SERVER_DIR/releases/routine-current.env" || \
  fail 'missing marker rewrote routine-current state'
grep -Fq 'up -d --no-deps' "$DOCKER_LOG" && fail 'missing marker updated a runtime service'
grep -Fq 'ROLLBACK_STATUS=not-required' \
  "$SERVER_DIR/releases/routine-history/$marker_failure_invocation.failed.env" || \
  fail 'missing marker failure evidence did not preserve the old runtime'
[ "$(sed -n '1p' "$MIGRATION_INVOCATION_FILE")" = "$marker_failure_invocation" ] || \
  fail 'compose did not receive the exact migration invocation id'

: > "$DOCKER_LOG"
exit_failure_invocation='migration-guard-exit-failure'
exit_failure_request="$(make_request "$exit_failure_invocation" 3333333333333333333333333333333333333333 c)"
set +e
FAKE_MIGRATION_MARKER_MODE=success FAKE_MIGRATION_EXIT_CODE=17 \
  run_request "$exit_failure_request" > "$TEST_ROOT/exit-failure.log" 2>&1
exit_failure_status=$?
set -e
[ "$exit_failure_status" -ne 0 ] || fail 'nonzero migration container exit code was accepted'
cmp -s "$TEST_ROOT/current-images.before" "$SERVER_DIR/releases/current-images.env" || \
  fail 'nonzero migration exit promoted candidate images'
cmp -s "$TEST_ROOT/routine-current.before" "$SERVER_DIR/releases/routine-current.env" || \
  fail 'nonzero migration exit rewrote routine-current state'
grep -Fq 'up -d --no-deps' "$DOCKER_LOG" && fail 'nonzero migration exit updated a runtime service'

printf 'AICopilot migration runner guard tests passed.\n'
