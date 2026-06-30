#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

REQUESTED_SERVICES=""
REQUESTED_ALL=false
DRY_RUN=false
SSH_TARGET="${DEPLOY_SSH_TARGET:-}"
REMOTE_DEPLOY_DIR="${REMOTE_DEPLOY_DIR:-/srv/enterprise-ai/deploy}"
SSH_TIMEOUT_SECONDS="${SSH_TIMEOUT_SECONDS:-1800}"
SYNC_TIMEOUT_SECONDS="${SYNC_TIMEOUT_SECONDS:-120}"

usage() {
  cat <<'EOF'
Usage:
  deploy/enterprise-ai/local-release.sh --services httpapi,web --ssh-target root@10.98.90.154
  deploy/enterprise-ai/local-release.sh --all --ssh-target root@10.98.90.154

Builds selected AICopilot images locally, pushes Harbor tags, then SSH-triggers
the server-side deploy-release.sh entrypoint.
EOF
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
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

run_with_timeout() {
  local seconds="$1"
  local label="$2"
  shift 2
  local marker
  local cmd_pid
  local timer_pid
  local exit_code
  marker="$(mktemp)"
  rm -f "$marker"

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] %s:' "$label"
    printf ' %q' "$@"
    printf '\n'
    return 0
  fi

  "$@" &
  cmd_pid=$!
  (
    sleep "$seconds"
    if kill -0 "$cmd_pid" 2>/dev/null; then
      printf 'Timed out after %s seconds: %s\n' "$seconds" "$label" >&2
      : > "$marker"
      kill -TERM "$cmd_pid" 2>/dev/null || true
      sleep 5
      kill -KILL "$cmd_pid" 2>/dev/null || true
    fi
  ) &
  timer_pid=$!

  set +e
  wait "$cmd_pid"
  exit_code=$?
  set -e
  kill "$timer_pid" 2>/dev/null || true
  wait "$timer_pid" 2>/dev/null || true

  if [ -f "$marker" ]; then
    rm -f "$marker"
    return 124
  fi
  rm -f "$marker"
  return "$exit_code"
}

print_deploy_diagnostics() {
  cat >&2 <<EOF

AICopilot SSH deploy failed or timed out.
Diagnostics to run before retrying:
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && docker compose --env-file .env -f docker-compose.yaml ps'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && tail -n 200 releases/current-release.summary.md'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && ls -l releases'
  ssh $SSH_TARGET 'cd $REMOTE_DEPLOY_DIR && docker compose --env-file .env -f docker-compose.yaml logs --tail=200 aicopilot-httpapi aicopilot-dataworker aicopilot-ragworker'
  docker buildx ls
  docker system df
EOF
}

sync_remote_deploy_files() {
  local files=(
    deploy-release.sh
    post-release-cleanup.sh
    harbor-retention.sh
    cloud-readonly
    scripts/apply-cloud-readonly-grants.sh
    scripts/check-cloud-readonly-grants.sh
  )
  local remote_command="mkdir -p '$REMOTE_DEPLOY_DIR' && cd '$REMOTE_DEPLOY_DIR' && tar -xf -"

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] sync AICopilot deploy support files to %s:%s\n' "$SSH_TARGET" "$REMOTE_DEPLOY_DIR"
    printf '[dry-run] tar -C %q -cf -' "$SCRIPT_DIR"
    printf ' %q' "${files[@]}"
    printf ' | ssh %q %q\n' "$SSH_TARGET" "$remote_command"
    return
  fi

  if ! run_with_timeout "$SYNC_TIMEOUT_SECONDS" "sync AICopilot deploy support files" \
    bash -c '
      set -euo pipefail
      script_dir="$1"
      ssh_target="$2"
      remote_command="$3"
      shift 3
      tar -C "$script_dir" -cf - "$@" | ssh "$ssh_target" "$remote_command"
    ' bash "$SCRIPT_DIR" "$SSH_TARGET" "$remote_command" "${files[@]}"; then
    print_deploy_diagnostics
    exit 124
  fi
}

require_pushed_clean_head() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] skip clean/pushed HEAD enforcement.\n'
    return
  fi

  if [ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]; then
    git -C "$REPO_ROOT" status --short >&2
    fail "AICopilot local release requires a clean worktree."
  fi

  local sha
  local remote
  sha="$(git -C "$REPO_ROOT" rev-parse HEAD)"
  remote="${GIT_REMOTE:-origin}"
  git -C "$REPO_ROOT" fetch --quiet "$remote" '+refs/heads/*:refs/remotes/'"$remote"'/*'
  if ! git -C "$REPO_ROOT" branch -r --contains "$sha" | grep -q "$remote/"; then
    fail "HEAD $sha is not present on remote $remote. Push to GitHub before production release."
  fi
}

TAG="sha-$(git -C "$REPO_ROOT" rev-parse HEAD)"
BUILD_ARGS=()
if [ "$REQUESTED_ALL" = true ]; then
  BUILD_ARGS+=(--all)
else
  BUILD_ARGS+=(--services "$REQUESTED_SERVICES")
fi
if [ "$DRY_RUN" = true ]; then
  BUILD_ARGS+=(--dry-run)
fi

require_pushed_clean_head
"$SCRIPT_DIR/build-and-push.sh" "${BUILD_ARGS[@]}"
sync_remote_deploy_files

SERVICES_FILE="$REPO_ROOT/artifacts/deploy/aicopilot-built-services.txt"
if [ ! -f "$SERVICES_FILE" ]; then
  fail "Missing built services file: $SERVICES_FILE"
fi
DEPLOY_SERVICES="$(tr -d '\r\n' < "$SERVICES_FILE")"
[ -n "$DEPLOY_SERVICES" ] || fail "Built services file is empty: $SERVICES_FILE"

REMOTE_COMMAND="cd '$REMOTE_DEPLOY_DIR' && DEPLOY_GIT_SHA='${TAG#sha-}' DEPLOY_TRIGGERED_BY=local ./deploy-release.sh '$TAG' --services '$DEPLOY_SERVICES'"

printf '\nAICopilot local deploy command:\n'
printf 'ssh %s %q\n' "$SSH_TARGET" "$REMOTE_COMMAND"

if ! run_with_timeout "$SSH_TIMEOUT_SECONDS" "ssh AICopilot deploy-release" \
  ssh "$SSH_TARGET" "$REMOTE_COMMAND"; then
  print_deploy_diagnostics
  exit 124
fi
