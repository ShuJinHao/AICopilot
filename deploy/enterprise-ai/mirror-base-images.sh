#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

REGISTRY="${REGISTRY:-10.98.90.154:80}"
HARBOR_PROJECT="${HARBOR_PROJECT:-enterprise-ai}"
PLATFORM="${PLATFORM:-linux/amd64}"
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

  printf 'Mirrored %s -> %s\n' "$source_image" "$IMAGE_PREFIX/$target_image"
}

docker version >/dev/null
docker buildx version >/dev/null

mirror_base_image "postgres:17.6" "base-postgres:17.6"
mirror_base_image "rabbitmq:4.2-management" "base-rabbitmq:4.2-management"
mirror_base_image "qdrant/qdrant:v1.15.5" "base-qdrant:v1.15.5"
mirror_base_image "node:22-alpine" "base-node:22-alpine"
mirror_base_image "nginx:1.27-alpine" "base-nginx:1.27-alpine"
