#!/usr/bin/env bash
set -euo pipefail

printf 'NON_PRODUCTION_MECHANISM_TEST productionEligible=false suite=aicopilot-deployment-behavior\n'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel)"
SOURCE_BASE_SHA="$(git -C "$SOURCE_ROOT" rev-parse HEAD)"
TEST_ROOT="$(cd "$(mktemp -d "${TMPDIR:-/tmp}/aicopilot-deploy-tests.XXXXXX")" && pwd -P)"
BIN_DIR="$TEST_ROOT/bin"
REMOTE_DIR="$TEST_ROOT/remote"
DATA_DIR="$TEST_ROOT/data"
DOCKER_LOG="$TEST_ROOT/docker.log"
SSH_LOG="$TEST_ROOT/ssh.log"
REPO_DIR="$TEST_ROOT/repo"
ORIGIN_DIR="$TEST_ROOT/origin.git"

cleanup() {
  cleanup_status=$?
  trap - EXIT
  if [ "$cleanup_status" -eq 0 ] && [ "${KEEP_TEST_ROOT:-false}" != true ]; then
    rm -rf "$TEST_ROOT"
  else
    printf 'AICopilot deployment behavior fixtures retained: %s\n' "$TEST_ROOT" >&2
  fi
  exit "$cleanup_status"
}
trap cleanup EXIT

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

assert_eq() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [ "$expected" = "$actual" ] || fail "$label expected=$expected actual=$actual"
}

assert_file_contains() {
  local path="$1"
  local text="$2"
  grep -Fq "$text" "$path" || fail "$path does not contain: $text"
}

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

mkdir -p "$BIN_DIR" "$REMOTE_DIR/releases/history" "$DATA_DIR"
git clone --quiet "$SOURCE_ROOT" "$REPO_DIR"
git -C "$REPO_DIR" checkout --quiet -B main "$SOURCE_BASE_SHA"

# The production entry deliberately refuses a dirty source tree. Create a clean,
# local-only commit containing the deployment files under test so this regression
# exercises current uncommitted hardening without committing or pushing the user's
# concurrent business changes.
rm -rf "$REPO_DIR/deploy/enterprise-ai"
cp -R "$SOURCE_ROOT/deploy/enterprise-ai" "$REPO_DIR/deploy/"
git -C "$REPO_DIR" add deploy/enterprise-ai
if ! git -C "$REPO_DIR" diff --cached --quiet; then
  git -C "$REPO_DIR" \
    -c user.name='AICopilot deployment test' \
    -c user.email='aicopilot-deployment-test@invalid.example' \
    commit --quiet -m 'test: snapshot current deployment files'
fi
SOURCE_SHA="$(git -C "$REPO_DIR" rev-parse HEAD)"
git clone --quiet --bare "$REPO_DIR" "$ORIGIN_DIR"
git -C "$REPO_DIR" remote set-url origin "$ORIGIN_DIR"

cat > "$BIN_DIR/dotnet" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf 'dotnet %s\n' "$*" >> "${FAKE_DOCKER_LOG:?}"
if [ -n "${FAKE_DOTNET_EXIT:-}" ]; then
  exit "$FAKE_DOTNET_EXIT"
fi
output=""
while [ "$#" -gt 0 ]; do
  if [ "$1" = "-o" ]; then
    shift
    output="${1:-}"
  fi
  shift || true
done
[ -z "$output" ] || mkdir -p "$output"
EOF

cat > "$BIN_DIR/docker" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
full="$*"
printf 'docker %s\n' "$full" >> "${FAKE_DOCKER_LOG:?}"
if [ -n "${FAKE_DOCKER_FAIL_MATCH:-}" ] && [[ "$full" == *"$FAKE_DOCKER_FAIL_MATCH"* ]]; then
  exit "${FAKE_DOCKER_FAIL_EXIT:-42}"
fi
if [ -n "${FAKE_DOCKER_DELAY_MATCH:-}" ] && [[ "$full" == *"$FAKE_DOCKER_DELAY_MATCH"* ]]; then
  sleep "${FAKE_DOCKER_DELAY_SECONDS:-3}"
fi
if [ "${FAKE_INFRA_TAG_DRIFT:-false}" = true ] &&
   [[ "$full" == compose*" up "* ]] && [[ "$full" == *postgres* ]] &&
   [ ! -f "${FAKE_REMOTE_DIR:?}/.fake-infra-tag-drift-active" ]; then
  : > "${FAKE_REMOTE_DIR:?}/.fake-infra-tag-drift-active"
fi
case "$full" in
  "buildx version"*) printf 'github.com/docker/buildx fake\n'; exit 0 ;;
  buildx\ build*) exit 0 ;;
  buildx\ imagetools\ inspect*) printf 'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n'; exit 0 ;;
  inspect*'.Config.Image'*)
    container_id="${full##* }"
    case "$container_id" in
      *fake-postgres) key=POSTGRES_IMAGE ;;
      *fake-eventbus) key=RABBITMQ_IMAGE ;;
      *fake-qdrant) key=QDRANT_IMAGE ;;
      *aicopilot-httpapi) key=AICOPILOT_HTTPAPI_IMAGE ;;
      *aicopilot-dataworker) key=AICOPILOT_DATAWORKER_IMAGE ;;
      *aicopilot-ragworker) key=AICOPILOT_RAGWORKER_IMAGE ;;
      *) key=AICOPILOT_WEBUI_IMAGE ;;
    esac
    value="${!key:-}"
    [ -n "$value" ] || value="$(sed -n "s/^${key}=//p" "${FAKE_REMOTE_DIR:?}/.env" | tail -n 1)"
    if [ "${FAKE_INFRA_TAG_DRIFT:-false}" = true ] &&
       [ -f "${FAKE_REMOTE_DIR:?}/.fake-infra-tag-drift-active" ] &&
       [[ "$key" == POSTGRES_IMAGE || "$key" == RABBITMQ_IMAGE || "$key" == QDRANT_IMAGE ]] &&
       [[ "$value" == *@sha256:* ]]; then
      printf 'frozen-infra-recovery %s=%s\n' "$key" "$value" >> "${FAKE_DOCKER_LOG:?}"
    fi
    printf '%s\n' "$value"
    exit 0
    ;;
  inspect*'.RepoDigests'*)
    printf 'container objects do not expose RepoDigests in this fake\n' >&2
    exit 91
    ;;
  image\ inspect*'.RepoDigests'*)
    image_id="${full##* }"
    printf 'registry.other.invalid/mirror/not-the-configured-repository@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n'
    case "$image_id" in
      sha256:1111111111111111111111111111111111111111111111111111111111111111)
        printf 'registry.factory.internal:5000/enterprise-ai/base-postgres@sha256:1111111111111111111111111111111111111111111111111111111111111111\n'
        ;;
      sha256:2222222222222222222222222222222222222222222222222222222222222222)
        printf 'registry.factory.internal:5000/enterprise-ai/base-rabbitmq@sha256:2222222222222222222222222222222222222222222222222222222222222222\n'
        ;;
      sha256:3333333333333333333333333333333333333333333333333333333333333333)
        printf 'registry.factory.internal:5000/enterprise-ai/base-qdrant@sha256:3333333333333333333333333333333333333333333333333333333333333333\n'
        ;;
      sha256:9999999999999999999999999999999999999999999999999999999999999999)
        printf 'registry.factory.internal:5000/enterprise-ai/base-postgres@sha256:9999999999999999999999999999999999999999999999999999999999999999\n'
        printf 'registry.factory.internal:5000/enterprise-ai/base-rabbitmq@sha256:9999999999999999999999999999999999999999999999999999999999999999\n'
        printf 'registry.factory.internal:5000/enterprise-ai/base-qdrant@sha256:9999999999999999999999999999999999999999999999999999999999999999\n'
        ;;
      *) exit 1 ;;
    esac
    exit 0
    ;;
  inspect*'{{.Image}}'*)
    container_id="${full##* }"
    case "$container_id" in
      *fake-postgres) key=POSTGRES_IMAGE; old_digest=1111111111111111111111111111111111111111111111111111111111111111 ;;
      *fake-eventbus) key=RABBITMQ_IMAGE; old_digest=2222222222222222222222222222222222222222222222222222222222222222 ;;
      *fake-qdrant) key=QDRANT_IMAGE; old_digest=3333333333333333333333333333333333333333333333333333333333333333 ;;
      *) printf 'sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n'; exit 0 ;;
    esac
    configured_ref="${!key:-}"
    [ -n "$configured_ref" ] || configured_ref="$(sed -n "s/^${key}=//p" "${FAKE_REMOTE_DIR:?}/.env" | tail -n 1)"
    if [ "${FAKE_INFRA_TAG_DRIFT:-false}" = true ] &&
       [ -f "${FAKE_REMOTE_DIR:?}/.fake-infra-tag-drift-active" ] &&
       [[ "$configured_ref" != *@sha256:* ]]; then
      printf 'sha256:%s\n' 9999999999999999999999999999999999999999999999999999999999999999
    else
      printf 'sha256:%s\n' "$old_digest"
    fi
    exit 0
    ;;
  inspect*'.State.Running'*)
    container_id="${full##* }"
    if [ -n "${FAKE_UNHEALTHY_SERVICE:-}" ] && [[ "$container_id" == *"$FAKE_UNHEALTHY_SERVICE"* ]]; then
      printf 'true|true|false|1|none\n'
    elif [[ "$container_id" == *aicopilot-webui* ]]; then
      printf 'true|false|false|0|healthy\n'
    else
      printf 'true|false|false|0|none\n'
    fi
    exit 0
    ;;
  inspect*'-f '*|inspect*'--format '*) printf 'healthy\n'; exit 0 ;;
  compose*' ps -q '*) service="${full##* }"; printf 'fake-%s\n' "$service"; exit 0 ;;
  compose*psql*)
    printf 'aigateway.language_models|0|0\nrag.embedding_models|0|0\n'
    exit 0
    ;;
  compose*' exec -T aicopilot-webui '*)
    printf 'AICopilot web container non-root attestation passed: uid=1000\n'
    exit 0
    ;;
  system\ df*) printf 'TYPE TOTAL ACTIVE SIZE RECLAIMABLE\n'; exit 0 ;;
  image\ ls*) exit 0 ;;
  ps*) exit 0 ;;
esac
exit 0
EOF

cat > "$BIN_DIR/curl" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
full="$*"
if [ "${FAKE_CURL_FAIL_ALWAYS:-false}" = true ]; then
  exit "${FAKE_CURL_FAIL_EXIT:-58}"
fi
if [ -n "${FAKE_CURL_FAIL_COUNT:-}" ] && { [ -z "${FAKE_CURL_FAIL_MATCH:-}" ] || [[ "$full" == *"$FAKE_CURL_FAIL_MATCH"* ]]; }; then
  count_file="${FAKE_REMOTE_DIR:?}/.fake-curl-failure-count"
  current_count="$(sed -n '1p' "$count_file" 2>/dev/null || printf '0')"
  if [ "$current_count" -lt "$FAKE_CURL_FAIL_COUNT" ]; then
    printf '%s\n' "$((current_count + 1))" > "$count_file"
    exit "${FAKE_CURL_FAIL_EXIT:-58}"
  fi
fi
if [[ "$full" == *"--head"* ]]; then
  cat <<'HEADERS'
HTTP/1.1 200 OK
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Permissions-Policy: camera=()
Content-Security-Policy: default-src 'self'; frame-ancestors 'none'

HEADERS
elif [[ "$full" == *"--write-out"* ]]; then
  printf '200'
else
  printf '{}\n'
fi
EOF

cat > "$BIN_DIR/mv" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
full="$*"
if [ -n "${FAKE_MV_FAIL_MATCH:-}" ] && [[ "$full" == *"$FAKE_MV_FAIL_MATCH"* ]]; then
  exit "${FAKE_MV_FAIL_EXIT:-55}"
fi
if [ -n "${FAKE_MV_DELAY_MATCH:-}" ] && [[ "$full" == *"$FAKE_MV_DELAY_MATCH"* ]]; then
  sleep "${FAKE_MV_DELAY_SECONDS:-3}"
fi
exec /bin/mv "$@"
EOF

cat > "$BIN_DIR/cp" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
full="$*"
source_arg=""
for arg in "$@"; do
  case "$arg" in -*) ;; *) source_arg="$arg"; break ;; esac
done
if [ -n "${FAKE_CP_FAIL_MATCH:-}" ] && [[ "$source_arg" == *"$FAKE_CP_FAIL_MATCH"* ]]; then
  exit "${FAKE_CP_FAIL_EXIT:-56}"
fi
exec /bin/cp "$@"
EOF

cat > "$BIN_DIR/ssh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
printf 'ssh %s\n' "$*" >> "${FAKE_SSH_LOG:?}"
while [ "$#" -gt 0 ]; do
  case "$1" in
    -o) shift 2 ;;
    *) break ;;
  esac
done
[ "$#" -ge 2 ] || exit 64
target="$1"
shift
remote_command="$1"
shift || true
if [ -n "${FAKE_REMOTE_PLAN_DIGEST_OVERRIDE:-}" ] && [[ "$remote_command" == *"DEPLOY_TRIGGERED_BY=local"* ]]; then
  remote_command="$(printf '%s' "$remote_command" | sed -E "s/IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST='[0-9a-fA-F]+'/IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST='${FAKE_REMOTE_PLAN_DIGEST_OVERRIDE}'/")"
fi
export PATH="${FAKE_BIN_DIR:?}:$PATH"
export POST_RELEASE_DATA_PATH="${FAKE_DATA_DIR:?}"
export POST_RELEASE_CLEANUP_LOCK_FILE="${FAKE_REMOTE_DIR:?}/.locks/shared-cleanup.lock"
export POST_RELEASE_CONTAINERD_ROOT="${FAKE_REMOTE_DIR:?}/containerd"
export POST_RELEASE_HARBOR_GC_ENABLED=0
if [ -n "${FAKE_SSH_ACTIVE_UNKNOWN_MATCH:-}" ] &&
   [[ "$remote_command" == *"$FAKE_SSH_ACTIVE_UNKNOWN_MATCH"* ]] &&
   [ ! -f "${FAKE_REMOTE_DIR:?}/.fake-ssh-active-unknown" ]; then
  token="$(printf '%s\n' "$remote_command" | sed -n "s/.*DEPLOY_LOCK_TOKEN='\([^']*\)'.*/\1/p")"
  deadline="$(printf '%s\n' "$remote_command" | sed -n "s/.*DEPLOY_SERVER_DEADLINE_EPOCH='\([^']*\)'.*/\1/p")"
  mkdir -p "${FAKE_REMOTE_DIR:?}/releases/invocations"
  {
    printf 'DEPLOY_STATUS=active\n'
    printf 'DEPLOY_EXIT_CODE=0\n'
    printf 'DEPLOY_LOCK_TOKEN=%s\n' "$token"
    printf 'DEPLOY_INVOCATION_ID=fake-active-unknown\n'
    printf 'DEPLOY_SERVER_DEADLINE_EPOCH=%s\n' "$deadline"
  } > "${FAKE_REMOTE_DIR:?}/releases/invocations/$token.env"
  : > "${FAKE_REMOTE_DIR:?}/.fake-ssh-active-unknown"
  exit 255
fi
if [ -n "${FAKE_SSH_ACK_LOSS_MATCH:-}" ] &&
   [[ "$remote_command" == *"$FAKE_SSH_ACK_LOSS_MATCH"* ]] &&
   [ ! -f "${FAKE_REMOTE_DIR:?}/.fake-ssh-ack-lost" ]; then
  bash -c "$remote_command"
  command_status=$?
  [ "$command_status" -eq 0 ] || exit "$command_status"
  : > "${FAKE_REMOTE_DIR:?}/.fake-ssh-ack-lost"
  exit 255
fi
bash -c "$remote_command"
EOF
chmod +x "$BIN_DIR/dotnet" "$BIN_DIR/docker" "$BIN_DIR/curl" "$BIN_DIR/mv" "$BIN_DIR/cp" "$BIN_DIR/ssh"

cat > "$REMOTE_DIR/.env" <<'EOF'
COMPOSE_PROJECT_NAME=enterprise-ai-test
AICOPILOT_PUBLIC_URL=http://aicopilot.factory.internal:82
CLOUD_PLATFORM_URL=http://cloud.factory.internal:81
POSTGRES_USER=aicopilot
POSTGRES_DB=aicopilot
POSTGRES_PASSWORD=PgStrongSecretValue1234
RABBITMQ_PASSWORD=RbStrongSecretValue1234
QDRANT_KEY=QdStrongSecretValue1234
AICOPILOT_BOOTSTRAP_ADMIN_PASSWORD=AdminStrong1234
AICOPILOT_API_KEY_ENCRYPTION_KEY=EncryptionKeyValue01234567890123456789
AICOPILOT_JWT_SECRET_KEY=JwtSecretValue012345678901234567890123456789012345678901234567890123
CLOUD_READONLY_MODE=Disabled
CLOUD_READONLY_REAL_ENABLED=false
CLOUD_READONLY_REAL_ALLOW_PRODUCTION_READ=false
CLOUD_AI_READ_ENABLED=false
CLOUD_AI_READ_BASE_URL=http://cloud.factory.internal:81
CLOUD_IDENTITY_STATUS_ENABLED=false
CLOUD_IDENTITY_STATUS_BASE_URL=http://cloud.factory.internal:81
DATA_ANALYSIS_CLOUD_READONLY_ENABLED=false
AICOPILOT_MODEL_SMOKE_ENABLED=false
AICOPILOT_MODEL_SMOKE_BASE_URL=http://model.factory.internal:40034/v1
AICOPILOT_RUNTIME_STABILITY_SECONDS=0
CLOUD_OIDC_ENABLED=true
CLOUD_OIDC_ISSUER=http://cloud.factory.internal:81
ALLOW_INTRANET_HTTP_OIDC=true
CLOUD_OIDC_REQUIRE_HTTPS_METADATA=false
POSTGRES_IMAGE=registry.factory.internal:5000/enterprise-ai/base-postgres:17.6
RABBITMQ_IMAGE=registry.factory.internal:5000/enterprise-ai/base-rabbitmq:4.2-management
QDRANT_IMAGE=registry.factory.internal:5000/enterprise-ai/base-qdrant:v1.15.5
AICOPILOT_HTTPAPI_IMAGE=registry.factory.internal:5000/enterprise-ai/aicopilot-httpapi:sha-1111111
AICOPILOT_MIGRATION_IMAGE=registry.factory.internal:5000/enterprise-ai/aicopilot-migration:sha-1111111
AICOPILOT_DATAWORKER_IMAGE=registry.factory.internal:5000/enterprise-ai/aicopilot-dataworker:sha-1111111
AICOPILOT_RAGWORKER_IMAGE=registry.factory.internal:5000/enterprise-ai/aicopilot-ragworker:sha-1111111
AICOPILOT_WEBUI_IMAGE=registry.factory.internal:5000/enterprise-ai/aicopilot-webui:sha-1111111
EOF
chmod 600 "$REMOTE_DIR/.env"
printf 'old compose\n' > "$REMOTE_DIR/docker-compose.yaml"

export PATH="$BIN_DIR:$PATH"
export FAKE_BIN_DIR="$BIN_DIR"
export FAKE_REMOTE_DIR="$REMOTE_DIR"
export FAKE_DATA_DIR="$DATA_DIR"
export FAKE_DOCKER_LOG="$DOCKER_LOG"
export FAKE_SSH_LOG="$SSH_LOG"

WORKSPACE_PLAN_DIGEST="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
WORKSPACE_PROFILE_DIGEST="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
WORKSPACE_PLAN_FILE="$TEST_ROOT/workspace-plan.json"
cat > "$WORKSPACE_PLAN_FILE" <<EOF
{"schemaVersion":1,"runId":"deployment-behavior-invocation","mode":"check-candidate","target":"AICopilot","fullSha":"$SOURCE_SHA","services":["web"],"all":false,"profileDigest":"$WORKSPACE_PROFILE_DIGEST","branch":"main","remote":"origin","remoteHeadSha":"$SOURCE_SHA","requireRemoteTip":true,"remoteVerified":true,"worktreeClean":true}
EOF
WORKSPACE_PLAN_DIGEST="$(sha256_file "$WORKSPACE_PLAN_FILE")"
WORKER_PLAN_FILE="$TEST_ROOT/workspace-worker-plan.json"
cat > "$WORKER_PLAN_FILE" <<EOF
{"schemaVersion":1,"runId":"deployment-behavior-worker","mode":"check-candidate","target":"AICopilot","fullSha":"$SOURCE_SHA","services":["migration","dataworker"],"all":false,"profileDigest":"$WORKSPACE_PROFILE_DIGEST","branch":"main","remote":"origin","remoteHeadSha":"$SOURCE_SHA","requireRemoteTip":true,"remoteVerified":true,"worktreeClean":true}
EOF
WORKER_PLAN_DIGEST="$(sha256_file "$WORKER_PLAN_FILE")"
ALL_PLAN_FILE="$TEST_ROOT/workspace-all-plan.json"
cat > "$ALL_PLAN_FILE" <<EOF
{"schemaVersion":1,"runId":"deployment-behavior-all","mode":"check-candidate","target":"AICopilot","fullSha":"$SOURCE_SHA","services":["httpapi","migration","dataworker","ragworker","web"],"all":true,"profileDigest":"$WORKSPACE_PROFILE_DIGEST","branch":"main","remote":"origin","remoteHeadSha":"$SOURCE_SHA","requireRemoteTip":true,"remoteVerified":true,"worktreeClean":true}
EOF
ALL_PLAN_DIGEST="$(sha256_file "$ALL_PLAN_FILE")"

run_local_release() {
  IIOT_WORKSPACE_DEPLOY_ENTRYPOINT="${TEST_WORKSPACE_ENTRYPOINT-1}" \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID="${TEST_WORKSPACE_INVOCATION_ID-deployment-behavior-invocation}" \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="${TEST_WORKSPACE_EXPECTED_SHA-$SOURCE_SHA}" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="${TEST_WORKSPACE_PLAN_DIGEST-$WORKSPACE_PLAN_DIGEST}" \
  IIOT_WORKSPACE_DEPLOY_PLAN_FILE="${TEST_WORKSPACE_PLAN_FILE-$WORKSPACE_PLAN_FILE}" \
  IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST="${TEST_WORKSPACE_PROFILE_DIGEST-$WORKSPACE_PROFILE_DIGEST}" \
  REGISTRY=registry.factory.internal:5000 \
  HARBOR_PROJECT=enterprise-ai \
  CLOUD_PLATFORM_URL=http://cloud.factory.internal:81 \
  GIT_TIMEOUT_SECONDS=211 \
  SYNC_TIMEOUT_SECONDS=212 \
  SSH_TIMEOUT_SECONDS="${TEST_SSH_TIMEOUT_SECONDS-213}" \
  LOCK_RESERVATION_TTL_SECONDS="${TEST_LOCK_RESERVATION_TTL_SECONDS-}" \
  RECONCILE_TIMEOUT_SECONDS="${TEST_RECONCILE_TIMEOUT_SECONDS-4}" \
  RECONCILE_INTERVAL_SECONDS=1 \
  RECONCILE_QUERY_TIMEOUT_SECONDS="${TEST_RECONCILE_QUERY_TIMEOUT_SECONDS-10}" \
  BUILD_TIMEOUT_SECONDS=214 \
  HARBOR_TIMEOUT_SECONDS=215 \
    "$REPO_DIR/deploy/enterprise-ai/local-release.sh" \
      --services "${TEST_SERVICES-web}" \
      --ssh-target fake-host \
      --remote-dir "$REMOTE_DIR"
}

printf 'TEST malicious arithmetic payloads are rejected without command execution\n'
ARITHMETIC_MARKER="$TEST_ROOT/arithmetic-payload-executed"
malicious_payload='x[$(touch '"$ARITHMETIC_MARKER"')]'
malicious_token='bad$(touch-arithmetic-marker)'
set +e
TEST_SSH_TIMEOUT_SECONDS="$malicious_payload" run_local_release > "$TEST_ROOT/local-arithmetic-payload.log" 2>&1
local_arithmetic_status=$?
bash -c '. "$1"; release_acquire_lock "$2" safe-token aicopilot-release reserved test "$3"' \
  bash "$REPO_DIR/deploy/enterprise-ai/scripts/release-common.sh" "$TEST_ROOT/malicious-lock.d" "$malicious_payload" \
  > "$TEST_ROOT/common-arithmetic-payload.log" 2>&1
common_arithmetic_status=$?
"$REPO_DIR/deploy/enterprise-ai/scripts/install-support-release.sh" \
  "$TEST_ROOT/install-target" "$TEST_ROOT/install-staging" safe-token \
  aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
  "$malicious_payload" > "$TEST_ROOT/installer-arithmetic-payload.log" 2>&1
installer_arithmetic_status=$?
bash -c '. "$1"; release_acquire_lock "$2" "$3" aicopilot-release reserved test 900' \
  bash "$REPO_DIR/deploy/enterprise-ai/scripts/release-common.sh" "$TEST_ROOT/malicious-token-lock.d" "$malicious_token" \
  > "$TEST_ROOT/common-token-payload.log" 2>&1
common_token_status=$?
"$REPO_DIR/deploy/enterprise-ai/scripts/install-support-release.sh" \
  "$TEST_ROOT/install-target" "$TEST_ROOT/install-staging" "$malicious_token" \
  aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
  900 > "$TEST_ROOT/installer-token-payload.log" 2>&1
installer_token_status=$?
set -e
assert_eq 64 "$local_arithmetic_status" "local malicious arithmetic payload exit code"
assert_eq 64 "$common_arithmetic_status" "release-common malicious TTL exit code"
assert_eq 64 "$installer_arithmetic_status" "support installer malicious TTL exit code"
assert_eq 64 "$common_token_status" "release-common malicious token exit code"
assert_eq 64 "$installer_token_status" "support installer malicious token exit code"
[ ! -e "$ARITHMETIC_MARKER" ] || fail "malicious arithmetic payload executed a command"
[ ! -d "$TEST_ROOT/malicious-lock.d" ] || fail "malicious TTL created a release lock"

printf 'TEST formal local release rejects a missing workspace marker\n'
set +e
TEST_WORKSPACE_ENTRYPOINT='' run_local_release > "$TEST_ROOT/missing-marker.log" 2>&1
missing_marker_status=$?
set -e
assert_eq 64 "$missing_marker_status" "missing workspace marker exit code"
assert_file_contains "$TEST_ROOT/missing-marker.log" "IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1"

printf 'TEST formal local release rejects a frozen SHA mismatch\n'
set +e
TEST_WORKSPACE_EXPECTED_SHA='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' run_local_release > "$TEST_ROOT/sha-mismatch.log" 2>&1
sha_mismatch_status=$?
set -e
assert_eq 64 "$sha_mismatch_status" "frozen SHA mismatch exit code"
assert_file_contains "$TEST_ROOT/sha-mismatch.log" "candidate SHA mismatch"

printf 'TEST clean local HEAD behind fresh origin/main tip is rejected\n'
ADVANCER_DIR="$TEST_ROOT/origin-advancer"
git clone --quiet "$ORIGIN_DIR" "$ADVANCER_DIR"
printf 'advance\n' > "$ADVANCER_DIR/remote-advance.txt"
git -C "$ADVANCER_DIR" add remote-advance.txt
git -C "$ADVANCER_DIR" -c user.name='AICopilot deployment test' -c user.email='aicopilot-deployment-test@invalid.example' commit --quiet -m 'test: advance remote tip'
git -C "$ADVANCER_DIR" push --quiet origin main
set +e
run_local_release > "$TEST_ROOT/behind-origin-main.log" 2>&1
behind_status=$?
set -e
assert_eq 64 "$behind_status" "behind origin/main exit code"
assert_file_contains "$TEST_ROOT/behind-origin-main.log" "origin/main tip moved"
git -C "$ORIGIN_DIR" update-ref refs/heads/main "$SOURCE_SHA"

printf 'TEST success path, digest binding and protected state\n'
set +e
(cd "$TEST_ROOT" && run_local_release) > "$TEST_ROOT/success.log" 2>&1
success_status=$?
set -e
assert_eq 0 "$success_status" "success deployment exit code"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_GIT_SHA=$SOURCE_SHA"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_CANDIDATE=true"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_INVOCATION_ID=deployment-behavior-invocation"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_EXPECTED_SHA=$SOURCE_SHA"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_PLAN_DIGEST=$WORKSPACE_PLAN_DIGEST"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_SERVICES_MANIFEST_DIGEST="
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_IMAGE_MANIFEST_DIGEST="
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_SUPPORT_DIGEST="
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_CONFIG_FINGERPRINT="
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_PROFILE_DIGEST=$WORKSPACE_PROFILE_DIGEST"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_RUNTIME_AICOPILOT_WEBUI_IMAGE_DIGEST=sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_INFRA_POSTGRES_IMAGE_REF=registry.factory.internal:5000/enterprise-ai/base-postgres@sha256:1111111111111111111111111111111111111111111111111111111111111111"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_INFRA_POSTGRES_RUNTIME_DIGEST=sha256:1111111111111111111111111111111111111111111111111111111111111111"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_INFRA_RABBITMQ_RUNTIME_DIGEST=sha256:2222222222222222222222222222222222222222222222222222222222222222"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_INFRA_QDRANT_RUNTIME_DIGEST=sha256:3333333333333333333333333333333333333333333333333333333333333333"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_MIGRATION_COMPLETED=false"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "AICOPILOT_WEBUI_IMAGE=registry.factory.internal:5000/enterprise-ai/aicopilot-webui@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
assert_file_contains "$REMOTE_DIR/releases/current-release.summary.md" 'Frozen candidate: `true`'
assert_file_contains "$REMOTE_DIR/releases/current-release.summary.md" 'Invocation id: `deployment-behavior-invocation`'
assert_file_contains "$REMOTE_DIR/.env" "POSTGRES_PASSWORD=PgStrongSecretValue1234"
cmp "$REPO_DIR/deploy/enterprise-ai/docker-compose.yaml" "$REMOTE_DIR/docker-compose.yaml" >/dev/null || fail "docker-compose.yaml was not synchronized"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "release lock remained after success"

printf 'TEST same SHA idempotency skips build and support sync\n'
: > "$DOCKER_LOG"
run_local_release > "$TEST_ROOT/idempotent.log" 2>&1
assert_file_contains "$TEST_ROOT/idempotent.log" "build/push/support sync skipped"
if grep -Fq 'buildx build' "$DOCKER_LOG"; then
  fail "same SHA idempotency rebuilt images"
fi
if grep -Fq 'network create' "$DOCKER_LOG"; then
  fail "read-only check-current created a Docker network"
fi
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "read-only check-current created a release lock"

printf 'TEST global secret/config fingerprint drift denies partial-service publication without recording secret values\n'
sed -i.bak 's/^QDRANT_KEY=.*/QDRANT_KEY=QdRotatedStrongSecretValue5678/' "$REMOTE_DIR/.env"
rm -f "$REMOTE_DIR/.env.bak"
: > "$DOCKER_LOG"
set +e
run_local_release > "$TEST_ROOT/config-fingerprint-change.log" 2>&1
config_drift_status=$?
set -e
assert_eq 67 "$config_drift_status" "partial-service config drift exit code"
assert_file_contains "$TEST_ROOT/config-fingerprint-change.log" "partial-service publication is denied"
if grep -Fq 'buildx build' "$DOCKER_LOG"; then
  fail "partial-service config drift built or published an image"
fi
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_CONFIG_FINGERPRINT="
if grep -Fq 'QdRotatedStrongSecretValue5678' "$REMOTE_DIR/releases/current-release.env" "$REMOTE_DIR/releases/current-release.summary.md"; then
  fail "release state recorded a secret value instead of only its fingerprint"
fi
TEST_SERVICES='httpapi,migration,dataworker,ragworker,web' \
TEST_WORKSPACE_PLAN_FILE="$ALL_PLAN_FILE" \
TEST_WORKSPACE_PLAN_DIGEST="$ALL_PLAN_DIGEST" \
TEST_WORKSPACE_INVOCATION_ID=deployment-behavior-all \
  run_local_release > "$TEST_ROOT/config-fingerprint-full-release.log" 2>&1
if grep -Fq 'QdRotatedStrongSecretValue5678' "$REMOTE_DIR/releases/current-release.env" "$REMOTE_DIR/releases/current-release.summary.md"; then
  fail "full config deployment recorded a secret value instead of only its fingerprint"
fi

printf 'TEST support install failure restores the complete previous support tree\n'
compose_hash_before="$(sha256_file "$REMOTE_DIR/docker-compose.yaml")"
support_manifest_hash_before="$(sha256_file "$REMOTE_DIR/.aicopilot-support-manifest.sha256")"
env_hash_before="$(sha256_file "$REMOTE_DIR/.env")"
set +e
AICOPILOT_FORCE_BUILD=true FAKE_MV_FAIL_MATCH='.aicopilot-stage.' FAKE_MV_FAIL_EXIT=55 \
  run_local_release > "$TEST_ROOT/support-install-rollback.log" 2>&1
support_install_status=$?
set -e
assert_eq 55 "$support_install_status" "support install failure exit code"
assert_eq "$compose_hash_before" "$(sha256_file "$REMOTE_DIR/docker-compose.yaml")" "compose after support install rollback"
assert_eq "$support_manifest_hash_before" "$(sha256_file "$REMOTE_DIR/.aicopilot-support-manifest.sha256")" "support manifest after install rollback"
assert_eq "$env_hash_before" "$(sha256_file "$REMOTE_DIR/.env")" "env after support install rollback"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "support install failure left a reservation"

printf 'TEST failed support restore durably blocks retries and returns unsafe-partial\n'
set +e
AICOPILOT_FORCE_BUILD=true \
FAKE_MV_FAIL_MATCH='.aicopilot-stage.' FAKE_MV_FAIL_EXIT=55 \
FAKE_CP_FAIL_MATCH='.support-backups/' FAKE_CP_FAIL_EXIT=56 \
  run_local_release > "$TEST_ROOT/support-restore-unsafe.log" 2>&1
support_restore_unsafe_status=$?
set -e
assert_eq 86 "$support_restore_unsafe_status" "support restore unsafe-partial exit code"
unsafe_support_marker="$(find "$REMOTE_DIR/.support-backups" -mindepth 2 -maxdepth 2 -type f -name UNSAFE_PARTIAL -print -quit)"
[ -f "$unsafe_support_marker" ] || fail "failed support restore did not persist an unsafe marker"
assert_file_contains "$unsafe_support_marker" "DEPLOY_STATUS=unsafe-partial"
set +e
AICOPILOT_FORCE_BUILD=true run_local_release > "$TEST_ROOT/support-restore-blocked-retry.log" 2>&1
support_blocked_retry_status=$?
set -e
assert_eq 86 "$support_blocked_retry_status" "support unsafe-partial retry gate exit code"
unsafe_support_backup="$(dirname "$unsafe_support_marker")"
unsafe_paths="$unsafe_support_backup/paths"
while IFS= read -r unsafe_relative_path; do
  [ -n "$unsafe_relative_path" ] || continue
  rm -f "$REMOTE_DIR/$unsafe_relative_path"
done < "$unsafe_paths"
if [ -d "$unsafe_support_backup/tree" ]; then
  while IFS= read -r unsafe_relative_path; do
    unsafe_relative_path="${unsafe_relative_path#./}"
    [ -n "$unsafe_relative_path" ] || continue
    mkdir -p "$(dirname "$REMOTE_DIR/$unsafe_relative_path")"
    /bin/cp -p "$unsafe_support_backup/tree/$unsafe_relative_path" "$REMOTE_DIR/$unsafe_relative_path"
  done < <(cd "$unsafe_support_backup/tree" && find . -type f -print | LC_ALL=C sort)
fi
if [ -f "$unsafe_support_backup/installed-manifest" ]; then
  /bin/cp -p "$unsafe_support_backup/installed-manifest" "$REMOTE_DIR/.aicopilot-support-manifest.sha256"
fi
if [ -f "$unsafe_support_backup/installed-digest" ]; then
  /bin/cp -p "$unsafe_support_backup/installed-digest" "$REMOTE_DIR/.aicopilot-support-manifest.digest"
fi
rm -f "$REMOTE_DIR/releases/blocked-release.env"
rm -rf "$REMOTE_DIR/.locks/release.lock.d" "$unsafe_support_backup"
bash -c '. "$1"; release_verify_sha256_manifest "$2" "$2/.aicopilot-support-manifest.sha256"' \
  bash "$REMOTE_DIR/scripts/release-common.sh" "$REMOTE_DIR"

printf 'TEST residual support backup and blocked lock fail closed when restore and marker persistence both fail\n'
set +e
AICOPILOT_FORCE_BUILD=true \
AICOPILOT_NON_PRODUCTION_MECHANISM_TEST=true \
AICOPILOT_TEST_FORCE_UNSAFE_MARKER_WRITE_FAILURE=true \
FAKE_MV_FAIL_MATCH='.aicopilot-stage.' FAKE_MV_FAIL_EXIT=55 \
FAKE_CP_FAIL_MATCH='.support-backups/' FAKE_CP_FAIL_EXIT=56 \
  run_local_release > "$TEST_ROOT/support-restore-marker-double-failure.log" 2>&1
double_failure_status=$?
set -e
assert_eq 86 "$double_failure_status" "support restore and marker double failure exit code"
[ ! -f "$REMOTE_DIR/releases/blocked-release.env" ] || fail "forced marker failure unexpectedly wrote blocked release state"
if find "$REMOTE_DIR/.support-backups" -type f -name UNSAFE_PARTIAL -print -quit | grep -q .; then
  fail "forced marker failure unexpectedly wrote an unsafe marker"
fi
double_failure_backup="$(find "$REMOTE_DIR/.support-backups" -mindepth 1 -maxdepth 1 -type d -print -quit)"
[ -d "$double_failure_backup" ] || fail "double failure did not retain its support backup"
assert_file_contains "$REMOTE_DIR/.locks/release.lock.d/state" "blocked"
double_failure_token="$(sed -n '1p' "$REMOTE_DIR/.locks/release.lock.d/token")"
set +e
AICOPILOT_FORCE_BUILD=true run_local_release > "$TEST_ROOT/support-residual-backup-retry.log" 2>&1
double_failure_retry_status=$?
set -e
assert_eq 86 "$double_failure_retry_status" "residual support backup retry gate exit code"
assert_file_contains "$TEST_ROOT/support-residual-backup-retry.log" "$double_failure_backup"
assert_file_contains "$REMOTE_DIR/.locks/release.lock.d/state" "blocked"
[ "$(sed -n '1p' "$REMOTE_DIR/.locks/release.lock.d/token")" = "$double_failure_token" ] || fail "blocked lock token changed during retry"
double_failure_paths="$double_failure_backup/paths"
while IFS= read -r double_relative_path; do
  [ -n "$double_relative_path" ] || continue
  rm -f "$REMOTE_DIR/$double_relative_path"
done < "$double_failure_paths"
if [ -d "$double_failure_backup/tree" ]; then
  while IFS= read -r double_relative_path; do
    double_relative_path="${double_relative_path#./}"
    [ -n "$double_relative_path" ] || continue
    mkdir -p "$(dirname "$REMOTE_DIR/$double_relative_path")"
    /bin/cp -p "$double_failure_backup/tree/$double_relative_path" "$REMOTE_DIR/$double_relative_path"
  done < <(cd "$double_failure_backup/tree" && find . -type f -print | LC_ALL=C sort)
fi
[ ! -f "$double_failure_backup/installed-manifest" ] || /bin/cp -p "$double_failure_backup/installed-manifest" "$REMOTE_DIR/.aicopilot-support-manifest.sha256"
[ ! -f "$double_failure_backup/installed-digest" ] || /bin/cp -p "$double_failure_backup/installed-digest" "$REMOTE_DIR/.aicopilot-support-manifest.digest"
rm -rf "$REMOTE_DIR/.locks/release.lock.d" "$double_failure_backup"
bash -c '. "$1"; release_verify_sha256_manifest "$2" "$2/.aicopilot-support-manifest.sha256"' \
  bash "$REMOTE_DIR/scripts/release-common.sh" "$REMOTE_DIR"

printf 'TEST lost support-install ACK queries and atomically cancels only this reservation\n'
rm -f "$REMOTE_DIR/.fake-ssh-ack-lost"
compose_hash_before="$(sha256_file "$REMOTE_DIR/docker-compose.yaml")"
set +e
AICOPILOT_FORCE_BUILD=true FAKE_SSH_ACK_LOSS_MATCH='install-support-release.sh' \
  run_local_release > "$TEST_ROOT/support-ack-loss.log" 2>&1
support_ack_status=$?
set -e
assert_eq 255 "$support_ack_status" "support install ACK loss exit code"
assert_eq "$compose_hash_before" "$(sha256_file "$REMOTE_DIR/docker-compose.yaml")" "compose after ACK-loss cancellation"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "ACK-loss cancellation left a reservation"
if find "$REMOTE_DIR/.support-backups" -mindepth 1 -maxdepth 1 -type d -print -quit 2>/dev/null | grep -q .; then
  fail "ACK-loss cancellation left a support backup"
fi

printf 'TEST lost final deploy ACK is reconciled from terminal invocation state\n'
rm -f "$REMOTE_DIR/.fake-ssh-ack-lost"
set +e
AICOPILOT_FORCE_BUILD=true FAKE_SSH_ACK_LOSS_MATCH='DEPLOY_TRIGGERED_BY=local' \
  run_local_release > "$TEST_ROOT/final-deploy-ack-loss.log" 2>&1
final_ack_status=$?
set -e
assert_eq 0 "$final_ack_status" "final deploy ACK-loss reconciliation exit code"
assert_file_contains "$TEST_ROOT/final-deploy-ack-loss.log" "remote invocation reconciled as succeeded"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "terminal ACK-loss reconciliation left a release lock"

printf 'TEST active or unknown remote invocation forbids cancellation and ordinary retry\n'
rm -f "$REMOTE_DIR/.fake-ssh-active-unknown"
set +e
AICOPILOT_FORCE_BUILD=true FAKE_SSH_ACTIVE_UNKNOWN_MATCH='DEPLOY_TRIGGERED_BY=local' \
TEST_RECONCILE_TIMEOUT_SECONDS=2 \
  run_local_release > "$TEST_ROOT/final-deploy-active-unknown.log" 2>&1
active_unknown_status=$?
set -e
assert_eq 87 "$active_unknown_status" "active unknown reconciliation exit code"
assert_file_contains "$TEST_ROOT/final-deploy-active-unknown.log" "automatic cancellation and retry are forbidden"
[ -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "active unknown outcome was automatically cancelled"
active_unknown_token="$(sed -n '1p' "$REMOTE_DIR/.locks/release.lock.d/token")"
[ -n "$active_unknown_token" ] || fail "active unknown outcome lost its reservation token"
rm -f "$REMOTE_DIR/releases/invocations/$active_unknown_token.env"
"$REMOTE_DIR/scripts/cancel-support-reservation.sh" "$REMOTE_DIR" "$active_unknown_token" > "$TEST_ROOT/active-unknown-manual-cancel.log" 2>&1
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "manual cleanup after active unknown outcome left a reservation"

printf 'TEST support installer and cancellation cannot mutate concurrently\n'
AICOPILOT_FORCE_BUILD=true FAKE_MV_DELAY_MATCH='.aicopilot-stage.' FAKE_MV_DELAY_SECONDS=2 \
  run_local_release > "$TEST_ROOT/support-install-cancel-race.log" 2>&1 &
install_race_pid=$!
for _ in $(seq 1 100); do
  if [ "$(sed -n '1p' "$REMOTE_DIR/.locks/release.lock.d/phase" 2>/dev/null || true)" = support-installing ]; then
    break
  fi
  sleep 0.1
done
install_race_token="$(sed -n '1p' "$REMOTE_DIR/.locks/release.lock.d/token" 2>/dev/null || true)"
[ -n "$install_race_token" ] || fail "support install race did not expose its active token"
set +e
"$REMOTE_DIR/scripts/cancel-support-reservation.sh" "$REMOTE_DIR" "$install_race_token" > "$TEST_ROOT/support-install-cancel-attempt.log" 2>&1
install_cancel_status=$?
wait "$install_race_pid"
install_race_status=$?
set -e
assert_eq 3 "$install_cancel_status" "cancel during support installation exit code"
assert_eq 0 "$install_race_status" "support installation after rejected cancellation"

printf 'TEST stale transition recovery and old-token cancellation preserve a new reservation\n'
. "$REPO_DIR/deploy/enterprise-ai/scripts/release-common.sh"
transition_lock="$TEST_ROOT/transition-lock.d"
release_acquire_lock "$transition_lock" transition-token aicopilot-release reserved support-ready 900
mkdir "$transition_lock/.transition.d"
printf 'transition-token\n' > "$transition_lock/.transition.d/token"
printf '99999999\n' > "$transition_lock/.transition.d/pid"
printf 'dead-process\n' > "$transition_lock/.transition.d/process-start"
printf 'test-owner\n' > "$transition_lock/.transition.d/owner"
printf '1\n' > "$transition_lock/.transition.d/expires-at-epoch"
release_begin_lock_transition "$transition_lock" transition-token reserved
assert_file_contains "$transition_lock/.transition.d/owner" "@"
release_end_lock_transition "$transition_lock" transition-token
release_unlock "$transition_lock" transition-token
live_expired_lock="$TEST_ROOT/live-expired-transition-lock.d"
release_acquire_lock "$live_expired_lock" live-transition-token aicopilot-release reserved support-ready 900
release_begin_lock_transition "$live_expired_lock" live-transition-token reserved
printf '1\n' > "$live_expired_lock/expires-at-epoch"
printf '1\n' > "$live_expired_lock/.transition.d/expires-at-epoch"
set +e
release_acquire_lock "$live_expired_lock" replacement-token aicopilot-release reserved support-ready 900 > "$TEST_ROOT/live-expired-transition.log" 2>&1
live_expired_status=$?
set -e
assert_eq 75 "$live_expired_status" "live expired transition lock contention exit code"
assert_file_contains "$live_expired_lock/token" "live-transition-token"
assert_file_contains "$live_expired_lock/.transition.d/token" "live-transition-token"
release_end_lock_transition "$live_expired_lock" live-transition-token
release_unlock "$live_expired_lock" live-transition-token
cancel_root="$TEST_ROOT/cancel-root"
mkdir -p "$cancel_root/scripts" "$cancel_root/.locks"
cp -p "$REPO_DIR/deploy/enterprise-ai/scripts/release-common.sh" "$cancel_root/scripts/release-common.sh"
new_lock="$cancel_root/.locks/release.lock.d"
release_acquire_lock "$new_lock" new-token aicopilot-release reserved support-ready 900
set +e
bash "$REPO_DIR/deploy/enterprise-ai/scripts/cancel-support-reservation.sh" "$cancel_root" old-token > "$TEST_ROOT/old-token-cancel.log" 2>&1
old_cancel_status=$?
set -e
assert_eq 3 "$old_cancel_status" "old-token cancellation against new reservation"
assert_file_contains "$new_lock/token" "new-token"
release_unlock "$new_lock" new-token

printf 'TEST unsafe path in installed old support manifest is rejected before deletion\n'
cp -p "$REMOTE_DIR/.aicopilot-support-manifest.sha256" "$TEST_ROOT/good-support-manifest.sha256"
env_hash_before="$(sha256_file "$REMOTE_DIR/.env")"
unsafe_case=0
for unsafe_path in '../../.env' './.env' './releases'; do
  unsafe_case=$((unsafe_case + 1))
  printf '%s  %s\n' "$(printf unsafe | shasum -a 256 | awk '{print $1}')" "$unsafe_path" > "$REMOTE_DIR/.aicopilot-support-manifest.sha256"
  set +e
  AICOPILOT_FORCE_BUILD=true run_local_release > "$TEST_ROOT/unsafe-old-support-path-$unsafe_case.log" 2>&1
  unsafe_old_status=$?
  set -e
  assert_eq 65 "$unsafe_old_status" "unsafe old support path exit code"
  assert_file_contains "$TEST_ROOT/unsafe-old-support-path-$unsafe_case.log" "Unsafe or protected support manifest path"
  assert_eq "$env_hash_before" "$(sha256_file "$REMOTE_DIR/.env")" "env after unsafe old support manifest"
done
cp -p "$TEST_ROOT/good-support-manifest.sha256" "$REMOTE_DIR/.aicopilot-support-manifest.sha256"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "unsafe old support path left a reservation"

printf 'TEST support manifest target rejects symbolic-link parent escape\n'
symlink_root="$TEST_ROOT/symlink-root"
symlink_outside="$TEST_ROOT/symlink-outside"
mkdir -p "$symlink_root" "$symlink_outside"
ln -s "$symlink_outside" "$symlink_root/scripts"
set +e
bash -c '. "$1"; release_validate_manifest_target "$2" scripts/escape.sh' \
  bash "$REPO_DIR/deploy/enterprise-ai/scripts/release-common.sh" "$symlink_root" > "$TEST_ROOT/symlink-manifest-path.log" 2>&1
symlink_status=$?
set -e
assert_eq 65 "$symlink_status" "support symlink parent exit code"
assert_file_contains "$TEST_ROOT/symlink-manifest-path.log" "symbolic link"

printf 'TEST ordinary build failure preserves exact exit code\n'
set +e
AICOPILOT_FORCE_BUILD=true FAKE_DOCKER_FAIL_MATCH='buildx build' FAKE_DOCKER_FAIL_EXIT=42 run_local_release > "$TEST_ROOT/build-failure.log" 2>&1
ordinary_status=$?
set -e
assert_eq 42 "$ordinary_status" "ordinary build failure exit code"

printf 'TEST plan digest drift is rejected before release-state mutation\n'
env_hash_before="$(sha256_file "$REMOTE_DIR/.env")"
current_hash_before="$(sha256_file "$REMOTE_DIR/releases/current-release.env")"
set +e
AICOPILOT_FORCE_BUILD=true \
FAKE_REMOTE_PLAN_DIGEST_OVERRIDE='cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc' \
  run_local_release > "$TEST_ROOT/plan-digest-drift.log" 2>&1
plan_drift_status=$?
set -e
assert_eq 65 "$plan_drift_status" "plan digest drift exit code"
assert_file_contains "$TEST_ROOT/plan-digest-drift.log" "plan digest drifted after support reservation"
assert_eq "$env_hash_before" "$(sha256_file "$REMOTE_DIR/.env")" "env after plan digest drift"
assert_eq "$current_hash_before" "$(sha256_file "$REMOTE_DIR/releases/current-release.env")" "current release after plan digest drift"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "candidate drift left this run reservation behind"

printf 'TEST real timeout returns 124 and clears descendants\n'
set +e
bash -c ". '$REPO_DIR/deploy/enterprise-ai/scripts/release-common.sh'; set -euo pipefail; DRY_RUN=false; release_run_with_timeout 1 timeout-tree bash -c 'sleep 31 & wait'" > "$TEST_ROOT/timeout.log" 2>&1
timeout_status=$?
set -e
assert_eq 124 "$timeout_status" "real timeout exit code"
if pgrep -f 'sleep 31' >/dev/null 2>&1; then
  fail "timeout left a descendant process"
fi

printf 'TEST digest mismatch fails before persistent state mutation\n'
env_hash_before="$(sha256_file "$REMOTE_DIR/.env")"
current_hash_before="$(sha256_file "$REMOTE_DIR/releases/current-release.env")"
printf '# drift\n' >> "$REMOTE_DIR/docker-compose.yaml"
dead_sha='dddddddddddddddddddddddddddddddddddddddd'
dead_services_file="$TEST_ROOT/dead-services.txt"
dead_images_file="$TEST_ROOT/dead-images.env"
printf 'web\n' > "$dead_services_file"
dead_digest='sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
printf 'AICOPILOT_WEBUI_IMAGE=registry.factory.internal:5000/enterprise-ai/aicopilot-webui@%s\n' "$dead_digest" > "$dead_images_file"
printf 'AICOPILOT_WEBUI_IMAGE_TAG=registry.factory.internal:5000/enterprise-ai/aicopilot-webui:sha-%s\n' "$dead_sha" >> "$dead_images_file"
printf 'AICOPILOT_WEBUI_IMAGE_DIGEST=%s\n' "$dead_digest" >> "$dead_images_file"
set +e
(cd "$REMOTE_DIR" && \
  IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 \
  IIOT_WORKSPACE_DEPLOY_INVOCATION_ID=digest-mismatch-check \
  IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA="$dead_sha" \
  IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST="$WORKSPACE_PLAN_DIGEST" \
  IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST="$WORKSPACE_PROFILE_DIGEST" \
  DEPLOY_GIT_SHA="$dead_sha" \
  DEPLOY_LOCK_TOKEN=digest-mismatch-check \
  DEPLOY_SERVICES_MANIFEST_DIGEST="$(sha256_file "$dead_services_file")" \
  DEPLOY_IMAGE_MANIFEST_DIGEST="$(sha256_file "$dead_images_file")" \
  DEPLOY_CANDIDATE_AICOPILOT_WEBUI_IMAGE="registry.factory.internal:5000/enterprise-ai/aicopilot-webui@$dead_digest" \
  DEPLOY_CANDIDATE_AICOPILOT_WEBUI_IMAGE_TAG="registry.factory.internal:5000/enterprise-ai/aicopilot-webui:sha-$dead_sha" \
  DEPLOY_CANDIDATE_AICOPILOT_WEBUI_IMAGE_DIGEST="$dead_digest" \
  EXPECTED_SUPPORT_DIGEST="$(sed -n '1p' .aicopilot-support-manifest.digest)" \
  ./deploy-release.sh "sha-$dead_sha" --check-current --services web) > "$TEST_ROOT/digest-mismatch.log" 2>&1
digest_status=$?
set -e
assert_eq 65 "$digest_status" "support digest mismatch exit code"
assert_eq "$env_hash_before" "$(sha256_file "$REMOTE_DIR/.env")" "env after digest mismatch"
assert_eq "$current_hash_before" "$(sha256_file "$REMOTE_DIR/releases/current-release.env")" "current release after digest mismatch"

printf 'TEST restore support after deliberate drift\n'
run_local_release > "$TEST_ROOT/repair-support.log" 2>&1

printf 'TEST compose failure rolls back env and release state\n'
env_hash_before="$(sha256_file "$REMOTE_DIR/.env")"
current_hash_before="$(sha256_file "$REMOTE_DIR/releases/current-release.env")"
set +e
AICOPILOT_FORCE_BUILD=true \
AICOPILOT_ALLOW_REDEPLOY_SAME_SHA=true \
FAKE_DOCKER_FAIL_MATCH='pull aicopilot-webui' \
FAKE_DOCKER_FAIL_EXIT=37 \
  run_local_release > "$TEST_ROOT/state-rollback.log" 2>&1
rollback_status=$?
set -e
assert_eq 37 "$rollback_status" "compose failure exit code"
assert_eq "$env_hash_before" "$(sha256_file "$REMOTE_DIR/.env")" "env rollback"
assert_eq "$current_hash_before" "$(sha256_file "$REMOTE_DIR/releases/current-release.env")" "current release rollback"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "release lock remained after rollback"

printf 'TEST reversible post-container failure restores previous runtime and health\n'
env_hash_before="$(sha256_file "$REMOTE_DIR/.env")"
current_hash_before="$(sha256_file "$REMOTE_DIR/releases/current-release.env")"
: > "$DOCKER_LOG"
set +e
AICOPILOT_FORCE_BUILD=true \
AICOPILOT_ALLOW_REDEPLOY_SAME_SHA=true \
AICOPILOT_NON_PRODUCTION_MECHANISM_TEST=true \
AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE=58 \
  run_local_release > "$TEST_ROOT/reversible-runtime-failure.log" 2>&1
reversible_status=$?
set -e
assert_eq 58 "$reversible_status" "reversible runtime failure exit code"
assert_file_contains "$TEST_ROOT/reversible-runtime-failure.log" "restored to the previous manifest and revalidated"
assert_file_contains "$TEST_ROOT/reversible-runtime-failure.log" "AICopilot web probe succeeded"
assert_file_contains "$TEST_ROOT/reversible-runtime-failure.log" "AICopilot web HTTP-only security header probe succeeded"
assert_file_contains "$TEST_ROOT/reversible-runtime-failure.log" "AICopilot release security attestation passed."
assert_file_contains "$DOCKER_LOG" "up -d aicopilot-httpapi aicopilot-dataworker aicopilot-ragworker aicopilot-webui"
assert_file_contains "$DOCKER_LOG" "ps -q aicopilot-dataworker"
assert_eq "$env_hash_before" "$(sha256_file "$REMOTE_DIR/.env")" "env after reversible runtime recovery"
assert_eq "$current_hash_before" "$(sha256_file "$REMOTE_DIR/releases/current-release.env")" "current release after reversible runtime recovery"
[ ! -f "$REMOTE_DIR/releases/blocked-release.env" ] || fail "reversible failure incorrectly blocked automatic deployment"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "reversible runtime recovery left the release lock"
if find "$REMOTE_DIR" -maxdepth 1 -type d -name '.release-transaction.*' -print -quit | grep -q .; then
  fail "reversible runtime recovery left a release transaction backup"
fi

printf 'TEST infrastructure rollback uses frozen previous immutable identity after tag drift\n'
rm -f "$REMOTE_DIR/.fake-infra-tag-drift-active"
: > "$DOCKER_LOG"
set +e
AICOPILOT_FORCE_BUILD=true \
AICOPILOT_ALLOW_REDEPLOY_SAME_SHA=true \
AICOPILOT_NON_PRODUCTION_MECHANISM_TEST=true \
AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE=58 \
FAKE_INFRA_TAG_DRIFT=true \
  run_local_release > "$TEST_ROOT/infra-tag-drift-recovery.log" 2>&1
infra_tag_drift_status=$?
set -e
assert_eq 58 "$infra_tag_drift_status" "immutable infrastructure tag-drift recovery exit code"
assert_file_contains "$TEST_ROOT/infra-tag-drift-recovery.log" "restored to the previous manifest and revalidated"
assert_file_contains "$DOCKER_LOG" "frozen-infra-recovery POSTGRES_IMAGE=registry.factory.internal:5000/enterprise-ai/base-postgres@sha256:1111111111111111111111111111111111111111111111111111111111111111"
assert_file_contains "$DOCKER_LOG" "frozen-infra-recovery RABBITMQ_IMAGE=registry.factory.internal:5000/enterprise-ai/base-rabbitmq@sha256:2222222222222222222222222222222222222222222222222222222222222222"
assert_file_contains "$DOCKER_LOG" "frozen-infra-recovery QDRANT_IMAGE=registry.factory.internal:5000/enterprise-ai/base-qdrant@sha256:3333333333333333333333333333333333333333333333333333333333333333"
[ ! -f "$REMOTE_DIR/releases/blocked-release.env" ] || fail "immutable infrastructure recovery incorrectly blocked deployment"
rm -f "$REMOTE_DIR/.fake-infra-tag-drift-active"

printf 'TEST recovery validates unselected runtimes before claiming restoration\n'
set +e
AICOPILOT_FORCE_BUILD=true \
AICOPILOT_ALLOW_REDEPLOY_SAME_SHA=true \
AICOPILOT_NON_PRODUCTION_MECHANISM_TEST=true \
AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE=58 \
FAKE_UNHEALTHY_SERVICE=aicopilot-dataworker \
  run_local_release > "$TEST_ROOT/unselected-runtime-recovery-failure.log" 2>&1
unselected_recovery_status=$?
set -e
assert_eq 86 "$unselected_recovery_status" "unselected runtime recovery unsafe-partial exit code"
assert_file_contains "$REMOTE_DIR/releases/blocked-release.env" "DEPLOY_FAILURE_REASON=container-recovery-failed"
unselected_transaction="$(sed -n 's/^DEPLOY_TRANSACTION_BACKUP=//p' "$REMOTE_DIR/releases/blocked-release.env")"
[ -d "$unselected_transaction" ] || fail "unselected runtime recovery failure did not retain transaction backup"
rm -f "$REMOTE_DIR/releases/blocked-release.env"
rm -rf "$unselected_transaction" "$REMOTE_DIR/.locks/release.lock.d" "$REMOTE_DIR/.support-backups"

printf 'TEST failed runtime recovery persists blocked partial state and blocks retry\n'
set +e
AICOPILOT_FORCE_BUILD=true \
AICOPILOT_ALLOW_REDEPLOY_SAME_SHA=true \
AICOPILOT_NON_PRODUCTION_MECHANISM_TEST=true \
AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE=58 \
FAKE_CURL_FAIL_COUNT=1 \
FAKE_CURL_FAIL_MATCH='--head' \
  run_local_release > "$TEST_ROOT/blocked-runtime-failure.log" 2>&1
blocked_failure_status=$?
set -e
assert_eq 86 "$blocked_failure_status" "blocked runtime unsafe-partial exit code"
assert_file_contains "$TEST_ROOT/blocked-runtime-failure.log" "AICopilot web security header probe failed"
assert_file_contains "$REMOTE_DIR/releases/blocked-release.env" "DEPLOY_STATUS=blocked-partial"
assert_file_contains "$REMOTE_DIR/releases/blocked-release.env" "DEPLOY_FAILURE_REASON=container-recovery-failed"
blocked_transaction="$(sed -n 's/^DEPLOY_TRANSACTION_BACKUP=//p' "$REMOTE_DIR/releases/blocked-release.env")"
[ -d "$blocked_transaction" ] || fail "blocked partial state did not retain its transaction backup"
assert_file_contains "$blocked_transaction/previous-infra-runtime.env" "PREVIOUS_POSTGRES_IMAGE_REF=registry.factory.internal:5000/enterprise-ai/base-postgres@sha256:1111111111111111111111111111111111111111111111111111111111111111"
blocked_token="$(sed -n '1p' "$REMOTE_DIR/.locks/release.lock.d/token")"
[ -n "$blocked_token" ] || fail "blocked partial state lost its release token"
assert_file_contains "$REMOTE_DIR/releases/invocations/$blocked_token.env" "DEPLOY_STATUS=unsafe-partial"
assert_file_contains "$REMOTE_DIR/releases/invocations/$blocked_token.env" "DEPLOY_EXIT_CODE=86"
set +e
run_local_release > "$TEST_ROOT/blocked-retry.log" 2>&1
blocked_retry_status=$?
set -e
assert_eq 78 "$blocked_retry_status" "blocked automatic retry exit code"
assert_file_contains "$TEST_ROOT/blocked-retry.log" "unresolved partial state"
rm -f "$REMOTE_DIR/releases/blocked-release.env"
rm -rf "$blocked_transaction"
assert_file_contains "$REMOTE_DIR/.locks/release.lock.d/state" "blocked"
rm -rf "$REMOTE_DIR/.locks/release.lock.d" "$REMOTE_DIR/.support-backups"

printf 'TEST blocked-state write failure retains transaction and detectable owned lock\n'
set +e
AICOPILOT_FORCE_BUILD=true \
AICOPILOT_ALLOW_REDEPLOY_SAME_SHA=true \
AICOPILOT_NON_PRODUCTION_MECHANISM_TEST=true \
AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE=58 \
AICOPILOT_TEST_FORCE_RECOVERY_FAILURE=true \
AICOPILOT_TEST_FORCE_BLOCKED_STATE_WRITE_FAILURE=true \
  run_local_release > "$TEST_ROOT/blocked-write-failure.log" 2>&1
blocked_write_status=$?
set -e
assert_eq 86 "$blocked_write_status" "blocked-state persistence unsafe-partial exit code"
[ ! -f "$REMOTE_DIR/releases/blocked-release.env" ] || fail "forced blocked-state write failure unexpectedly wrote blocked state"
orphan_transaction="$(find "$REMOTE_DIR" -maxdepth 1 -type d -name '.release-transaction.*' -print -quit)"
[ -d "$orphan_transaction" ] || fail "blocked-state write failure did not retain transaction backup"
assert_file_contains "$orphan_transaction/BLOCKED_PERSISTENCE_FAILED" "blocked-state-persistence-failed"
[ -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "blocked-state write failure did not retain its detectable lock"
set +e
run_local_release > "$TEST_ROOT/orphan-transaction-retry.log" 2>&1
orphan_retry_status=$?
set -e
assert_eq 79 "$orphan_retry_status" "orphan transaction retry gate exit code"
assert_file_contains "$TEST_ROOT/orphan-transaction-retry.log" "unresolved transaction backup"
rm -rf "$orphan_transaction" "$REMOTE_DIR/.locks/release.lock.d" "$REMOTE_DIR/.support-backups"

printf 'TEST abnormal worker process state blocks commit and records migration partial state\n'
TEST_SERVICES='migration,dataworker' \
TEST_WORKSPACE_PLAN_FILE="$WORKER_PLAN_FILE" \
TEST_WORKSPACE_PLAN_DIGEST="$WORKER_PLAN_DIGEST" \
TEST_WORKSPACE_INVOCATION_ID=deployment-behavior-worker \
  run_local_release > "$TEST_ROOT/worker-baseline.log" 2>&1
set +e
TEST_SERVICES='migration,dataworker' \
TEST_WORKSPACE_PLAN_FILE="$WORKER_PLAN_FILE" \
TEST_WORKSPACE_PLAN_DIGEST="$WORKER_PLAN_DIGEST" \
TEST_WORKSPACE_INVOCATION_ID=deployment-behavior-worker \
FAKE_UNHEALTHY_SERVICE=aicopilot-dataworker \
  run_local_release > "$TEST_ROOT/worker-abnormal.log" 2>&1
worker_abnormal_status=$?
set -e
assert_eq 86 "$worker_abnormal_status" "abnormal worker unsafe-partial exit code"
assert_file_contains "$REMOTE_DIR/releases/blocked-release.env" "DEPLOY_FAILURE_REASON=migration-or-runtime-partial"
worker_transaction="$(sed -n 's/^DEPLOY_TRANSACTION_BACKUP=//p' "$REMOTE_DIR/releases/blocked-release.env")"
[ -d "$worker_transaction" ] || fail "worker partial failure did not retain transaction backup"
rm -f "$REMOTE_DIR/releases/blocked-release.env"
rm -rf "$worker_transaction" "$REMOTE_DIR/.locks/release.lock.d" "$REMOTE_DIR/.support-backups"

printf 'TEST active and stale release locks\n'
. "$REPO_DIR/deploy/enterprise-ai/scripts/release-common.sh"
release_acquire_lock "$REMOTE_DIR/.locks/release.lock.d" active-test aicopilot-release active test 0
set +e
(cd "$REMOTE_DIR" && ./deploy-release.sh --validate-only) > "$TEST_ROOT/active-lock.log" 2>&1
active_lock_status=$?
set -e
assert_eq 75 "$active_lock_status" "active lock exit code"
release_unlock "$REMOTE_DIR/.locks/release.lock.d" active-test
mkdir -p "$REMOTE_DIR/.locks/release.lock.d"
printf 'stale-test\n' > "$REMOTE_DIR/.locks/release.lock.d/token"
printf 'active\n' > "$REMOTE_DIR/.locks/release.lock.d/state"
printf '99999999\n' > "$REMOTE_DIR/.locks/release.lock.d/pid"
printf 'old-start\n' > "$REMOTE_DIR/.locks/release.lock.d/process-start"
(cd "$REMOTE_DIR" && ./deploy-release.sh --validate-only) > "$TEST_ROOT/stale-lock.log" 2>&1
assert_file_contains "$TEST_ROOT/stale-lock.log" "Removed stale deployment lock"

printf 'TEST concurrent deployments serialize on release lock\n'
AICOPILOT_FORCE_BUILD=true FAKE_DOCKER_DELAY_MATCH='compose --env-file' FAKE_DOCKER_DELAY_SECONDS=3 run_local_release > "$TEST_ROOT/concurrent-a.log" 2>&1 &
first_pid=$!
for _ in $(seq 1 100); do
  [ -d "$REMOTE_DIR/.locks/release.lock.d" ] && break
  sleep 0.1
done
set +e
AICOPILOT_FORCE_BUILD=true run_local_release > "$TEST_ROOT/concurrent-b.log" 2>&1
second_status=$?
wait "$first_pid"
first_status=$?
set -e
assert_eq 0 "$first_status" "first concurrent deploy"
assert_eq 75 "$second_status" "second concurrent deploy"

printf 'TEST cleanup signal exits and releases managed lock\n'
FAKE_DOCKER_DELAY_MATCH='builder prune' FAKE_DOCKER_DELAY_SECONDS=3 \
  POST_RELEASE_DATA_PATH="$DATA_DIR" \
  POST_RELEASE_CLEANUP_LOCK_FILE="$REMOTE_DIR/.locks/signal-cleanup.lock" \
  POST_RELEASE_HARBOR_GC_ENABLED=0 \
  DEPLOY_DIR="$REMOTE_DIR" ENV_FILE="$REMOTE_DIR/.env" \
  "$REMOTE_DIR/post-release-cleanup.sh" --release-tag "sha-$SOURCE_SHA" > "$TEST_ROOT/cleanup-signal.log" 2>&1 &
cleanup_pid=$!
for _ in $(seq 1 100); do
  [ -f "$REMOTE_DIR/.locks/signal-cleanup.lock.d/phase" ] && break
  sleep 0.1
done
kill -TERM "$cleanup_pid"
set +e
wait "$cleanup_pid"
cleanup_status=$?
set -e
assert_eq 143 "$cleanup_status" "cleanup signal exit code"
[ ! -d "$REMOTE_DIR/.locks/signal-cleanup.lock.d" ] || fail "cleanup lock remained after TERM"

printf 'TEST no watchdog process is orphaned\n'
orphan_watchdogs="$(ps -axo pid=,ppid=,command= | awk '$2 == 1 && $3 == "sleep" && ($4 == "211" || $4 == "212" || $4 == "213" || $4 == "214" || $4 == "215") {print}')"
[ -z "$orphan_watchdogs" ] || fail "orphan watchdogs detected: $orphan_watchdogs"

printf 'NON_PRODUCTION_MECHANISM_TEST productionEligible=false result=passed\n'
printf 'All AICopilot deployment behavior tests passed.\n'
