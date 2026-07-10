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
case "$full" in
  "buildx version"*) printf 'github.com/docker/buildx fake\n'; exit 0 ;;
  buildx\ build*) exit 0 ;;
  buildx\ imagetools\ inspect*) printf 'sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n'; exit 0 ;;
  inspect*'.Config.Image'*) sed -n 's/^AICOPILOT_WEBUI_IMAGE=//p' "${FAKE_REMOTE_DIR:?}/.env" | tail -n 1; exit 0 ;;
  inspect*'{{.Image}}'*) printf 'sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee\n'; exit 0 ;;
  inspect*'-f '*|inspect*'--format '*) printf 'healthy\n'; exit 0 ;;
  compose*' ps -q '*) printf 'fake-container-id\n'; exit 0 ;;
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
exec /bin/mv "$@"
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
bash -c "$remote_command"
EOF
chmod +x "$BIN_DIR/dotnet" "$BIN_DIR/docker" "$BIN_DIR/curl" "$BIN_DIR/mv" "$BIN_DIR/ssh"

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
  SSH_TIMEOUT_SECONDS=213 \
  BUILD_TIMEOUT_SECONDS=214 \
  HARBOR_TIMEOUT_SECONDS=215 \
    "$REPO_DIR/deploy/enterprise-ai/local-release.sh" \
      --services web \
      --ssh-target fake-host \
      --remote-dir "$REMOTE_DIR"
}

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
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_CONFIG_SUMMARY_DIGEST="
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_PROFILE_DIGEST=$WORKSPACE_PROFILE_DIGEST"
assert_file_contains "$REMOTE_DIR/releases/current-release.env" "DEPLOY_RUNTIME_AICOPILOT_WEBUI_IMAGE_DIGEST=sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
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

printf 'TEST unsafe path in installed old support manifest is rejected before deletion\n'
cp -p "$REMOTE_DIR/.aicopilot-support-manifest.sha256" "$TEST_ROOT/good-support-manifest.sha256"
printf '%s  ../../.env\n' "$(printf unsafe | shasum -a 256 | awk '{print $1}')" > "$REMOTE_DIR/.aicopilot-support-manifest.sha256"
env_hash_before="$(sha256_file "$REMOTE_DIR/.env")"
set +e
AICOPILOT_FORCE_BUILD=true run_local_release > "$TEST_ROOT/unsafe-old-support-path.log" 2>&1
unsafe_old_status=$?
set -e
assert_eq 65 "$unsafe_old_status" "unsafe old support path exit code"
assert_file_contains "$TEST_ROOT/unsafe-old-support-path.log" "Unsafe or protected support manifest path"
assert_eq "$env_hash_before" "$(sha256_file "$REMOTE_DIR/.env")" "env after unsafe old support manifest"
cp -p "$TEST_ROOT/good-support-manifest.sha256" "$REMOTE_DIR/.aicopilot-support-manifest.sha256"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "unsafe old support path left a reservation"

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
FAKE_DOCKER_FAIL_MATCH='compose --env-file' \
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
assert_eq "$env_hash_before" "$(sha256_file "$REMOTE_DIR/.env")" "env after reversible runtime recovery"
assert_eq "$current_hash_before" "$(sha256_file "$REMOTE_DIR/releases/current-release.env")" "current release after reversible runtime recovery"
[ ! -f "$REMOTE_DIR/releases/blocked-release.env" ] || fail "reversible failure incorrectly blocked automatic deployment"

printf 'TEST failed runtime recovery persists blocked partial state and blocks retry\n'
set +e
AICOPILOT_FORCE_BUILD=true \
AICOPILOT_ALLOW_REDEPLOY_SAME_SHA=true \
AICOPILOT_NON_PRODUCTION_MECHANISM_TEST=true \
AICOPILOT_TEST_FAIL_AFTER_CONTAINER_EXIT_CODE=58 \
AICOPILOT_TEST_FORCE_RECOVERY_FAILURE=true \
  run_local_release > "$TEST_ROOT/blocked-runtime-failure.log" 2>&1
blocked_failure_status=$?
set -e
assert_eq 58 "$blocked_failure_status" "blocked runtime failure exit code"
assert_file_contains "$REMOTE_DIR/releases/blocked-release.env" "DEPLOY_STATUS=blocked-partial"
assert_file_contains "$REMOTE_DIR/releases/blocked-release.env" "DEPLOY_FAILURE_REASON=container-recovery-failed"
blocked_transaction="$(sed -n 's/^DEPLOY_TRANSACTION_BACKUP=//p' "$REMOTE_DIR/releases/blocked-release.env")"
[ -d "$blocked_transaction" ] || fail "blocked partial state did not retain its transaction backup"
set +e
run_local_release > "$TEST_ROOT/blocked-retry.log" 2>&1
blocked_retry_status=$?
set -e
assert_eq 78 "$blocked_retry_status" "blocked automatic retry exit code"
assert_file_contains "$TEST_ROOT/blocked-retry.log" "unresolved partial state"
rm -f "$REMOTE_DIR/releases/blocked-release.env"
rm -rf "$blocked_transaction"
[ ! -d "$REMOTE_DIR/.locks/release.lock.d" ] || fail "blocked retry left this run reservation behind"

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
