#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-$(cd "$SCRIPT_DIR/.." && pwd)}"
ENV_FILE="${ENV_FILE:-$DEPLOY_DIR/.env}"
COMPOSE_FILE="${COMPOSE_FILE:-$DEPLOY_DIR/docker-compose.yaml}"
WEB_URL="${AICOPILOT_RELEASE_WEB_URL:-}"
DRY_RUN=false

usage() {
  cat <<'USAGE'
Usage:
  deploy/enterprise-ai/scripts/check-release-security-attestation.sh [options]

Runs post-release AICopilot security attestation from the production server.
This keeps the current HTTP-only red line: it requires HTTP headers compatible
with intranet HTTP and fails if HSTS/HTTPS-only behavior is detected.

Checks:
  - Web HTTP endpoint returns required security headers and no HSTS.
  - Cloud OIDC status endpoint is reachable through the Web same-origin proxy.
  - aicopilot-webui container runs as a non-root user and has writable nginx dirs.
  - LanguageModel and EmbeddingModel API keys are migrated to encv2.

Options:
  --env-file <path>       Deploy .env file. Default: deploy/enterprise-ai/.env.
  --compose-file <path>   Docker compose file. Default: deploy/enterprise-ai/docker-compose.yaml.
  --web-url <url>         HTTP web URL. Default: http://127.0.0.1:${AICOPILOT_WEB_PORT:-82}/.
  --dry-run               Print checks without connecting to services.
  --help                  Show this help.
USAGE
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
}

read_env_value() {
  local key="$1"
  awk -v key="$key" '
    BEGIN { prefix = key "=" }
    index($0, prefix) == 1 {
      value = substr($0, length(prefix) + 1)
      sub(/\r$/, "", value)
      print value
      exit
    }
  ' "$ENV_FILE"
}

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

require_response_header() {
  local headers_file="$1"
  local header_pattern="$2"
  local header_description="$3"

  if ! grep -Eiq "$header_pattern" "$headers_file"; then
    printf 'AICopilot web security header missing or invalid: %s\n' "$header_description" >&2
    cat "$headers_file" >&2
    exit 65
  fi
}

resolve_web_url() {
  if [ -n "$WEB_URL" ]; then
    printf '%s\n' "$WEB_URL"
    return
  fi

  local web_port="${AICOPILOT_WEB_PORT:-}"
  if [ -z "$web_port" ] && [ -f "$ENV_FILE" ]; then
    web_port="$(read_env_value AICOPILOT_WEB_PORT)"
  fi

  printf 'http://127.0.0.1:%s/\n' "${web_port:-82}"
}

check_web_headers() {
  local url="$1"
  local headers_file

  case "$url" in
    http://*)
      ;;
    https://*)
      fail "AICopilot current production red line is HTTP-only; web attestation URL must not be HTTPS: $url"
      ;;
    *)
      fail "Web attestation URL must be an absolute HTTP URL: $url"
      ;;
  esac

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] curl --head %s and require HTTP-only security headers.\n' "$url"
    return
  fi

  headers_file="$(mktemp "${TMPDIR:-/tmp}/aicopilot-web-headers.XXXXXX")"
  if ! curl --silent --show-error --fail --head --max-time 10 "$url" | tr -d '\r' > "$headers_file"; then
    printf 'AICopilot web security header probe failed: %s\n' "$url" >&2
    rm -f "$headers_file"
    exit 65
  fi

  require_response_header "$headers_file" '^X-Content-Type-Options:[[:space:]]*nosniff[[:space:]]*$' 'X-Content-Type-Options: nosniff'
  require_response_header "$headers_file" '^X-Frame-Options:[[:space:]]*DENY[[:space:]]*$' 'X-Frame-Options: DENY'
  require_response_header "$headers_file" '^Referrer-Policy:[[:space:]]*no-referrer[[:space:]]*$' 'Referrer-Policy: no-referrer'
  require_response_header "$headers_file" '^Permissions-Policy:[[:space:]].*camera=\(\)' 'Permissions-Policy'
  require_response_header "$headers_file" "^Content-Security-Policy:[[:space:]].*default-src 'self'" 'Content-Security-Policy'

  if grep -Eiq '^Strict-Transport-Security:' "$headers_file"; then
    printf 'AICopilot HTTP-only deployment must not emit Strict-Transport-Security until HTTPS is explicitly approved.\n' >&2
    cat "$headers_file" >&2
    rm -f "$headers_file"
    exit 65
  fi

  rm -f "$headers_file"
  printf 'AICopilot web HTTP-only header attestation passed: %s\n' "$url"
}

check_cloud_oidc_status() {
  local web_url="$1"
  local status_url="${web_url%/}/api/identity/cloud-oidc/status"
  local status_code
  local attempt

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] curl %s and require HTTP 200.\n' "$status_url"
    return
  fi

  for attempt in $(seq 1 18); do
    status_code="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 10 "$status_url" || true)"
    if [ "$status_code" = "200" ]; then
      printf 'AICopilot Cloud OIDC status attestation passed: %s -> %s\n' "$status_url" "$status_code"
      return
    fi

    printf 'AICopilot Cloud OIDC status probe attempt %s/18 failed: %s -> %s\n' \
      "$attempt" "$status_url" "${status_code:-curl-error}" >&2
    sleep 3
  done

  printf 'AICopilot Cloud OIDC status probe failed after 18 attempts: %s -> %s\n' \
    "$status_url" "${status_code:-curl-error}" >&2
  exit 65
}

check_web_container_user() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] docker compose exec aicopilot-webui id -u and writable nginx dir checks.\n'
    return
  fi

  compose exec -T aicopilot-webui sh -lc '
    uid="$(id -u)"
    if [ "$uid" -eq 0 ]; then
      echo "aicopilot-webui must not run as root." >&2
      exit 65
    fi
    test -w /var/cache/nginx
    test -w /var/run
    printf "AICopilot web container non-root attestation passed: uid=%s\n" "$uid"
  '
}

check_model_secret_migration() {
  local check_script="$SCRIPT_DIR/check-model-secret-migration.sh"

  if [ ! -x "$check_script" ]; then
    fail "Model secret migration check script is missing or not executable: $check_script"
  fi

  if [ "$DRY_RUN" = true ]; then
    "$check_script" --env-file "$ENV_FILE" --compose-file "$COMPOSE_FILE" --dry-run
  else
    "$check_script" --env-file "$ENV_FILE" --compose-file "$COMPOSE_FILE"
  fi
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --env-file)
      shift
      ENV_FILE="${1:-}"
      ;;
    --env-file=*)
      ENV_FILE="${1#--env-file=}"
      ;;
    --compose-file)
      shift
      COMPOSE_FILE="${1:-}"
      ;;
    --compose-file=*)
      COMPOSE_FILE="${1#--compose-file=}"
      ;;
    --web-url)
      shift
      WEB_URL="${1:-}"
      ;;
    --web-url=*)
      WEB_URL="${1#--web-url=}"
      ;;
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown check-release-security-attestation option: $1"
      ;;
  esac
  shift
done

if [ "$DRY_RUN" != true ]; then
  [ -f "$ENV_FILE" ] || fail "Missing deploy .env file: $ENV_FILE"
  [ -f "$COMPOSE_FILE" ] || fail "Missing docker compose file: $COMPOSE_FILE"
  command -v curl >/dev/null || fail "Required command not found: curl"
  command -v docker >/dev/null || fail "Required command not found: docker"
  docker compose version >/dev/null
fi

resolved_web_url="$(resolve_web_url)"
printf 'Running AICopilot release security attestation: webUrl=%s envFile=%s composeFile=%s\n' \
  "$resolved_web_url" "$ENV_FILE" "$COMPOSE_FILE"

check_web_headers "$resolved_web_url"
check_cloud_oidc_status "$resolved_web_url"
check_web_container_user
check_model_secret_migration

printf 'AICopilot release security attestation passed.\n'
