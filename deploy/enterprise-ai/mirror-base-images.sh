#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

REGISTRY="${REGISTRY:-}"
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

mirror_dotnet_runtime_base_image() {
  local source_image="$1"
  local target_image="$2"

  docker buildx build \
    --platform "$PLATFORM" \
    --pull \
    --push \
    --tag "$IMAGE_PREFIX/$target_image" \
    - <<EOF
FROM $source_image
RUN apt-get -o Acquire::Retries=5 update \
    && DEBIAN_FRONTEND=noninteractive apt-get -o Acquire::Retries=5 install -y --no-install-recommends \
        libgssapi-krb5-2 \
        tzdata \
    && mkdir -p /app /var/lib/aicopilot/storage /var/lib/aicopilot/artifact-workspaces \
    && chown -R app:app /app /var/lib/aicopilot \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
USER app
EOF

  printf 'Mirrored hardened %s -> %s\n' "$source_image" "$IMAGE_PREFIX/$target_image"
}

docker version >/dev/null
docker buildx version >/dev/null
[ -n "$REGISTRY" ] || {
  printf 'REGISTRY is required, for example harbor.internal.example:80.\n' >&2
  exit 64
}

mirror_base_image "postgres:17.6" "base-postgres:17.6"
mirror_base_image "rabbitmq:4.2-management" "base-rabbitmq:4.2-management"
mirror_base_image "qdrant/qdrant:v1.15.5" "base-qdrant:v1.15.5"
mirror_dotnet_runtime_base_image "mcr.microsoft.com/dotnet/aspnet:10.0-noble" "base-dotnet-aspnet:10.0-noble"
mirror_base_image "node:22-alpine" "base-node:22-alpine"
mirror_base_image "nginx:1.27-alpine" "base-nginx:1.27-alpine"
