#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-$(cd "$SCRIPT_DIR/.." && pwd)}"
ENV_FILE="${ENV_FILE:-$DEPLOY_DIR/.env}"
SQL_DIR="${SQL_DIR:-$DEPLOY_DIR/cloud-readonly}"
CHECK_SQL="$SQL_DIR/check-readonly-grants.sql"

READONLY_USERNAME="${DATA_ANALYSIS_CLOUD_READONLY_USERNAME:-}"
READONLY_PASSWORD="${DATA_ANALYSIS_CLOUD_READONLY_PASSWORD:-}"
READONLY_DATABASE="${DATA_ANALYSIS_CLOUD_READONLY_DATABASE:-}"
DRY_RUN=false

usage() {
  cat <<'USAGE'
Usage:
  deploy/enterprise-ai/scripts/check-cloud-readonly-grants.sh [options]

Checks that the AICopilot CloudReadOnly PostgreSQL role can SELECT only the
governed Cloud tables and has no write or schema-create privilege.

Options:
  --env-file <path>       Deploy .env file. Default: deploy/enterprise-ai/.env.
  --username <name>       Readonly role name. Default: canonical dedicated env value.
  --password <value>      Readonly role password. Default: canonical dedicated env value.
  --database <name>       Cloud database name. Default: canonical dedicated env value.
  --dry-run               Print the probe command without connecting.
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
      if (length(value) >= 2 &&
          substr(value, 1, 1) == "\"" &&
          substr(value, length(value), 1) == "\"") {
        value = substr(value, 2, length(value) - 2)
        gsub(/\$\$/, "$", value)
        gsub(/\\"/, sprintf("%c", 34), value)
        gsub(/\\\\/, sprintf("%c", 92), value)
      } else if (length(value) >= 2 &&
                 substr(value, 1, 1) == "\047" &&
                 substr(value, length(value), 1) == "\047") {
        value = substr(value, 2, length(value) - 2)
      }
      print value
      exit
    }
  ' "$ENV_FILE"
}

validate_identifier() {
  local value="$1"
  if ! printf '%s' "$value" | grep -Eq '^[A-Za-z_][A-Za-z0-9_]{0,62}$'; then
    fail "Invalid PostgreSQL identifier: $value"
  fi
}

cloud_container_id() {
  local cloud_project
  local cloud_service
  cloud_project="$(read_env_value DATA_ANALYSIS_CLOUD_READONLY_CLOUD_COMPOSE_PROJECT)"
  cloud_service="$(read_env_value DATA_ANALYSIS_CLOUD_READONLY_CLOUD_POSTGRES_SERVICE)"
  cloud_project="${cloud_project:-deploy}"
  cloud_service="${cloud_service:-postgres}"

  docker ps \
    --filter "label=com.docker.compose.project=$cloud_project" \
    --filter "label=com.docker.compose.service=$cloud_service" \
    --format '{{.ID}}' |
    head -n 1
}

assert_probe_passed() {
  local output_file="$1"
  awk -F'|' '
    $1 == "readonly_privileges" {
      found = 1
      for (i = 2; i <= NF; i++) {
        if ($i != "t") {
          exit 1
        }
      }
    }
    END {
      if (!found) {
        exit 1
      }
    }
  ' "$output_file"
}

print_row_counts() {
  local output_file="$1"
  awk -F'|' '/_rows/ { printf "  - %s=%s\n", $1, $2 }' "$output_file"
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
    --username)
      shift
      READONLY_USERNAME="${1:-}"
      ;;
    --username=*)
      READONLY_USERNAME="${1#--username=}"
      ;;
    --password)
      shift
      READONLY_PASSWORD="${1:-}"
      ;;
    --password=*)
      READONLY_PASSWORD="${1#--password=}"
      ;;
    --database)
      shift
      READONLY_DATABASE="${1:-}"
      ;;
    --database=*)
      READONLY_DATABASE="${1#--database=}"
      ;;
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown check-cloud-readonly-grants option: $1"
      ;;
  esac
  shift
done

[ -f "$ENV_FILE" ] || fail "Missing deploy .env file: $ENV_FILE"
[ -f "$CHECK_SQL" ] || fail "Missing CloudReadOnly check SQL: $CHECK_SQL"
command -v docker >/dev/null || fail "Required command not found: docker"

READONLY_USERNAME="${READONLY_USERNAME:-$(read_env_value DATA_ANALYSIS_CLOUD_READONLY_USERNAME)}"
READONLY_PASSWORD="${READONLY_PASSWORD:-$(read_env_value DATA_ANALYSIS_CLOUD_READONLY_PASSWORD)}"
READONLY_DATABASE="${READONLY_DATABASE:-$(read_env_value DATA_ANALYSIS_CLOUD_READONLY_DATABASE)}"

[ -n "$READONLY_USERNAME" ] || fail "Missing readonly username."
[ -n "$READONLY_PASSWORD" ] || fail "Missing readonly password."
[ -n "$READONLY_DATABASE" ] || fail "Missing readonly database."
validate_identifier "$READONLY_USERNAME"

cloud_container="$(cloud_container_id)"
if [ -z "$cloud_container" ]; then
  if [ "$DRY_RUN" = true ]; then
    cloud_container="<cloud-postgres-container>"
  else
    fail "Cloud PostgreSQL container was not found for CloudReadOnly grant check."
  fi
fi

printf 'Checking CloudReadOnly grants: role=%s database=%s sql=%s\n' \
  "$READONLY_USERNAME" "$READONLY_DATABASE" "$CHECK_SQL"

if [ "$DRY_RUN" = true ]; then
  printf '[dry-run] docker exec <cloud-postgres> psql -h 127.0.0.1 -U %s -d %s < %s\n' \
    "$READONLY_USERNAME" "$READONLY_DATABASE" "$CHECK_SQL"
  exit 0
fi

output_file="$(mktemp)"
trap 'rm -f "$output_file"' EXIT

docker exec -i \
  -e PGPASSWORD="$READONLY_PASSWORD" \
  "$cloud_container" psql \
    -v ON_ERROR_STOP=1 \
    -At \
    -F '|' \
    -h 127.0.0.1 \
    -U "$READONLY_USERNAME" \
    -d "$READONLY_DATABASE" \
  < "$CHECK_SQL" > "$output_file"

if ! assert_probe_passed "$output_file"; then
  printf 'CloudReadOnly readonly privilege verification failed.\n' >&2
  sed 's/[[:space:]]\+/ /g' "$output_file" >&2
  exit 65
fi

printf 'CloudReadOnly readonly privilege verification passed.\n'
printf 'Cloud readonly table row counts:\n'
print_row_counts "$output_file"
