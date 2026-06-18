#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-$SCRIPT_DIR}"
ENV_FILE="${ENV_FILE:-$DEPLOY_DIR/.env}"
COMPOSE_FILE="${COMPOSE_FILE:-$DEPLOY_DIR/docker-compose.yaml}"

APP_IMAGE_KEYS=(
  AICOPILOT_HTTPAPI_IMAGE
  AICOPILOT_MIGRATION_IMAGE
  AICOPILOT_DATAWORKER_IMAGE
  AICOPILOT_RAGWORKER_IMAGE
  AICOPILOT_WEBUI_IMAGE
)

INFRA_IMAGE_KEYS=(
  POSTGRES_IMAGE
  RABBITMQ_IMAGE
  QDRANT_IMAGE
)

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

ensure_release_tag() {
  local release_tag="$1"
  if [[ ! "$release_tag" =~ ^sha-[0-9a-f]+$ ]]; then
    printf 'Release tag must match sha-<hex>: %s\n' "$release_tag" >&2
    exit 64
  fi
}

load_dotenv() {
  if [ ! -f "$ENV_FILE" ]; then
    printf 'Missing deploy environment file: %s\n' "$ENV_FILE" >&2
    exit 66
  fi

  while IFS= read -r env_line || [ -n "$env_line" ]
  do
    env_line="${env_line//$'\r'/}"

    case "$env_line" in
      ''|'#'*)
        continue
        ;;
      *=*)
        export "$env_line"
        ;;
      *)
        printf 'Invalid env line in %s: %s\n' "$ENV_FILE" "$env_line" >&2
        exit 64
        ;;
    esac
  done < "$ENV_FILE"
}

require_env_value() {
  local key="$1"
  local value="${!key:-}"
  if [ -z "$value" ]; then
    printf 'Missing required value in .env: %s\n' "$key" >&2
    exit 64
  fi
}

image_repository_from_ref() {
  local image_ref="$1"
  local last_segment="${image_ref##*/}"

  if [[ "$image_ref" == *@* ]]; then
    printf '%s\n' "${image_ref%@*}"
    return
  fi

  if [[ "$last_segment" == *:* ]]; then
    printf '%s\n' "${image_ref%:*}"
    return
  fi

  printf '%s\n' "$image_ref"
}

replace_env_value() {
  local key="$1"
  local value="$2"
  local tmp_file
  tmp_file="$(mktemp "$DEPLOY_DIR/.env.XXXXXX")"

  awk -v key="$key" -v value="$value" '
    BEGIN { updated = 0 }
    index($0, key "=") == 1 {
      print key "=" value
      updated = 1
      next
    }
    { print }
    END {
      if (!updated) {
        print key "=" value
      }
    }' "$ENV_FILE" > "$tmp_file"

  mv "$tmp_file" "$ENV_FILE"
}

ensure_explicit_registry_image() {
  local key="$1"
  local image_ref="${!key:-}"
  local image_registry="${image_ref%%/*}"

  case "$image_ref" in
    docker.io/*|registry-1.docker.io/*|postgres:*|rabbitmq:*|qdrant/*|node:*|nginx:*)
      printf 'Image must be mirrored to Harbor, not pulled from Docker Hub: %s=%s\n' "$key" "$image_ref" >&2
      exit 64
      ;;
  esac

  if [ "$image_registry" = "$image_ref" ]; then
    printf 'Image must include an explicit Harbor registry: %s=%s\n' "$key" "$image_ref" >&2
    exit 64
  fi

  case "$image_registry" in
    *.*|*:*|localhost)
      ;;
    *)
      printf 'Image must include an explicit Harbor registry: %s=%s\n' "$key" "$image_ref" >&2
      exit 64
      ;;
  esac
}

apply_release_tag_to_app_images() {
  local release_tag="$1"
  local key
  local image_ref
  local image_repository

  for key in "${APP_IMAGE_KEYS[@]}"
  do
    require_env_value "$key"
    image_ref="${!key}"
    image_repository="$(image_repository_from_ref "$image_ref")"
    replace_env_value "$key" "$image_repository:$release_tag"
  done
}

apply_release_tag_to_image_keys() {
  local release_tag="$1"
  shift
  local key
  local image_ref
  local image_repository

  for key in "$@"
  do
    require_env_value "$key"
    image_ref="${!key}"
    image_repository="$(image_repository_from_ref "$image_ref")"
    replace_env_value "$key" "$image_repository:$release_tag"
  done
}

ensure_image_policy() {
  local key

  for key in "${APP_IMAGE_KEYS[@]}" "${INFRA_IMAGE_KEYS[@]}"
  do
    require_env_value "$key"
    ensure_explicit_registry_image "$key"
  done
}

probe_web() {
  local web_port="${AICOPILOT_WEB_PORT:-82}"
  local url="http://127.0.0.1:${web_port}/"
  local attempt=1
  local max_attempts=18
  local status_code

  while [ "$attempt" -le "$max_attempts" ]
  do
    status_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 10 "$url" || true)"
    if [ "$status_code" = "200" ]; then
      printf 'AICopilot web probe succeeded: %s -> %s\n' "$url" "$status_code"
      return
    fi

    printf 'AICopilot web probe attempt %s/%s failed: %s -> %s\n' "$attempt" "$max_attempts" "$url" "${status_code:-curl-error}" >&2
    sleep 5
    attempt=$((attempt + 1))
  done

  printf 'AICopilot web probe failed after %s attempts: %s\n' "$max_attempts" "$url" >&2
  exit 1
}

RELEASE_TAG="${RELEASE_TAG:-}"
REQUESTED_SERVICES="${DEPLOY_SERVICES:-}"

while [ "$#" -gt 0 ]
do
  case "$1" in
    --services)
      shift
      REQUESTED_SERVICES="${1:-}"
      ;;
    --services=*)
      REQUESTED_SERVICES="${1#--services=}"
      ;;
    -*)
      printf 'Unknown deploy-release option: %s\n' "$1" >&2
      exit 64
      ;;
    *)
      if [ -n "$RELEASE_TAG" ]; then
        if [ "$RELEASE_TAG" != "$1" ]; then
          printf 'Unexpected deploy-release argument: %s\n' "$1" >&2
          exit 64
        fi
      else
        RELEASE_TAG="$1"
      fi
      ;;
  esac
  shift
done

ensure_release_tag "$RELEASE_TAG"

normalize_services() {
  local services_input="${1:-}"
  local normalized_services=""
  local service
  local normalized

  if [ -z "$services_input" ]; then
    printf '%s\n' "aicopilot-httpapi aicopilot-migration aicopilot-dataworker aicopilot-ragworker aicopilot-webui"
    return
  fi

  for service in $(printf '%s' "$services_input" | tr ',' ' ')
  do
    case "$service" in
      httpapi|aicopilot-httpapi)
        normalized=aicopilot-httpapi
        ;;
      migration|aicopilot-migration)
        normalized=aicopilot-migration
        ;;
      dataworker|aicopilot-dataworker)
        normalized=aicopilot-dataworker
        ;;
      ragworker|aicopilot-ragworker)
        normalized=aicopilot-ragworker
        ;;
      web|webui|aicopilot-webui)
        normalized=aicopilot-webui
        ;;
      *)
        printf 'Unsupported deploy service: %s\n' "$service" >&2
        exit 64
        ;;
    esac

    case " $normalized_services " in
      *" $normalized "*)
        ;;
      *)
        normalized_services="$normalized_services $normalized"
        ;;
    esac
  done

  printf '%s\n' "$(printf '%s' "$normalized_services" | awk '{$1=$1; print}')"
}

image_key_for_service() {
  case "$1" in
    aicopilot-httpapi)
      printf '%s\n' AICOPILOT_HTTPAPI_IMAGE
      ;;
    aicopilot-migration)
      printf '%s\n' AICOPILOT_MIGRATION_IMAGE
      ;;
    aicopilot-dataworker)
      printf '%s\n' AICOPILOT_DATAWORKER_IMAGE
      ;;
    aicopilot-ragworker)
      printf '%s\n' AICOPILOT_RAGWORKER_IMAGE
      ;;
    aicopilot-webui)
      printf '%s\n' AICOPILOT_WEBUI_IMAGE
      ;;
    *)
      printf 'Unsupported deploy service: %s\n' "$1" >&2
      exit 64
      ;;
  esac
}

SELECTED_SERVICE_NAMES="$(normalize_services "$REQUESTED_SERVICES")"
SELECTED_IMAGE_KEYS=()
RUNTIME_SELECTED_SERVICES=()
RUN_MIGRATION=false
for service in $SELECTED_SERVICE_NAMES
do
  SELECTED_IMAGE_KEYS+=("$(image_key_for_service "$service")")
  if [ "$service" = "aicopilot-migration" ]; then
    RUN_MIGRATION=true
  else
    RUNTIME_SELECTED_SERVICES+=("$service")
  fi
done

command -v docker >/dev/null
command -v curl >/dev/null
docker compose version >/dev/null

cd "$DEPLOY_DIR"
load_dotenv
if [ -z "$REQUESTED_SERVICES" ]; then
  apply_release_tag_to_app_images "$RELEASE_TAG"
else
  apply_release_tag_to_image_keys "$RELEASE_TAG" "${SELECTED_IMAGE_KEYS[@]}"
fi
load_dotenv
ensure_image_policy
compose config -q
if [ -z "$REQUESTED_SERVICES" ]; then
  compose pull
  compose up -d --remove-orphans
else
  compose pull $SELECTED_SERVICE_NAMES
  compose up -d postgres eventbus qdrant
  if [ "$RUN_MIGRATION" = "true" ]; then
    compose up --no-deps --abort-on-container-exit --exit-code-from aicopilot-migration aicopilot-migration
  fi
  if [ "${#RUNTIME_SELECTED_SERVICES[@]}" -gt 0 ]; then
    compose up -d "${RUNTIME_SELECTED_SERVICES[@]}"
  fi
fi
compose ps
probe_web

printf 'AICopilot deploy completed for release tag: %s\n' "$RELEASE_TAG"
