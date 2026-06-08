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

mirror_base_image() {
  local source_image="$1"
  local target_image="$2"

  printf 'FROM %s\n' "$source_image" \
    | docker buildx build \
      --platform "$PLATFORM" \
      --pull \
      --push \
      --tag "$IMAGE_PREFIX/$target_image" \
      -
}

mirror_base_image "postgres:17.6" "base-postgres:17.6"
mirror_base_image "rabbitmq:4.2-management" "base-rabbitmq:4.2-management"
mirror_base_image "qdrant/qdrant:v1.15.5" "base-qdrant:v1.15.5"

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
