#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-$(cd "$SCRIPT_DIR/.." && pwd)}"
ENV_FILE=""

MODEL_BASE_URL="${AICOPILOT_MODEL_SMOKE_BASE_URL:-}"
MODEL_NAME="${AICOPILOT_MODEL_SMOKE_MODEL:-}"
MODEL_API_KEY="${AICOPILOT_MODEL_SMOKE_API_KEY:-}"
ALLOW_DUMMY_KEY="${AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY:-}"
TIMEOUT_SECONDS="${AICOPILOT_MODEL_SMOKE_TIMEOUT_SECONDS:-}"
DRY_RUN=false

usage() {
  cat <<'USAGE'
Usage:
  deploy/enterprise-ai/scripts/check-model-provider-openai.sh [options]

Checks server-to-model connectivity by calling an OpenAI-compatible
/chat/completions endpoint directly from the current machine. This bypasses
AICopilot application code and is intended for production server diagnostics.

Options:
  --env-file <path>       Deploy .env file. Values are read only for AICOPILOT_MODEL_SMOKE_* keys.
  --base-url <url>        OpenAI-compatible base URL. Required through option or env file.
  --model <name>          Model name. Required through option or env file.
  --api-key <value>       API key. Required through option or env file.
  --allow-dummy-key       Explicitly allow the literal dummy-key for model gateways that require it.
  --timeout <seconds>     curl max-time in seconds. Default: 10.
  --dry-run               Print the probe target without connecting.
  --help                  Show this help.
USAGE
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
}

is_truthy() {
  case "${1:-}" in
    true|TRUE|True|1|yes|YES|Yes)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
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

validate_json_string() {
  local key="$1"
  local value="$2"

  case "$value" in
    *\"*|*\\*|*$'\n'*|*$'\r'*)
      fail "$key cannot contain JSON control characters, quotes, or backslashes."
      ;;
  esac
}

validate_header_value() {
  local key="$1"
  local value="$2"

  case "$value" in
    *" "*|*$'\t'*|*$'\n'*|*$'\r'*)
      fail "$key cannot contain whitespace or HTTP header control characters."
      ;;
    *\"*|*\\*)
      fail "$key cannot contain quotes or backslashes."
      ;;
  esac
}

validate_model_base_url() {
  local value="$1"
  local without_scheme

  case "$value" in
    http://*|https://*)
      ;;
    *)
      fail "Model base URL must start with http:// or https://: $value"
      ;;
  esac

  case "$value" in
    *$'\n'*|*$'\r'*|*$'\t'*|*\"*|*\\*)
      fail "Model base URL cannot contain control characters, quotes, or backslashes."
      ;;
    *\?*|*\#*)
      fail "Model base URL must not include query string or fragment."
      ;;
  esac

  without_scheme="${value#http://}"
  without_scheme="${without_scheme#https://}"
  if [[ "$without_scheme" == *@* ]]; then
    fail "Model base URL must not include userinfo credentials."
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
    --base-url)
      shift
      MODEL_BASE_URL="${1:-}"
      ;;
    --base-url=*)
      MODEL_BASE_URL="${1#--base-url=}"
      ;;
    --model)
      shift
      MODEL_NAME="${1:-}"
      ;;
    --model=*)
      MODEL_NAME="${1#--model=}"
      ;;
    --api-key)
      shift
      MODEL_API_KEY="${1:-}"
      ;;
    --api-key=*)
      MODEL_API_KEY="${1#--api-key=}"
      ;;
    --allow-dummy-key)
      ALLOW_DUMMY_KEY=true
      ;;
    --timeout)
      shift
      TIMEOUT_SECONDS="${1:-}"
      ;;
    --timeout=*)
      TIMEOUT_SECONDS="${1#--timeout=}"
      ;;
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown check-model-provider-openai option: $1"
      ;;
  esac
  shift
done

if [ -n "$ENV_FILE" ]; then
  [ -f "$ENV_FILE" ] || fail "Missing deploy .env file: $ENV_FILE"
  MODEL_BASE_URL="${MODEL_BASE_URL:-$(read_env_value AICOPILOT_MODEL_SMOKE_BASE_URL)}"
  MODEL_NAME="${MODEL_NAME:-$(read_env_value AICOPILOT_MODEL_SMOKE_MODEL)}"
  MODEL_API_KEY="${MODEL_API_KEY:-$(read_env_value AICOPILOT_MODEL_SMOKE_API_KEY)}"
  ALLOW_DUMMY_KEY="${ALLOW_DUMMY_KEY:-$(read_env_value AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY)}"
  TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-$(read_env_value AICOPILOT_MODEL_SMOKE_TIMEOUT_SECONDS)}"
fi

TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-10}"
ALLOW_DUMMY_KEY="${ALLOW_DUMMY_KEY:-false}"

[ -n "$MODEL_BASE_URL" ] || fail "AICOPILOT_MODEL_SMOKE_BASE_URL or --base-url is required."
[ -n "$MODEL_NAME" ] || fail "AICOPILOT_MODEL_SMOKE_MODEL or --model is required."
[ -n "$MODEL_API_KEY" ] || fail "AICOPILOT_MODEL_SMOKE_API_KEY or --api-key is required."

if [ "$MODEL_API_KEY" = "dummy-key" ] && ! is_truthy "$ALLOW_DUMMY_KEY"; then
  fail "Model smoke API key uses dummy-key. Set AICOPILOT_MODEL_SMOKE_ALLOW_DUMMY_KEY=true or pass --allow-dummy-key only when the model gateway explicitly requires it."
fi

validate_model_base_url "$MODEL_BASE_URL"
validate_header_value "Model API key" "$MODEL_API_KEY"

if ! printf '%s' "$TIMEOUT_SECONDS" | grep -Eq '^[1-9][0-9]{0,2}$'; then
  fail "Timeout must be a positive integer number of seconds: $TIMEOUT_SECONDS"
fi

validate_json_string "Model name" "$MODEL_NAME"
command -v curl >/dev/null || fail "Required command not found: curl"

trimmed_base_url="${MODEL_BASE_URL%/}"
chat_completions_url="$trimmed_base_url/chat/completions"

printf 'Checking model provider connectivity: baseUrl=%s model=%s timeout=%ss\n' \
  "$MODEL_BASE_URL" "$MODEL_NAME" "$TIMEOUT_SECONDS"

if [ "$DRY_RUN" = true ]; then
  printf '[dry-run] POST %s with bearer token masked.\n' "$chat_completions_url"
  exit 0
fi

request_file="$(mktemp)"
response_file="$(mktemp)"
trap 'rm -f "$request_file" "$response_file"' EXIT

cat > "$request_file" <<JSON
{"model":"$MODEL_NAME","messages":[{"role":"user","content":"Return exactly OK."}],"max_tokens":16,"temperature":0}
JSON

curl_status=0
http_code="$(
  curl --silent --show-error \
    --output "$response_file" \
    --write-out '%{http_code}' \
    --max-time "$TIMEOUT_SECONDS" \
    --header "Authorization: Bearer $MODEL_API_KEY" \
    --header 'Content-Type: application/json' \
    --data @"$request_file" \
    "$chat_completions_url"
)" || curl_status=$?

if [ "$curl_status" -ne 0 ]; then
  printf 'Model provider connectivity failed: curl_exit=%s url=%s\n' "$curl_status" "$chat_completions_url" >&2
  exit 65
fi

if [ "$http_code" != "200" ]; then
  printf 'Model provider returned non-200 status: http=%s url=%s\n' "$http_code" "$chat_completions_url" >&2
  printf 'Response preview: ' >&2
  head -c 800 "$response_file" | tr '\n' ' ' >&2
  printf '\n' >&2
  exit 65
fi

if ! grep -q '"choices"' "$response_file"; then
  printf 'Model provider response did not look OpenAI-compatible: missing choices field.\n' >&2
  printf 'Response preview: ' >&2
  head -c 800 "$response_file" | tr '\n' ' ' >&2
  printf '\n' >&2
  exit 65
fi

printf 'Model provider connectivity passed: http=%s url=%s\n' "$http_code" "$chat_completions_url"
