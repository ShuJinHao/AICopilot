#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
# shellcheck source=scripts/release-common.sh
. "$SCRIPT_DIR/scripts/release-common.sh"

REQUESTED_SERVICES=""
REQUESTED_ALL=false
DRY_RUN=false
HARBOR_PROJECT="${HARBOR_PROJECT:-enterprise-ai}"
SSH_TARGET="${DEPLOY_SSH_TARGET:-}"
REMOTE_DEPLOY_DIR="${REMOTE_DEPLOY_DIR:-/srv/enterprise-ai/deploy}"
SSH_TIMEOUT_SECONDS="${SSH_TIMEOUT_SECONDS:-1800}"
SYNC_TIMEOUT_SECONDS="${SYNC_TIMEOUT_SECONDS:-120}"
GIT_TIMEOUT_SECONDS="${GIT_TIMEOUT_SECONDS:-120}"
SSH_CONNECT_TIMEOUT_SECONDS="${SSH_CONNECT_TIMEOUT_SECONDS:-15}"
LOCK_RESERVATION_TTL_SECONDS="${LOCK_RESERVATION_TTL_SECONDS:-$((SSH_TIMEOUT_SECONDS + 300))}"
SSH_OPTIONS=(
  -o BatchMode=yes
  -o "ConnectTimeout=$SSH_CONNECT_TIMEOUT_SECONDS"
  -o ServerAliveInterval=15
  -o ServerAliveCountMax=3
)
RUN_ROOT=""
RUN_ID=""
SOURCE_GIT_SHA=""
SNAPSHOT_DIR=""
OUTPUT_DIR=""
SUPPORT_TREE=""
SUPPORT_DIGEST=""
REMOTE_RESERVATION_CREATED=false
EXPECTED_DIR=""
CANDIDATE_SERVICES=""
SERVICES_MANIFEST_DIGEST=""
IMAGE_MANIFEST_DIGEST=""
WORKSPACE_ENTRYPOINT="${IIOT_WORKSPACE_DEPLOY_ENTRYPOINT:-}"
WORKSPACE_INVOCATION_ID="${IIOT_WORKSPACE_DEPLOY_INVOCATION_ID:-}"
WORKSPACE_EXPECTED_SHA="${IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA:-}"
WORKSPACE_PLAN_DIGEST="${IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST:-}"
WORKSPACE_PLAN_FILE="${IIOT_WORKSPACE_DEPLOY_PLAN_FILE:-}"
WORKSPACE_PROFILE_DIGEST="${IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST:-}"

usage() {
  cat <<'EOF'
Usage:
  deploy/enterprise-ai/local-release.sh --services httpapi,web --ssh-target github-runner@<shared-host>
  deploy/enterprise-ai/local-release.sh --all --ssh-target github-runner@<shared-host>

Builds a fixed pushed git commit in an isolated worktree, pushes selected
AICopilot images, installs a digest-bound support release, then SSH-triggers
the server-side deploy-release.sh entrypoint under one remote release lock.
EOF
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
}

require_full_git_sha() {
  local value="$1"
  local label="$2"
  if [[ ! "$value" =~ ^[0-9a-f]{40}$ ]]; then
    fail "$label must be a lowercase full 40-character Git SHA."
  fi
}

require_sha256_digest() {
  local value="$1"
  local label="$2"
  if [[ ! "$value" =~ ^[0-9a-f]{64}$ ]]; then
    fail "$label must be a lowercase 64-character SHA256 digest."
  fi
}

require_bounded_integer() {
  local value="$1"
  local label="$2"
  local minimum="$3"
  local maximum="$4"
  case "$value" in
    ''|*[!0-9]*) fail "$label must be an integer between $minimum and $maximum." ;;
  esac
  if [ "$value" -lt "$minimum" ] || [ "$value" -gt "$maximum" ]; then
    fail "$label must be between $minimum and $maximum."
  fi
}

validate_transport_inputs() {
  [[ "$SSH_TARGET" =~ ^([A-Za-z0-9._-]+@)?[A-Za-z0-9._-]+$ ]] || fail "AICopilot SSH target contains unsupported characters."
  [[ "$REMOTE_DEPLOY_DIR" =~ ^/[A-Za-z0-9._/-]+$ ]] || fail "AICopilot remote deploy directory must be a safe absolute path without spaces or shell metacharacters."
  case "$REMOTE_DEPLOY_DIR" in
    /|*//*|*/../*|*/..|../*|..)
      fail "AICopilot remote deploy directory is unsafe: $REMOTE_DEPLOY_DIR"
      ;;
  esac
  require_bounded_integer "$SSH_CONNECT_TIMEOUT_SECONDS" SSH_CONNECT_TIMEOUT_SECONDS 1 120
  require_bounded_integer "$GIT_TIMEOUT_SECONDS" GIT_TIMEOUT_SECONDS 1 3600
  require_bounded_integer "$SYNC_TIMEOUT_SECONDS" SYNC_TIMEOUT_SECONDS 1 3600
  require_bounded_integer "$SSH_TIMEOUT_SECONDS" SSH_TIMEOUT_SECONDS 1 7200
  require_bounded_integer "$LOCK_RESERVATION_TTL_SECONDS" LOCK_RESERVATION_TTL_SECONDS 60 86400
  if [ "$LOCK_RESERVATION_TTL_SECONDS" -lt $((SSH_TIMEOUT_SECONDS + 60)) ]; then
    fail "LOCK_RESERVATION_TTL_SECONDS must be at least SSH_TIMEOUT_SECONDS + 60."
  fi
}

validate_workspace_candidate() {
  local plan_profile_digest
  if [ "$DRY_RUN" = true ]; then
    printf 'NON_PRODUCTION_MECHANISM_TEST productionEligible=false mode=dry-run\n'
    return
  fi

  [ "$WORKSPACE_ENTRYPOINT" = "1" ] || fail "Formal AICopilot local release requires IIOT_WORKSPACE_DEPLOY_ENTRYPOINT=1 from the workspace deploy entrypoint."
  if [[ ! "$WORKSPACE_INVOCATION_ID" =~ ^[A-Za-z0-9._:-]+$ ]]; then
    fail "Formal AICopilot local release requires a safe non-empty IIOT_WORKSPACE_DEPLOY_INVOCATION_ID."
  fi
  require_full_git_sha "$WORKSPACE_EXPECTED_SHA" "IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA"
  require_sha256_digest "$WORKSPACE_PLAN_DIGEST" "IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST"
  require_sha256_digest "$WORKSPACE_PROFILE_DIGEST" "IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST"
  case "$WORKSPACE_PLAN_FILE" in
    /*) ;;
    *) fail "Formal AICopilot local release requires an absolute IIOT_WORKSPACE_DEPLOY_PLAN_FILE." ;;
  esac
  [ -f "$WORKSPACE_PLAN_FILE" ] && [ -r "$WORKSPACE_PLAN_FILE" ] || fail "Workspace deployment plan file is missing or unreadable: $WORKSPACE_PLAN_FILE"
  if [ "$(release_sha256_file "$WORKSPACE_PLAN_FILE")" != "$WORKSPACE_PLAN_DIGEST" ]; then
    fail "Workspace deployment plan file digest does not match IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST."
  fi
  plan_profile_digest="$(sed -n 's/.*"profileDigest"[[:space:]]*:[[:space:]]*"\([0-9a-f]*\)".*/\1/p' "$WORKSPACE_PLAN_FILE" | head -n 1)"
  [ "$plan_profile_digest" = "$WORKSPACE_PROFILE_DIGEST" ] || fail "Workspace deployment plan profileDigest does not match IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST."
  if [ "$WORKSPACE_EXPECTED_SHA" != "$SOURCE_GIT_SHA" ]; then
    fail "AICopilot candidate SHA mismatch: workspace expected $WORKSPACE_EXPECTED_SHA, fixed source $SOURCE_GIT_SHA."
  fi
}

normalize_candidate_services() {
  local normalized=""
  local item
  local service
  local backend_runtime_selected=false
  local has_migration=false

  if [ "$REQUESTED_ALL" = true ]; then
    printf '%s\n' "httpapi,migration,dataworker,ragworker,web"
    return
  fi

  for item in $(printf '%s' "$REQUESTED_SERVICES" | tr ',' ' '); do
    case "$item" in
      httpapi|aicopilot-httpapi)
        service=httpapi
        backend_runtime_selected=true
        ;;
      migration|aicopilot-migration)
        service=migration
        has_migration=true
        ;;
      dataworker|aicopilot-dataworker)
        service=dataworker
        backend_runtime_selected=true
        ;;
      ragworker|aicopilot-ragworker)
        service=ragworker
        backend_runtime_selected=true
        ;;
      web|webui|aicopilot-webui)
        service=web
        ;;
      *)
        fail "Unsupported AICopilot image service: $item"
        ;;
    esac
    case ",$normalized," in
      *,"$service",*) ;;
      ,,) normalized="$service" ;;
      *) normalized="$normalized,$service" ;;
    esac
  done
  if [ "$backend_runtime_selected" = true ] && [ "$has_migration" != true ]; then
    normalized="$normalized,migration"
  fi
  [ -n "$normalized" ] || fail "No AICopilot image services were selected."
  printf '%s\n' "$normalized"
}

image_key_for_candidate_service() {
  case "$1" in
    httpapi) printf '%s\n' AICOPILOT_HTTPAPI_IMAGE ;;
    migration) printf '%s\n' AICOPILOT_MIGRATION_IMAGE ;;
    dataworker) printf '%s\n' AICOPILOT_DATAWORKER_IMAGE ;;
    ragworker) printf '%s\n' AICOPILOT_RAGWORKER_IMAGE ;;
    web) printf '%s\n' AICOPILOT_WEBUI_IMAGE ;;
    *) fail "Unsupported AICopilot candidate service: $1" ;;
  esac
}

image_name_for_candidate_service() {
  case "$1" in
    httpapi) printf '%s\n' aicopilot-httpapi ;;
    migration) printf '%s\n' aicopilot-migration ;;
    dataworker) printf '%s\n' aicopilot-dataworker ;;
    ragworker) printf '%s\n' aicopilot-ragworker ;;
    web) printf '%s\n' aicopilot-webui ;;
    *) fail "Unsupported AICopilot candidate service: $1" ;;
  esac
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --services)
      shift
      REQUESTED_SERVICES="${1:-}"
      ;;
    --services=*)
      REQUESTED_SERVICES="${1#--services=}"
      ;;
    --all)
      REQUESTED_ALL=true
      ;;
    --ssh-target)
      shift
      SSH_TARGET="${1:-}"
      ;;
    --ssh-target=*)
      SSH_TARGET="${1#--ssh-target=}"
      ;;
    --remote-dir)
      shift
      REMOTE_DEPLOY_DIR="${1:-}"
      ;;
    --remote-dir=*)
      REMOTE_DEPLOY_DIR="${1#--remote-dir=}"
      ;;
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown local-release option: $1"
      ;;
  esac
  shift
done

if [ "$REQUESTED_ALL" = true ] && [ -n "$REQUESTED_SERVICES" ]; then
  fail "Use either --all or --services, not both."
fi
if [ "$REQUESTED_ALL" != true ] && [ -z "$REQUESTED_SERVICES" ]; then
  fail "AICopilot local release requires explicit --services or --all."
fi
if [ -z "$SSH_TARGET" ]; then
  fail "AICopilot local release requires DEPLOY_SSH_TARGET or --ssh-target."
fi
validate_transport_inputs
SOURCE_GIT_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
validate_workspace_candidate
[ -n "${REGISTRY:-}" ] || fail "REGISTRY is required, for example harbor.internal.example:80."
[ -n "${CLOUD_PLATFORM_URL:-}" ] || fail "CLOUD_PLATFORM_URL is required, for example http://cloud.internal.example:81."
case "$SSH_TARGET" in
  root@*)
    if [ "${ALLOW_ROOT_SSH_DEPLOY:-false}" != "true" ]; then
      fail "Root SSH deploy is not the standard path. Use a dedicated deploy user, or set ALLOW_ROOT_SSH_DEPLOY=true only for a documented emergency."
    fi
    ;;
esac

run_with_timeout() {
  release_run_with_timeout "$@"
}

cleanup_snapshot() {
  if [ -n "$SNAPSHOT_DIR" ] && [ -d "$SNAPSHOT_DIR" ]; then
    git -C "$REPO_ROOT" worktree remove --force "$SNAPSHOT_DIR" >/dev/null 2>&1 || rm -rf "$SNAPSHOT_DIR"
  fi
}

release_own_remote_reservation() {
  local remote_command
  [ "$REMOTE_RESERVATION_CREATED" = true ] || return 0
  remote_command="bash -s -- '$REMOTE_DEPLOY_DIR' '$RUN_ID'"
  if printf '%s\n' \
      'set -euo pipefail' \
      'deploy_dir="$1"' \
      'token="$2"' \
      'lock_dir="$deploy_dir/.locks/release.lock.d"' \
      '[ -d "$lock_dir" ] || exit 0' \
      '[ "$(sed -n "1p" "$lock_dir/token" 2>/dev/null || true)" = "$token" ] || exit 0' \
      '[ "$(sed -n "1p" "$lock_dir/state" 2>/dev/null || true)" = "reserved" ] || exit 0' \
      'rm -rf "$lock_dir"' | \
      ssh "${SSH_OPTIONS[@]}" "$SSH_TARGET" "$remote_command" >/dev/null 2>&1; then
    REMOTE_RESERVATION_CREATED=false
    return 0
  fi
  printf 'Could not release this run remote reservation safely: token=%s dir=%s\n' "$RUN_ID" "$REMOTE_DEPLOY_DIR" >&2
  return 1
}

finish_local_release() {
  local status="$1"
  trap - EXIT HUP INT TERM
  release_cancel_active_timeout
  cleanup_snapshot
  if [ "$status" -ne 0 ] && [ "$REMOTE_RESERVATION_CREATED" = true ]; then
    release_own_remote_reservation || true
  fi
  if [ "$status" -ne 0 ] && [ "$REMOTE_RESERVATION_CREATED" = true ]; then
    printf 'Remote release reservation may still be active; inspect it before retrying: %s/.locks/release.lock.d\n' "$REMOTE_DEPLOY_DIR" >&2
  fi
  exit "$status"
}

trap 'finish_local_release $?' EXIT
trap 'finish_local_release 129' HUP
trap 'finish_local_release 130' INT
trap 'finish_local_release 143' TERM

print_deploy_diagnostics() {
  cat >&2 <<EOF

AICopilot SSH deploy failed or timed out.
Diagnostics to run before retrying:
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && find .locks -maxdepth 2 -type f -print -exec sed -n "1p" {} \\;'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && cat .aicopilot-support-manifest.digest && tail -n 200 releases/current-release.summary.md'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && docker compose --env-file .env -f docker-compose.yaml ps'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && ls -l releases'
  docker buildx ls
  docker system df
Expected source SHA: $SOURCE_GIT_SHA
Expected support digest: $SUPPORT_DIGEST
Run id: $RUN_ID
EOF
}

require_pushed_clean_head() {
  local remote="origin"
  local status
  local branch
  local remote_main_sha

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] fixed committed HEAD snapshot will be used; clean/pushed enforcement is deferred to formal deploy.\n'
    return
  fi

  if [ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]; then
    git -C "$REPO_ROOT" status --short >&2
    fail "AICopilot local release requires a clean worktree."
  fi

  [ "${GIT_REMOTE:-origin}" = "origin" ] || fail "Formal AICopilot release only accepts the approved origin/main remote."
  branch="$(git -C "$REPO_ROOT" branch --show-current)"
  [ "$branch" = "main" ] || fail "Formal AICopilot release requires the approved main branch, actual: ${branch:-detached}."
  if run_with_timeout "$GIT_TIMEOUT_SECONDS" "git fetch origin/main" \
    git -C "$REPO_ROOT" fetch --quiet origin '+refs/heads/main:refs/remotes/origin/main'; then
    :
  else
    status=$?
    printf 'Could not verify pushed AICopilot HEAD against remote %s.\n' "$remote" >&2
    exit "$status"
  fi
  remote_main_sha="$(git -C "$REPO_ROOT" rev-parse refs/remotes/origin/main)"
  [ "$remote_main_sha" = "$WORKSPACE_EXPECTED_SHA" ] || fail "Approved origin/main tip moved after the workspace plan: expected=$WORKSPACE_EXPECTED_SHA actual=$remote_main_sha. Regenerate the deployment plan."
  [ "$SOURCE_GIT_SHA" = "$remote_main_sha" ] || fail "Formal AICopilot release requires HEAD to equal the fresh origin/main tip."
}

create_fixed_source_snapshot() {
  RUN_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/aicopilot-release.XXXXXX")"
  RUN_ID="$(basename "$RUN_ROOT")-$$"
  SNAPSHOT_DIR="$RUN_ROOT/source"
  OUTPUT_DIR="$RUN_ROOT/output"
  SUPPORT_TREE="$RUN_ROOT/support"
  EXPECTED_DIR="$RUN_ROOT/expected"
  mkdir -p "$OUTPUT_DIR" "$SUPPORT_TREE" "$EXPECTED_DIR"

  git -C "$REPO_ROOT" worktree add --detach --quiet "$SNAPSHOT_DIR" "$SOURCE_GIT_SHA"
  if [ "$(git -C "$SNAPSHOT_DIR" rev-parse HEAD)" != "$SOURCE_GIT_SHA" ] ||
     [ -n "$(git -C "$SNAPSHOT_DIR" status --porcelain)" ]; then
    fail "Could not create an immutable clean AICopilot source snapshot for $SOURCE_GIT_SHA."
  fi
  printf 'AICopilot fixed source snapshot: sha=%s path=%s runId=%s\n' "$SOURCE_GIT_SHA" "$SNAPSHOT_DIR" "$RUN_ID"
}

freeze_workspace_plan() {
  local frozen_plan="$EXPECTED_DIR/workspace-deploy-plan.json"
  if [ "$DRY_RUN" = true ] && [ -z "$WORKSPACE_PLAN_FILE" ]; then
    printf 'NON_PRODUCTION_MECHANISM_TEST productionEligible=false planFile=not-required-for-project-dry-run\n'
    return
  fi
  cp -p "$WORKSPACE_PLAN_FILE" "$frozen_plan"
  if [ "$(release_sha256_file "$frozen_plan")" != "$WORKSPACE_PLAN_DIGEST" ]; then
    fail "Frozen workspace deployment plan digest drifted while creating the run-private copy."
  fi
  printf 'AICopilot workspace plan consumed: digest=%s frozen=%s\n' "$WORKSPACE_PLAN_DIGEST" "$frozen_plan"
}

prepare_candidate_manifests() {
  local services_file="$EXPECTED_DIR/aicopilot-built-services.txt"

  CANDIDATE_SERVICES="$(normalize_candidate_services)"
  printf '%s\n' "$CANDIDATE_SERVICES" > "$services_file"
  SERVICES_MANIFEST_DIGEST="$(release_sha256_file "$services_file")"
  printf 'AICopilot run-private services candidate prepared: servicesDigest=%s\n' "$SERVICES_MANIFEST_DIGEST"
}

append_candidate_image_record() {
  local output_file="$1"
  local service="$2"
  local digest="$3"
  local key
  local image_name
  local repository
  local tagged_ref
  local immutable_ref

  [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Invalid immutable OCI digest for $service: $digest"
  key="$(image_key_for_candidate_service "$service")"
  image_name="$(image_name_for_candidate_service "$service")"
  repository="$REGISTRY/$HARBOR_PROJECT/$image_name"
  tagged_ref="$repository:sha-$SOURCE_GIT_SHA"
  immutable_ref="$repository@$digest"
  [[ "$immutable_ref" =~ ^[A-Za-z0-9._:/@-]+$ ]] || fail "AICopilot immutable image reference contains unsupported characters: $immutable_ref"
  printf '%s=%s\n' "$key" "$immutable_ref" >> "$output_file"
  printf '%s_TAG=%s\n' "$key" "$tagged_ref" >> "$output_file"
  printf '%s_DIGEST=%s\n' "$key" "$digest" >> "$output_file"
}

resolve_existing_candidate_images() {
  local images_file="$EXPECTED_DIR/aicopilot-images.env"
  local service
  local image_name
  local tagged_ref
  local digest

  : > "$images_file"
  command -v docker >/dev/null 2>&1 || { rm -f "$images_file"; return 1; }
  for service in $(printf '%s' "$CANDIDATE_SERVICES" | tr ',' ' '); do
    image_name="$(image_name_for_candidate_service "$service")"
    tagged_ref="$REGISTRY/$HARBOR_PROJECT/$image_name:sha-$SOURCE_GIT_SHA"
    if ! digest="$(docker buildx imagetools inspect "$tagged_ref" --format '{{.Manifest.Digest}}' 2>/dev/null)"; then
      rm -f "$images_file"
      IMAGE_MANIFEST_DIGEST=""
      return 1
    fi
    digest="$(printf '%s\n' "$digest" | tail -n 1 | awk '{$1=$1; print}')"
    if [[ ! "$digest" =~ ^sha256:[0-9a-f]{64}$ ]]; then
      rm -f "$images_file"
      IMAGE_MANIFEST_DIGEST=""
      return 1
    fi
    append_candidate_image_record "$images_file" "$service" "$digest"
  done
  IMAGE_MANIFEST_DIGEST="$(release_sha256_file "$images_file")"
  printf 'Resolved immutable OCI candidate images without rebuilding: imageDigest=%s\n' "$IMAGE_MANIFEST_DIGEST"
}

consume_run_private_build_manifests() {
  local services_file="$OUTPUT_DIR/aicopilot-built-services.txt"
  local images_file="$OUTPUT_DIR/aicopilot-images.env"
  local actual_services_digest
  local actual_image_digest
  local service
  local key
  local image_name
  local repository
  local expected_tag
  local immutable_ref
  local tagged_ref
  local digest

  [ -f "$services_file" ] || fail "Missing run-private built services file: $services_file"
  [ -f "$images_file" ] || fail "Missing run-private image manifest: $images_file"
  actual_services_digest="$(release_sha256_file "$services_file")"
  if [ "$actual_services_digest" != "$SERVICES_MANIFEST_DIGEST" ] ||
     ! cmp -s "$EXPECTED_DIR/aicopilot-built-services.txt" "$services_file"; then
    fail "Run-private built services manifest does not match the frozen deployment candidate."
  fi
  for service in $(printf '%s' "$CANDIDATE_SERVICES" | tr ',' ' '); do
    key="$(image_key_for_candidate_service "$service")"
    image_name="$(image_name_for_candidate_service "$service")"
    repository="$REGISTRY/$HARBOR_PROJECT/$image_name"
    expected_tag="$repository:sha-$SOURCE_GIT_SHA"
    immutable_ref="$(sed -n "s/^${key}=//p" "$images_file")"
    tagged_ref="$(sed -n "s/^${key}_TAG=//p" "$images_file")"
    digest="$(sed -n "s/^${key}_DIGEST=//p" "$images_file")"
    [ "$tagged_ref" = "$expected_tag" ] || fail "Run-private image manifest tag drifted for $key."
    [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]] || fail "Run-private image manifest has an invalid OCI digest for $key."
    [ "$immutable_ref" = "$repository@$digest" ] || fail "Run-private immutable image reference does not match its OCI digest for $key."
    [ "$(grep -c "^${key}=" "$images_file")" -eq 1 ] || fail "Run-private image manifest must contain exactly one $key record."
    [ "$(grep -c "^${key}_TAG=" "$images_file")" -eq 1 ] || fail "Run-private image manifest must contain exactly one ${key}_TAG record."
    [ "$(grep -c "^${key}_DIGEST=" "$images_file")" -eq 1 ] || fail "Run-private image manifest must contain exactly one ${key}_DIGEST record."
  done
  actual_image_digest="$(release_sha256_file "$images_file")"
  if [ -n "$IMAGE_MANIFEST_DIGEST" ]; then
    [ "$actual_image_digest" = "$IMAGE_MANIFEST_DIGEST" ] &&
      cmp -s "$EXPECTED_DIR/aicopilot-images.env" "$images_file" ||
      fail "A fixed-SHA image tag resolved to a different OCI digest after build."
  else
    cp -p "$images_file" "$EXPECTED_DIR/aicopilot-images.env"
    IMAGE_MANIFEST_DIGEST="$actual_image_digest"
  fi
  printf 'AICopilot run-private build manifests consumed: servicesDigest=%s imageDigest=%s\n' \
    "$actual_services_digest" "$IMAGE_MANIFEST_DIGEST"
}

candidate_remote_environment() {
  local service
  local key
  local value
  local tagged_value
  local digest_value
  local images_file="$EXPECTED_DIR/aicopilot-images.env"

  printf "IIOT_WORKSPACE_DEPLOY_ENTRYPOINT='1' "
  printf "IIOT_WORKSPACE_DEPLOY_INVOCATION_ID='%s' " "$WORKSPACE_INVOCATION_ID"
  printf "IIOT_WORKSPACE_DEPLOY_EXPECTED_SHA='%s' " "$WORKSPACE_EXPECTED_SHA"
  printf "IIOT_WORKSPACE_DEPLOY_PLAN_DIGEST='%s' " "$WORKSPACE_PLAN_DIGEST"
  printf "IIOT_WORKSPACE_DEPLOY_PROFILE_DIGEST='%s' " "$WORKSPACE_PROFILE_DIGEST"
  printf "DEPLOY_SERVICES_MANIFEST_DIGEST='%s' " "$SERVICES_MANIFEST_DIGEST"
  printf "DEPLOY_IMAGE_MANIFEST_DIGEST='%s' " "$IMAGE_MANIFEST_DIGEST"
  for service in $(printf '%s' "$CANDIDATE_SERVICES" | tr ',' ' '); do
    key="$(image_key_for_candidate_service "$service")"
    value="$(sed -n "s/^${key}=//p" "$images_file")"
    tagged_value="$(sed -n "s/^${key}_TAG=//p" "$images_file")"
    digest_value="$(sed -n "s/^${key}_DIGEST=//p" "$images_file")"
    [ -n "$value" ] || fail "Frozen image manifest is missing $key."
    printf "DEPLOY_CANDIDATE_%s='%s' " "$key" "$value"
    printf "DEPLOY_CANDIDATE_%s_TAG='%s' " "$key" "$tagged_value"
    printf "DEPLOY_CANDIDATE_%s_DIGEST='%s' " "$key" "$digest_value"
  done
}

prepare_support_release() {
  local source_deploy_dir="$SNAPSHOT_DIR/deploy/enterprise-ai"
  local relative_path
  local checksum
  local manifest="$SUPPORT_TREE/.aicopilot-support-manifest.sha256"
  local root_file

  for root_file in \
    docker-compose.yaml \
    deploy-release.sh \
    post-release-cleanup.sh \
    harbor-retention.sh \
    runner-platform-attestation.template.md; do
    [ -f "$source_deploy_dir/$root_file" ] || fail "Missing AICopilot support file in source snapshot: $root_file"
    mkdir -p "$(dirname "$SUPPORT_TREE/$root_file")"
    cp -p "$source_deploy_dir/$root_file" "$SUPPORT_TREE/$root_file"
  done

  while IFS= read -r relative_path; do
    relative_path="${relative_path#./}"
    mkdir -p "$(dirname "$SUPPORT_TREE/$relative_path")"
    cp -p "$source_deploy_dir/$relative_path" "$SUPPORT_TREE/$relative_path"
  done < <(cd "$source_deploy_dir" && find scripts cloud-readonly -type f -print | LC_ALL=C sort)

  : > "$manifest"
  while IFS= read -r relative_path; do
    relative_path="${relative_path#./}"
    checksum="$(release_sha256_file "$SUPPORT_TREE/$relative_path")"
    printf '%s  %s\n' "$checksum" "$relative_path" >> "$manifest"
  done < <(cd "$SUPPORT_TREE" && find . -type f ! -name '.aicopilot-support-manifest.sha256' -print | LC_ALL=C sort)
  SUPPORT_DIGEST="$(release_sha256_file "$manifest")"
  printf 'AICopilot support manifest prepared: digest=%s manifest=%s\n' "$SUPPORT_DIGEST" "$manifest"
}

check_remote_preflight() {
  local remote_command
  local status

  remote_command="cd '$REMOTE_DEPLOY_DIR' && test -r .env && test -w .env && test -w . && { test ! -e releases || { test -d releases && test -r releases && test -w releases && test -x releases; }; } && { test ! -e .locks || { test -d .locks && test -r .locks && test -w .locks && test -x .locks; }; }"
  if run_with_timeout "$SYNC_TIMEOUT_SECONDS" "AICopilot remote preflight" \
    ssh "${SSH_OPTIONS[@]}" "$SSH_TARGET" "$remote_command"; then
    return
  else
    status=$?
    print_deploy_diagnostics
    exit "$status"
  fi
}

check_remote_current_release() {
  local service_args="--services '$CANDIDATE_SERVICES'"
  local candidate_env
  local remote_command
  local status

  candidate_env="$(candidate_remote_environment)"
  remote_command="cd '$REMOTE_DEPLOY_DIR' && test -f .aicopilot-support-manifest.digest && test -f .aicopilot-support-manifest.sha256 && grep -q -- '--check-current' ./deploy-release.sh && test \"\$(sed -n '1p' .aicopilot-support-manifest.digest)\" = '$SUPPORT_DIGEST' && sha256sum -c .aicopilot-support-manifest.sha256 >/dev/null 2>&1 || exit 3; ${candidate_env}DEPLOY_GIT_SHA='$SOURCE_GIT_SHA' DEPLOY_TRIGGERED_BY=local DEPLOY_LOCK_TOKEN='check-$RUN_ID' EXPECTED_SUPPORT_DIGEST='$SUPPORT_DIGEST' ./deploy-release.sh 'sha-$SOURCE_GIT_SHA' --check-current $service_args"
  if run_with_timeout "$SYNC_TIMEOUT_SECONDS" "check current AICopilot release" \
    ssh "${SSH_OPTIONS[@]}" "$SSH_TARGET" "$remote_command"; then
    printf 'AICopilot fixed commit is already deployed and healthy; build/push/support sync skipped: sha=%s\n' "$SOURCE_GIT_SHA"
    return 0
  else
    status=$?
  fi

  if [ "$status" -eq 3 ]; then
    return 1
  fi
  print_deploy_diagnostics
  exit "$status"
}

build_and_push_release() {
  local build_args=()
  local status

  if [ "$REQUESTED_ALL" = true ]; then
    build_args+=(--all)
  else
    build_args+=(--services "$REQUESTED_SERVICES")
  fi
  build_args+=(--output-dir "$OUTPUT_DIR")
  if [ "$DRY_RUN" = true ]; then
    build_args+=(--dry-run)
  fi

  if AICOPILOT_RELEASE_SOURCE_SHA="$SOURCE_GIT_SHA" \
     AICOPILOT_RELEASE_OUTPUT_DIR="$OUTPUT_DIR" \
     "$SNAPSHOT_DIR/deploy/enterprise-ai/build-and-push.sh" "${build_args[@]}"; then
    return
  else
    status=$?
    exit "$status"
  fi
}

sync_remote_support_release() {
  local remote_staging="$REMOTE_DEPLOY_DIR/.support-staging/$RUN_ID"
  local remote_command
  local status

  remote_command="set -eu; umask 077; mkdir -p '$remote_staging'; tar --no-same-owner -xf - -C '$remote_staging'; bash '$remote_staging/scripts/install-support-release.sh' '$REMOTE_DEPLOY_DIR' '$remote_staging' '$RUN_ID' '$SUPPORT_DIGEST' '$LOCK_RESERVATION_TTL_SECONDS' '$WORKSPACE_ENTRYPOINT' '$WORKSPACE_INVOCATION_ID' '$WORKSPACE_EXPECTED_SHA' '$WORKSPACE_PLAN_DIGEST' '$SERVICES_MANIFEST_DIGEST' '$IMAGE_MANIFEST_DIGEST' '$WORKSPACE_PROFILE_DIGEST'"
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] install digest-bound AICopilot support release on %s:%s digest=%s token=%s\n' \
      "$SSH_TARGET" "$REMOTE_DEPLOY_DIR" "$SUPPORT_DIGEST" "$RUN_ID"
    return
  fi

  if run_with_timeout "$SYNC_TIMEOUT_SECONDS" "sync AICopilot support release" \
    bash -c '
      set -euo pipefail
      support_tree="$1"
      shift
      COPYFILE_DISABLE=1 tar -C "$support_tree" -cf - . | ssh "$@"
    ' bash "$SUPPORT_TREE" "${SSH_OPTIONS[@]}" "$SSH_TARGET" "$remote_command"; then
    REMOTE_RESERVATION_CREATED=true
    return
  else
    status=$?
    print_deploy_diagnostics
    exit "$status"
  fi
}

require_pushed_clean_head
create_fixed_source_snapshot
freeze_workspace_plan
prepare_support_release
prepare_candidate_manifests

if [ "$DRY_RUN" != true ]; then
  check_remote_preflight
  if [ "${AICOPILOT_FORCE_BUILD:-false}" != "true" ] && resolve_existing_candidate_images; then
    if check_remote_current_release; then
      exit 0
    fi
  fi
fi
build_and_push_release
consume_run_private_build_manifests
sync_remote_support_release

SERVICES_FILE="$OUTPUT_DIR/aicopilot-built-services.txt"
DEPLOY_SERVICES="$(tr -d '\r\n' < "$SERVICES_FILE")"
[ -n "$DEPLOY_SERVICES" ] || fail "Run-private built services file is empty: $SERVICES_FILE"

TAG="sha-$SOURCE_GIT_SHA"
CANDIDATE_REMOTE_ENV="$(candidate_remote_environment)"
REMOTE_COMMAND="cd '$REMOTE_DEPLOY_DIR' && ${CANDIDATE_REMOTE_ENV}DEPLOY_GIT_SHA='$SOURCE_GIT_SHA' DEPLOY_TRIGGERED_BY=local DEPLOY_LOCK_TOKEN='$RUN_ID' EXPECTED_SUPPORT_DIGEST='$SUPPORT_DIGEST' ./deploy-release.sh '$TAG' --services '$DEPLOY_SERVICES'"

printf '\nAICopilot local deploy command:\n'
printf 'ssh'
printf ' %q' "${SSH_OPTIONS[@]}" "$SSH_TARGET" "$REMOTE_COMMAND"
printf '\n'

if run_with_timeout "$SSH_TIMEOUT_SECONDS" "ssh AICopilot deploy-release" \
  ssh "${SSH_OPTIONS[@]}" "$SSH_TARGET" "$REMOTE_COMMAND"; then
  REMOTE_RESERVATION_CREATED=false
else
  status=$?
  print_deploy_diagnostics
  exit "$status"
fi

if [ "$DRY_RUN" = true ]; then
  printf 'NON_PRODUCTION_MECHANISM_TEST productionEligible=false result=dry-run-complete\n'
fi
