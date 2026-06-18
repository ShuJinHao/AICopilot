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

if [ "${MIRROR_BASE_IMAGES:-false}" = "true" ]; then
  "$SCRIPT_DIR/mirror-base-images.sh"
fi

publish_dotnet_image() {
  local project_path="$1"
  local image_name="$2"

  dotnet publish "$REPO_ROOT/$project_path" \
    -c Release \
    --os linux \
    --arch x64 \
    /t:PublishContainer \
    -p:ContainerRegistry="$REGISTRY" \
    -p:ContainerRepository="$HARBOR_PROJECT/$image_name" \
    -p:ContainerImageTag="$TAG"
}

publish_dotnet_image "src/hosts/AICopilot.HttpApi/AICopilot.HttpApi.csproj" "aicopilot-httpapi"
publish_dotnet_image "src/hosts/AICopilot.MigrationWorkApp/AICopilot.MigrationWorkApp.csproj" "aicopilot-migration"
publish_dotnet_image "src/hosts/AICopilot.DataWorker/AICopilot.DataWorker.csproj" "aicopilot-dataworker"
publish_dotnet_image "src/hosts/AICopilot.RagWorker/AICopilot.RagWorker.csproj" "aicopilot-ragworker"

docker buildx build \
  --platform "$PLATFORM" \
  --build-arg VITE_CLOUD_PLATFORM_URL="$CLOUD_PLATFORM_URL" \
  --build-arg NODE_BASE_IMAGE="$NODE_BASE_IMAGE" \
  --build-arg NGINX_BASE_IMAGE="$NGINX_BASE_IMAGE" \
  --tag "$IMAGE_PREFIX/aicopilot-webui:$TAG" \
  --push \
  "$REPO_ROOT/src/vues/AICopilot.Web"

cat <<EOF
AICOPILOT_HTTPAPI_IMAGE=$IMAGE_PREFIX/aicopilot-httpapi:$TAG
AICOPILOT_MIGRATION_IMAGE=$IMAGE_PREFIX/aicopilot-migration:$TAG
AICOPILOT_DATAWORKER_IMAGE=$IMAGE_PREFIX/aicopilot-dataworker:$TAG
AICOPILOT_RAGWORKER_IMAGE=$IMAGE_PREFIX/aicopilot-ragworker:$TAG
AICOPILOT_WEBUI_IMAGE=$IMAGE_PREFIX/aicopilot-webui:$TAG
EOF
