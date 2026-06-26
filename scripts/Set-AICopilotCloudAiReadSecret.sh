#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/Set-AICopilotCloudAiReadSecret.sh [options]

Reads a local Mac-only secret file, writes GitHub production environment
secrets, and optionally triggers the server workflow that enables real Cloud
AiRead for AICopilot.

Default local secret file:
  ~/.config/aicopilot/cloud-ai-read.env

Supported local secret file forms:
  CLOUD_AI_READ_BASE_URL=http://10.98.90.154:81
  CLOUD_AI_SERVICE_ACCOUNT_TOKEN=<Cloud AI read-only JWT>

or:
  CLOUD_AI_READ_BASE_URL=http://10.98.90.154:81
  CLOUD_JWT_SIGNING_SECRET=<Cloud JwtSettings__Secret>
  CLOUD_AI_READ_TOKEN_EXPIRES_DAYS=30

Options:
  --env-file <path>       Local secret file path.
  --github-env <name>     GitHub environment name. Default: production.
  --repo <owner/repo>     GitHub repo. Default: current repo.
  --ref <git-ref>         Workflow ref to run. Default: current branch.
  --no-trigger            Only set secrets; do not run the workflow.
  --help                  Show this help.
USAGE
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
config_file="${AICOPILOT_CLOUD_AI_READ_ENV:-$HOME/.config/aicopilot/cloud-ai-read.env}"
github_env="${AICOPILOT_GITHUB_ENVIRONMENT:-production}"
repo=""
workflow_file="aicopilot-enable-real-cloud-ai-read.yml"
workflow_ref=""
trigger_workflow=true

while [ "$#" -gt 0 ]; do
  case "$1" in
    --env-file)
      shift
      config_file="${1:-}"
      ;;
    --github-env)
      shift
      github_env="${1:-}"
      ;;
    --repo)
      shift
      repo="${1:-}"
      ;;
    --ref)
      shift
      workflow_ref="${1:-}"
      ;;
    --no-trigger)
      trigger_workflow=false
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      printf 'Unknown option: %s\n' "$1" >&2
      usage >&2
      exit 64
      ;;
  esac
  shift
done

if [ ! -f "$config_file" ]; then
  printf 'Missing local Cloud AiRead secret file: %s\n' "$config_file" >&2
  printf 'Create it with CLOUD_AI_SERVICE_ACCOUNT_TOKEN or CLOUD_JWT_SIGNING_SECRET.\n' >&2
  exit 66
fi

command -v gh >/dev/null
command -v python3 >/dev/null

if ! gh auth status >/dev/null 2>&1; then
  printf 'GitHub CLI is not authenticated. Run: gh auth login\n' >&2
  exit 69
fi

if [ -z "$repo" ]; then
  repo="$(cd "$repo_root" && gh repo view --json nameWithOwner -q .nameWithOwner)"
fi

if [ -z "$workflow_ref" ]; then
  workflow_ref="$(cd "$repo_root" && git rev-parse --abbrev-ref HEAD)"
fi

strip_outer_quotes() {
  local value="$1"
  value="${value%$'\r'}"
  if [[ "$value" == \"*\" && "$value" == *\" ]]; then
    value="${value:1:${#value}-2}"
  elif [[ "$value" == \'*\' && "$value" == *\' ]]; then
    value="${value:1:${#value}-2}"
  fi
  printf '%s' "$value"
}

read_config_value() {
  local key="$1"
  local value
  value="$(awk -v key="$key" '
    /^[[:space:]]*#/ || /^[[:space:]]*$/ { next }
    {
      line = $0
      sub(/\r$/, "", line)
      sub(/^[[:space:]]*export[[:space:]]+/, "", line)
      prefix = key "="
      if (index(line, prefix) == 1) {
        print substr(line, length(prefix) + 1)
        exit
      }
    }
  ' "$config_file")"
  strip_outer_quotes "$value"
}

cloud_ai_read_base_url="$(read_config_value CLOUD_AI_READ_BASE_URL)"
cloud_ai_read_token="$(read_config_value CLOUD_AI_SERVICE_ACCOUNT_TOKEN)"
cloud_jwt_signing_secret="$(read_config_value CLOUD_JWT_SIGNING_SECRET)"
expires_days="$(read_config_value CLOUD_AI_READ_TOKEN_EXPIRES_DAYS)"
issuer="$(read_config_value CLOUD_JWT_ISSUER)"
audience="$(read_config_value CLOUD_JWT_AUDIENCE)"
service_account_name="$(read_config_value CLOUD_AI_READ_SERVICE_ACCOUNT_NAME)"
permissions="$(read_config_value CLOUD_AI_READ_PERMISSIONS)"

cloud_ai_read_base_url="${cloud_ai_read_base_url:-http://10.98.90.154:81}"
expires_days="${expires_days:-30}"
issuer="${issuer:-IIoT.CloudPlatform}"
audience="${audience:-IIoT.WpfClient}"
service_account_name="${service_account_name:-aicopilot-ai-read}"
permissions="${permissions:-AiRead.Device,AiRead.Capacity,AiRead.DeviceLog,AiRead.PassStation}"

if [ -z "$cloud_ai_read_token" ]; then
  if [ -z "$cloud_jwt_signing_secret" ]; then
    printf 'CLOUD_AI_SERVICE_ACCOUNT_TOKEN is missing and CLOUD_JWT_SIGNING_SECRET is not provided.\n' >&2
    exit 64
  fi

  cloud_ai_read_token="$(
    CLOUD_JWT_SIGNING_SECRET="$cloud_jwt_signing_secret" \
    CLOUD_AI_READ_TOKEN_EXPIRES_DAYS="$expires_days" \
    CLOUD_JWT_ISSUER="$issuer" \
    CLOUD_JWT_AUDIENCE="$audience" \
    CLOUD_AI_READ_SERVICE_ACCOUNT_NAME="$service_account_name" \
    CLOUD_AI_READ_PERMISSIONS="$permissions" \
    python3 - <<'PY'
import base64
import hmac
import hashlib
import json
import os
import time
import uuid

def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode("ascii").rstrip("=")

secret = os.environ["CLOUD_JWT_SIGNING_SECRET"].encode("utf-8")
now = int(time.time())
expires_days = int(os.environ.get("CLOUD_AI_READ_TOKEN_EXPIRES_DAYS", "30"))
permissions = [
    item.strip()
    for item in os.environ.get("CLOUD_AI_READ_PERMISSIONS", "").split(",")
    if item.strip()
]
subject = str(uuid.uuid4())
service_account_name = os.environ.get("CLOUD_AI_READ_SERVICE_ACCOUNT_NAME", "aicopilot-ai-read")

header = {"alg": "HS256", "typ": "JWT"}
payload = {
    "iss": os.environ.get("CLOUD_JWT_ISSUER", "IIoT.CloudPlatform"),
    "aud": os.environ.get("CLOUD_JWT_AUDIENCE", "IIoT.WpfClient"),
    "sub": subject,
    "unique_name": service_account_name,
    "nameid": subject,
    "name": service_account_name,
    "actor_type": "ai-service-account",
    "jti": str(uuid.uuid4()),
    "iat": now,
    "nbf": now,
    "exp": now + expires_days * 86400,
    "Permission": permissions,
}

header_part = b64url(json.dumps(header, separators=(",", ":")).encode("utf-8"))
payload_part = b64url(json.dumps(payload, separators=(",", ":")).encode("utf-8"))
signing_input = f"{header_part}.{payload_part}".encode("ascii")
signature = hmac.new(secret, signing_input, hashlib.sha256).digest()
print(f"{header_part}.{payload_part}.{b64url(signature)}")
PY
  )"
  printf 'Generated Cloud AiRead JWT locally. Token value was not printed.\n'
else
  printf 'Using CLOUD_AI_SERVICE_ACCOUNT_TOKEN from local secret file. Token value was not printed.\n'
fi

if [ -z "$cloud_ai_read_token" ]; then
  printf 'Cloud AiRead token is empty after loading/generation.\n' >&2
  exit 64
fi

case "$cloud_ai_read_base_url" in
  http://*|https://*)
    ;;
  *)
    printf 'CLOUD_AI_READ_BASE_URL must be an absolute HTTP/HTTPS URL: %s\n' "$cloud_ai_read_base_url" >&2
    exit 64
    ;;
esac

printf '%s' "$cloud_ai_read_base_url" |
  gh secret set CLOUD_AI_READ_BASE_URL --env "$github_env" --repo "$repo" --body-file -
printf '%s' "$cloud_ai_read_token" |
  gh secret set CLOUD_AI_SERVICE_ACCOUNT_TOKEN --env "$github_env" --repo "$repo" --body-file -

printf 'Updated GitHub environment secrets for %s (%s).\n' "$repo" "$github_env"
printf 'CLOUD_AI_READ_BASE_URL=%s\n' "$cloud_ai_read_base_url"
printf 'CLOUD_AI_SERVICE_ACCOUNT_TOKEN_CONFIGURED=yes\n'

if [ "$trigger_workflow" = "true" ]; then
  gh workflow run "$workflow_file" --repo "$repo" --ref "$workflow_ref"
  printf 'Triggered workflow %s on ref %s.\n' "$workflow_file" "$workflow_ref"
else
  printf 'Skipped workflow trigger because --no-trigger was set.\n'
fi
