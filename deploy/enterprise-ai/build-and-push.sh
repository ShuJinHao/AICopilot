#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

REGISTRY="${REGISTRY:-10.98.90.154:80}"
HARBOR_PROJECT="${HARBOR_PROJECT:-enterprise-ai}"
TAG="${TAG:-sha-$(git -C "$REPO_ROOT" rev-parse --short=16 HEAD)}"
PLATFORM="${PLATFORM:-linux/amd64}"
CLOUD_PLATFORM_URL="${CLOUD_PLATFORM_URL:-http://10.98.90.154:81}"
IMAGE_PREFIX="$REGISTRY/$HARBOR_PROJECT"
BASE_IMAGE_PREFIX="${BASE_IMAGE_PREFIX:-$IMAGE_PREFIX}"
NODE_BASE_IMAGE="${NODE_BASE_IMAGE:-$BASE_IMAGE_PREFIX/base-node:22-alpine}"
NGINX_BASE_IMAGE="${NGINX_BASE_IMAGE:-$BASE_IMAGE_PREFIX/base-nginx:1.27-alpine}"
DOTNET_RUNTIME_BASE_IMAGE="${DOTNET_RUNTIME_BASE_IMAGE:-$BASE_IMAGE_PREFIX/base-dotnet-aspnet:10.0-noble}"
HARBOR_RETENTION_ENABLED="${HARBOR_RETENTION_ENABLED:-true}"
HARBOR_KEEP_SHA_TAGS="${HARBOR_KEEP_SHA_TAGS:-3}"

if [ "${MIRROR_BASE_IMAGES:-false}" = "true" ]; then
  "$SCRIPT_DIR/mirror-base-images.sh"
fi

publish_dotnet_image() {
  local project_path="$1"
  local image_name="$2"
  local app_dll="$3"
  local publish_dir="$REPO_ROOT/artifacts/container-publish/$image_name"

  rm -rf "$publish_dir"
  mkdir -p "$publish_dir"
  dotnet publish "$REPO_ROOT/$project_path" \
    -c Release \
    --os linux \
    --arch x64 \
    --self-contained false \
    -p:UseAppHost=false \
    -o "$publish_dir"

  docker buildx build \
    --platform "$PLATFORM" \
    --build-arg RUNTIME_BASE_IMAGE="$DOTNET_RUNTIME_BASE_IMAGE" \
    --build-arg APP_DLL="$app_dll" \
    --tag "$IMAGE_PREFIX/$image_name:$TAG" \
    --push \
    --file "$SCRIPT_DIR/Dockerfile.backend-runtime" \
    "$publish_dir"
}

publish_dotnet_image "src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj" "aicopilot-httpapi" "AICopilot.HttpApi.dll"
publish_dotnet_image "src/hosts/AICopilot.MigrationWorkApp/AICopilot.MigrationWorkApp.csproj" "aicopilot-migration" "AICopilot.MigrationWorkApp.dll"
publish_dotnet_image "src/hosts/AICopilot.DataWorker/AICopilot.DataWorker.csproj" "aicopilot-dataworker" "AICopilot.DataWorker.dll"
publish_dotnet_image "src/hosts/AICopilot.RagWorker/AICopilot.RagWorker.csproj" "aicopilot-ragworker" "AICopilot.RagWorker.dll"

docker buildx build \
  --platform "$PLATFORM" \
  --build-arg VITE_CLOUD_PLATFORM_URL="$CLOUD_PLATFORM_URL" \
  --build-arg NODE_BASE_IMAGE="$NODE_BASE_IMAGE" \
  --build-arg NGINX_BASE_IMAGE="$NGINX_BASE_IMAGE" \
  --tag "$IMAGE_PREFIX/aicopilot-webui:$TAG" \
  --push \
  "$REPO_ROOT/src/vues/AICopilot.Web"

if [ "$HARBOR_RETENTION_ENABLED" = "true" ]; then
  HARBOR_URL="${HARBOR_URL:-$REGISTRY}" \
    HARBOR_PROJECT="$HARBOR_PROJECT" \
    HARBOR_KEEP_SHA_TAGS="$HARBOR_KEEP_SHA_TAGS" \
    "$SCRIPT_DIR/harbor-retention.sh" \
      aicopilot-httpapi \
      aicopilot-migration \
      aicopilot-dataworker \
      aicopilot-ragworker \
      aicopilot-webui
fi

cat <<EOF
AICOPILOT_HTTPAPI_IMAGE=$IMAGE_PREFIX/aicopilot-httpapi:$TAG
AICOPILOT_MIGRATION_IMAGE=$IMAGE_PREFIX/aicopilot-migration:$TAG
AICOPILOT_DATAWORKER_IMAGE=$IMAGE_PREFIX/aicopilot-dataworker:$TAG
AICOPILOT_RAGWORKER_IMAGE=$IMAGE_PREFIX/aicopilot-ragworker:$TAG
AICOPILOT_WEBUI_IMAGE=$IMAGE_PREFIX/aicopilot-webui:$TAG
EOF
