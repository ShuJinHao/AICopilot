#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

REGISTRY="${REGISTRY:-10.98.90.154:80}"
HARBOR_PROJECT="${HARBOR_PROJECT:-enterprise-ai}"
TAG="${TAG:-sha-$(git -C "$REPO_ROOT" rev-parse HEAD)}"
PLATFORM="${PLATFORM:-linux/amd64}"
CLOUD_PLATFORM_URL="${CLOUD_PLATFORM_URL:-http://10.98.90.154:81}"
IMAGE_PREFIX="$REGISTRY/$HARBOR_PROJECT"
BASE_IMAGE_PREFIX="${BASE_IMAGE_PREFIX:-$IMAGE_PREFIX}"
NODE_BASE_IMAGE="${NODE_BASE_IMAGE:-$BASE_IMAGE_PREFIX/base-node:22-alpine}"
NGINX_BASE_IMAGE="${NGINX_BASE_IMAGE:-$BASE_IMAGE_PREFIX/base-nginx:1.27-alpine}"
DOTNET_RUNTIME_BASE_IMAGE="${DOTNET_RUNTIME_BASE_IMAGE:-$BASE_IMAGE_PREFIX/base-dotnet-aspnet:10.0-noble}"
BUILD_TIMEOUT_SECONDS="${BUILD_TIMEOUT_SECONDS:-900}"
HARBOR_TIMEOUT_SECONDS="${HARBOR_TIMEOUT_SECONDS:-120}"
DRY_RUN=false
REQUESTED_SERVICES=""
REQUESTED_ALL=false

usage() {
  cat <<'EOF'
Usage:
  deploy/enterprise-ai/build-and-push.sh --services httpapi,migration,dataworker,ragworker,web [--dry-run]
  deploy/enterprise-ai/build-and-push.sh --all [--dry-run]

Builds selected AICopilot application images locally and pushes them to Harbor.
Production use must pass either --services or --all explicitly.
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
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown build-and-push option: $1"
      ;;
  esac
  shift
done

if [ "$REQUESTED_ALL" = true ] && [ -n "$REQUESTED_SERVICES" ]; then
  fail "Use either --all or --services, not both."
fi
if [ "$REQUESTED_ALL" != true ] && [ -z "$REQUESTED_SERVICES" ]; then
  fail "AICopilot local image build requires explicit --services or --all."
fi

normalize_services() {
  local services_input="${1:-}"
  local normalized=""
  local service
  local item

  if [ "$REQUESTED_ALL" = true ]; then
    printf '%s\n' "httpapi migration dataworker ragworker web"
    return
  fi

  for item in $(printf '%s' "$services_input" | tr ',' ' '); do
    case "$item" in
      httpapi|aicopilot-httpapi)
        service=httpapi
        ;;
      migration|aicopilot-migration)
        service=migration
        ;;
      dataworker|aicopilot-dataworker)
        service=dataworker
        ;;
      ragworker|aicopilot-ragworker)
        service=ragworker
        ;;
      web|webui|aicopilot-webui)
        service=web
        ;;
      *)
        fail "Unsupported AICopilot image service: $item"
        ;;
    esac

    case " $normalized " in
      *" $service "*)
        ;;
      *)
        normalized="$normalized $service"
        ;;
    esac
  done

  normalized="$(printf '%s' "$normalized" | awk '{$1=$1; print}')"
  [ -n "$normalized" ] || fail "No AICopilot image services were selected."
  printf '%s\n' "$normalized"
}

service_csv() {
  printf '%s' "$1" | awk '{$1=$1; gsub(/ /, ","); print}'
}

print_diagnostics() {
  local service="${1:-}"
  local image_name="${2:-}"
  cat >&2 <<EOF

Local AICopilot image build failed or timed out.
Diagnostics to run before retrying:
  docker buildx ls
  docker system df
  docker images '${IMAGE_PREFIX}/*'
  curl -fsS 'http://${REGISTRY}/api/v2.0/projects/${HARBOR_PROJECT}/repositories/${image_name:-<repository>}/artifacts?with_tag=true&page_size=20'
  git status --short
  git rev-parse HEAD

Context:
  service=${service:-unknown}
  image=${image_name:-unknown}
  tag=$TAG
  timeout_seconds=$BUILD_TIMEOUT_SECONDS
EOF
}

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

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "Required command not found: $1"
}

validate_local_tools() {
  require_command git
  require_command docker
  docker buildx version >/dev/null 2>&1 || fail "docker buildx is not available."
  if [ -n "${HARBOR_USERNAME:-${OCI_REGISTRY_USERNAME:-}}" ] && [ -n "${HARBOR_PASSWORD:-${OCI_REGISTRY_PASSWORD:-}}" ]; then
    local username="${HARBOR_USERNAME:-$OCI_REGISTRY_USERNAME}"
    local password="${HARBOR_PASSWORD:-$OCI_REGISTRY_PASSWORD}"
    if [ "$DRY_RUN" = true ]; then
      printf '[dry-run] docker login %s --username %s --password-stdin\n' "$REGISTRY" "$username"
    else
      printf '%s\n' "$password" | run_with_timeout "$HARBOR_TIMEOUT_SECONDS" "docker login $REGISTRY" \
        docker login "$REGISTRY" --username "$username" --password-stdin
    fi
  else
    printf 'Harbor credentials were not provided; using existing Docker login for %s.\n' "$REGISTRY"
  fi
}

image_name_for_service() {
  case "$1" in
    httpapi) printf '%s\n' aicopilot-httpapi ;;
    migration) printf '%s\n' aicopilot-migration ;;
    dataworker) printf '%s\n' aicopilot-dataworker ;;
    ragworker) printf '%s\n' aicopilot-ragworker ;;
    web) printf '%s\n' aicopilot-webui ;;
    *) fail "Unsupported AICopilot image service: $1" ;;
  esac
}

env_key_for_service() {
  case "$1" in
    httpapi) printf '%s\n' AICOPILOT_HTTPAPI_IMAGE ;;
    migration) printf '%s\n' AICOPILOT_MIGRATION_IMAGE ;;
    dataworker) printf '%s\n' AICOPILOT_DATAWORKER_IMAGE ;;
    ragworker) printf '%s\n' AICOPILOT_RAGWORKER_IMAGE ;;
    web) printf '%s\n' AICOPILOT_WEBUI_IMAGE ;;
    *) fail "Unsupported AICopilot image service: $1" ;;
  esac
}

publish_dotnet_image() {
  local service="$1"
  local project_path="$2"
  local image_name="$3"
  local app_dll="$4"
  local publish_dir="$REPO_ROOT/artifacts/container-publish/$image_name"

  printf 'Building AICopilot backend image: service=%s image=%s/%s:%s\n' "$service" "$IMAGE_PREFIX" "$image_name" "$TAG"

  if [ "$DRY_RUN" != true ]; then
    rm -rf "$publish_dir"
    mkdir -p "$publish_dir"
  fi

  if ! run_with_timeout "$BUILD_TIMEOUT_SECONDS" "dotnet publish $service" \
    dotnet publish "$REPO_ROOT/$project_path" \
      -c Release \
      --os linux \
      --arch x64 \
      --self-contained false \
      -p:UseAppHost=false \
      -o "$publish_dir"; then
    print_diagnostics "$service" "$image_name"
    exit 124
  fi

  if ! run_with_timeout "$BUILD_TIMEOUT_SECONDS" "build/push $service" \
    docker buildx build \
      --platform "$PLATFORM" \
      --build-arg RUNTIME_BASE_IMAGE="$DOTNET_RUNTIME_BASE_IMAGE" \
      --build-arg APP_DLL="$app_dll" \
      --tag "$IMAGE_PREFIX/$image_name:$TAG" \
      --push \
      --file "$SCRIPT_DIR/Dockerfile.backend-runtime" \
      "$publish_dir"; then
    print_diagnostics "$service" "$image_name"
    exit 124
  fi
}

publish_web_image() {
  local image_name="aicopilot-webui"
  printf 'Building AICopilot web image: image=%s/%s:%s\n' "$IMAGE_PREFIX" "$image_name" "$TAG"
  if ! run_with_timeout "$BUILD_TIMEOUT_SECONDS" "build/push web" \
    docker buildx build \
      --platform "$PLATFORM" \
      --build-arg VITE_CLOUD_PLATFORM_URL="$CLOUD_PLATFORM_URL" \
      --build-arg NODE_BASE_IMAGE="$NODE_BASE_IMAGE" \
      --build-arg NGINX_BASE_IMAGE="$NGINX_BASE_IMAGE" \
      --tag "$IMAGE_PREFIX/$image_name:$TAG" \
      --push \
      "$REPO_ROOT/src/vues/AICopilot.Web"; then
    print_diagnostics web "$image_name"
    exit 124
  fi
}

build_and_push_service() {
  case "$1" in
    httpapi)
      publish_dotnet_image httpapi "src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj" "aicopilot-httpapi" "AICopilot.HttpApi.dll"
      ;;
    migration)
      publish_dotnet_image migration "src/hosts/AICopilot.MigrationWorkApp/AICopilot.MigrationWorkApp.csproj" "aicopilot-migration" "AICopilot.MigrationWorkApp.dll"
      ;;
    dataworker)
      publish_dotnet_image dataworker "src/hosts/AICopilot.DataWorker/AICopilot.DataWorker.csproj" "aicopilot-dataworker" "AICopilot.DataWorker.dll"
      ;;
    ragworker)
      publish_dotnet_image ragworker "src/hosts/AICopilot.RagWorker/AICopilot.RagWorker.csproj" "aicopilot-ragworker" "AICopilot.RagWorker.dll"
      ;;
    web)
      publish_web_image
      ;;
    *)
      fail "Unsupported AICopilot image service: $1"
      ;;
  esac
}

emit_outputs() {
  local services="$1"
  local services_csv="$2"
  local artifact_dir="$REPO_ROOT/artifacts/deploy"
  local services_file="$artifact_dir/aicopilot-built-services.txt"
  local images_file="$artifact_dir/aicopilot-images.env"
  mkdir -p "$artifact_dir"
  printf '%s\n' "$services_csv" > "$services_file"
  : > "$images_file"

  printf '\nRelease tag: %s\n' "$TAG"
  printf 'Deploy services input: %s\n' "$services_csv"
  for service in $services; do
    local key
    local image_name
    key="$(env_key_for_service "$service")"
    image_name="$(image_name_for_service "$service")"
    printf '%s=%s/%s:%s\n' "$key" "$IMAGE_PREFIX" "$image_name" "$TAG" | tee -a "$images_file"
  done
  printf 'Built services file: %s\n' "$services_file"
  printf 'Image manifest file: %s\n' "$images_file"
}

if [ "${MIRROR_BASE_IMAGES:-false}" = "true" ]; then
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] %q\n' "$SCRIPT_DIR/mirror-base-images.sh"
  else
    "$SCRIPT_DIR/mirror-base-images.sh"
  fi
fi

SELECTED_SERVICES="$(normalize_services "$REQUESTED_SERVICES")"
SELECTED_SERVICES_CSV="$(service_csv "$SELECTED_SERVICES")"

validate_local_tools

for service in $SELECTED_SERVICES; do
  build_and_push_service "$service"
done

emit_outputs "$SELECTED_SERVICES" "$SELECTED_SERVICES_CSV"
