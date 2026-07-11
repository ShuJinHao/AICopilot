#!/usr/bin/env bash
set -euo pipefail

EXPECTED_WORK_ROOT="${AICOPILOT_RUNNER_WORK_ROOT:-/data/github-runner/aicopilot}"
EXPECTED_DOCKER_ROOT="${AICOPILOT_DOCKER_ROOT:-/data/docker}"
EXPECTED_DEPLOY_TARGET_DIR="${AICOPILOT_DEPLOY_TARGET_DIR:-/srv/enterprise-ai/deploy}"
DRY_RUN=false

usage() {
  cat <<'USAGE'
Usage:
  deploy/enterprise-ai/scripts/check-runner-security-attestation.sh [options]

Checks the local self-hosted runner machine invariants for AICopilot disaster
workflows. This script only proves machine-local facts. GitHub production
environment reviewers/secrets and OIDC/Vault or equivalent short-lived
credential rollout must still be attested by the platform owner.

Options:
  --work-root <path>      Expected runner work root. Default: /data/github-runner/aicopilot.
  --docker-root <path>    Expected Docker Root Dir. Default: /data/docker.
  --deploy-dir <path>     Expected AICopilot deploy target. Default: /srv/enterprise-ai/deploy.
  --dry-run               Print checks without inspecting local Docker.
  --help                  Show this help.
USAGE
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
}

warn() {
  printf 'WARN: %s\n' "$*" >&2
}

canonical_existing_path() {
  local path="$1"
  if [ -d "$path" ]; then
    (cd "$path" && pwd -P)
  else
    printf '%s\n' "$path"
  fi
}

path_is_under() {
  local path="$1"
  local root="$2"

  case "$path" in
    "$root"|"$root"/*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

check_not_root() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] check current user is not root.\n'
    return
  fi

  local uid
  uid="$(id -u)"
  if [ "$uid" -eq 0 ]; then
    fail "AICopilot self-hosted runner must not run as root."
  fi

  printf 'Runner user attestation passed: uid=%s\n' "$uid"
}

check_workspace_root() {
  local workspace
  local expected_root
  workspace="${RUNNER_WORKSPACE:-${GITHUB_WORKSPACE:-}}"
  expected_root="$(canonical_existing_path "$EXPECTED_WORK_ROOT")"

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] check runner work root %s exists, is writable, and GitHub workspace is under it when workspace env is set.\n' "$expected_root"
    return
  fi

  if [ ! -d "$expected_root" ]; then
    fail "AICopilot runner work root must exist: $expected_root"
  fi

  if [ ! -w "$expected_root" ]; then
    fail "AICopilot runner work root must be writable by the runner user: $expected_root"
  fi

  if [ -z "$workspace" ]; then
    printf 'Runner work root attestation passed: workRoot=%s (workspace env not set)\n' "$expected_root"
    return
  fi

  workspace="$(canonical_existing_path "$workspace")"

  if ! path_is_under "$workspace" "$expected_root"; then
    fail "AICopilot runner workspace must be under $expected_root; actual=$workspace"
  fi

  printf 'Runner workspace attestation passed: workspace=%s\n' "$workspace"
}

check_docker_root() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] docker info --format {{.DockerRootDir}} and require %s.\n' "$EXPECTED_DOCKER_ROOT"
    return
  fi

  command -v docker >/dev/null || fail "Required command not found: docker"

  local docker_root
  local expected_root
  docker_root="$(docker info --format '{{.DockerRootDir}}')"
  expected_root="$(canonical_existing_path "$EXPECTED_DOCKER_ROOT")"

  if ! path_is_under "$docker_root" "$expected_root"; then
    fail "AICopilot runner Docker Root Dir must be under $expected_root; actual=$docker_root"
  fi

  printf 'Runner Docker root attestation passed: dockerRoot=%s\n' "$docker_root"
}

check_deploy_target_dir() {
  local deploy_dir
  deploy_dir="${DEPLOY_TARGET_DIR:-$EXPECTED_DEPLOY_TARGET_DIR}"

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] check deploy target dir resolves to %s.\n' "$deploy_dir"
    return
  fi

  if [ "$deploy_dir" != "$EXPECTED_DEPLOY_TARGET_DIR" ]; then
    fail "AICopilot DEPLOY_TARGET_DIR must be $EXPECTED_DEPLOY_TARGET_DIR; actual=$deploy_dir"
  fi

  printf 'Runner deploy target attestation passed: deployDir=%s\n' "$deploy_dir"
}

print_external_attestation_reminder() {
  cat <<'REMINDER'
Manual platform attestations still required:
  - GitHub production environment secrets are restricted to AICopilot production workflows and required reviewers.
  - Disaster workflows keep permissions: contents: read and use the self-hosted iiot-linux-prod label.
  - Runner service account filesystem permissions are limited to runner work root, Docker access, and deploy support paths.
  - OIDC/Vault or an equivalent short-lived credential design is either implemented or explicitly tracked as an open infrastructure task.
  - Fill runner-platform-attestation.template.md and lint the filled record with scripts/check-platform-attestation-record.sh.
REMINDER
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --work-root)
      shift
      EXPECTED_WORK_ROOT="${1:-}"
      ;;
    --work-root=*)
      EXPECTED_WORK_ROOT="${1#--work-root=}"
      ;;
    --docker-root)
      shift
      EXPECTED_DOCKER_ROOT="${1:-}"
      ;;
    --docker-root=*)
      EXPECTED_DOCKER_ROOT="${1#--docker-root=}"
      ;;
    --deploy-dir)
      shift
      EXPECTED_DEPLOY_TARGET_DIR="${1:-}"
      ;;
    --deploy-dir=*)
      EXPECTED_DEPLOY_TARGET_DIR="${1#--deploy-dir=}"
      ;;
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown check-runner-security-attestation option: $1"
      ;;
  esac
  shift
done

[ -n "$EXPECTED_WORK_ROOT" ] || fail "--work-root cannot be empty."
[ -n "$EXPECTED_DOCKER_ROOT" ] || fail "--docker-root cannot be empty."
[ -n "$EXPECTED_DEPLOY_TARGET_DIR" ] || fail "--deploy-dir cannot be empty."

printf 'Running AICopilot runner security attestation: workRoot=%s dockerRoot=%s deployDir=%s\n' \
  "$EXPECTED_WORK_ROOT" "$EXPECTED_DOCKER_ROOT" "$EXPECTED_DEPLOY_TARGET_DIR"

check_not_root
check_workspace_root
check_docker_root
check_deploy_target_dir
print_external_attestation_reminder

if [ "$DRY_RUN" = true ]; then
  warn "Dry-run does not prove runner filesystem, Docker root, GitHub environment, or Vault/OIDC state."
fi

printf 'AICopilot runner security attestation completed.\n'
