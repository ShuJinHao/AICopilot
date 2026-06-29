#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/Set-AICopilotCloudReadOnlyDbSecret.sh [options]

Reads a local Mac-only secret file, writes the GitHub production environment
secret used by AICopilot direct Cloud readonly DB mode, and optionally triggers
the server workflow.

Default local secret file:
  ~/.config/aicopilot/cloud-db-read.env

Required local secret file form:
  DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING=<Cloud PostgreSQL readonly connection string>
  DATA_ANALYSIS_CLOUD_READONLY_CREDENTIAL_VERIFIED=true

Server-side recommended connection host:
  Host=cloud-postgres;Port=5432;Database=iiot-db;Username=<readonly_user>;Password=<readonly_password>

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
config_file="${AICOPILOT_CLOUD_DB_READ_ENV:-$HOME/.config/aicopilot/cloud-db-read.env}"
github_env="${AICOPILOT_GITHUB_ENVIRONMENT:-production}"
repo=""
workflow_file="aicopilot-enable-direct-cloud-readonly-db.yml"
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
  printf 'Missing local Cloud DB readonly secret file: %s\n' "$config_file" >&2
  printf 'Create it with DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING and DATA_ANALYSIS_CLOUD_READONLY_CREDENTIAL_VERIFIED=true.\n' >&2
  exit 66
fi

command -v gh >/dev/null

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

connection_string="$(read_config_value DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING)"
if [ -z "$connection_string" ]; then
  connection_string="$(read_config_value CLOUD_READONLY_DB_CONNECTION_STRING)"
fi
credential_verified="$(read_config_value DATA_ANALYSIS_CLOUD_READONLY_CREDENTIAL_VERIFIED)"

if [ -z "$connection_string" ]; then
  printf 'DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING is missing or empty.\n' >&2
  exit 64
fi

if [ "$credential_verified" != "true" ]; then
  printf 'DATA_ANALYSIS_CLOUD_READONLY_CREDENTIAL_VERIFIED must be true after you verify the DB account is readonly.\n' >&2
  exit 64
fi

printf '%s' "$connection_string" |
  gh secret set DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING --env "$github_env" --repo "$repo"

printf 'Updated GitHub environment secret for %s (%s).\n' "$repo" "$github_env"
printf 'DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING_CONFIGURED=yes\n'

if [ "$trigger_workflow" = "true" ]; then
  gh workflow run "$workflow_file" --repo "$repo" --ref "$workflow_ref"
  printf 'Triggered workflow %s on ref %s.\n' "$workflow_file" "$workflow_ref"
else
  printf 'Skipped workflow trigger because --no-trigger was set.\n'
fi
