#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/Provision-AICopilotCloudReadOnlyDbRole.sh [options]

Generates a Cloud PostgreSQL readonly role password locally, writes the required
GitHub production environment secrets, and optionally triggers the confirmed
manual workflow that creates/rotates the readonly DB role and enables AICopilot
direct Cloud readonly Text-to-SQL.

The authoritative grant and probe SQL lives under:
  deploy/enterprise-ai/cloud-readonly/

Do not add table grants here or inline them in the workflow.

Options:
  --username <name>       PostgreSQL role name. Default: aicopilot_cloud_readonly.
  --github-env <name>     GitHub environment name. Default: production.
  --repo <owner/repo>     GitHub repo. Default: current repo.
  --ref <git-ref>         Workflow ref to run. Default: current branch.
  --no-trigger            Only set secrets; do not run the workflow.
  --no-enable             Provision/rotate the DB role but do not enable AICopilot.
  --help                  Show this help.

The connection string stored in GitHub will use:
  Host=cloud-postgres;Port=5432;Database=iiot-db;Username=<role>;Password=<generated>
USAGE
}

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"
github_env="${AICOPILOT_GITHUB_ENVIRONMENT:-production}"
repo=""
workflow_file="aicopilot-provision-cloud-readonly-db-role.yml"
workflow_ref=""
trigger_workflow=true
enable_after_provision=true
readonly_username="${AICOPILOT_CLOUD_READONLY_DB_USERNAME:-aicopilot_cloud_readonly}"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --username)
      shift
      readonly_username="${1:-}"
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
    --no-enable)
      enable_after_provision=false
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

case "$readonly_username" in
  [A-Za-z_]*)
    ;;
  *)
    printf 'Invalid PostgreSQL role name: %s\n' "$readonly_username" >&2
    exit 64
    ;;
esac

if ! printf '%s' "$readonly_username" | grep -Eq '^[A-Za-z_][A-Za-z0-9_]{0,62}$'; then
  printf 'Invalid PostgreSQL role name: %s\n' "$readonly_username" >&2
  exit 64
fi

command -v gh >/dev/null
command -v openssl >/dev/null

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

readonly_password="$(
  openssl rand -base64 48 |
    tr '+/' '-_' |
    tr -d '=\n' |
    cut -c1-48
)"
connection_string="Host=cloud-postgres;Port=5432;Database=iiot-db;Username=${readonly_username};Password=${readonly_password}"

printf '%s' "$connection_string" |
  gh secret set DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING --env "$github_env" --repo "$repo"
printf '%s' "$readonly_username" |
  gh secret set DATA_ANALYSIS_CLOUD_READONLY_USERNAME --env "$github_env" --repo "$repo"
printf '%s' "$readonly_password" |
  gh secret set DATA_ANALYSIS_CLOUD_READONLY_PASSWORD --env "$github_env" --repo "$repo"

printf 'Updated GitHub environment secrets for %s (%s).\n' "$repo" "$github_env"
printf 'DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING_CONFIGURED=yes\n'
printf 'DATA_ANALYSIS_CLOUD_READONLY_USERNAME=%s\n' "$readonly_username"
printf 'DATA_ANALYSIS_CLOUD_READONLY_PASSWORD_CONFIGURED=yes\n'

if [ "$trigger_workflow" = "true" ]; then
  gh workflow run "$workflow_file" \
    --repo "$repo" \
    --ref "$workflow_ref" \
    -f confirm=PROVISION_AICOPILOT_CLOUD_READONLY_DB_ROLE \
    -f enable_after_provision="$enable_after_provision"
  printf 'Triggered workflow %s on ref %s.\n' "$workflow_file" "$workflow_ref"
else
  printf 'Skipped workflow trigger because --no-trigger was set.\n'
fi
