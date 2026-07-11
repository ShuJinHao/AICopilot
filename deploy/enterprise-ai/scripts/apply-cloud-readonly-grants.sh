#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-$(cd "$SCRIPT_DIR/.." && pwd)}"
ENV_FILE="${ENV_FILE:-$DEPLOY_DIR/.env}"
SQL_DIR="${SQL_DIR:-$DEPLOY_DIR/cloud-readonly}"
APPLY_SQL="$SQL_DIR/apply-readonly-grants.sql"
CHECK_SCRIPT="$SCRIPT_DIR/check-cloud-readonly-grants.sh"

READONLY_USERNAME="${DATA_ANALYSIS_CLOUD_READONLY_USERNAME:-}"
READONLY_PASSWORD="${DATA_ANALYSIS_CLOUD_READONLY_PASSWORD:-}"
READONLY_DATABASE="${DATA_ANALYSIS_CLOUD_READONLY_DATABASE:-}"
DRY_RUN=false

usage() {
  cat <<'USAGE'
Usage:
  deploy/enterprise-ai/scripts/apply-cloud-readonly-grants.sh [options]

Applies the governed AICopilot CloudReadOnly PostgreSQL role grants from
deploy/enterprise-ai/cloud-readonly/apply-readonly-grants.sql, then runs the
readonly privilege probe.

Options:
  --env-file <path>       Deploy .env file. Default: deploy/enterprise-ai/.env.
  --username <name>       Readonly role name. Default: parsed from connection string.
  --password <value>      Readonly role password. Default: parsed from connection string.
  --database <name>       Cloud database name. Default: parsed from connection string.
  --dry-run               Print the actions without touching PostgreSQL.
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

connection_part() {
  local key="$1"
  local value="$2"
  printf '%s' "$value" |
    tr ';' '\n' |
    awk -F= -v key="$key" 'tolower($1) == tolower(key) { print $2; exit }'
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

ensure_cloud_readonly_network() {
  local network
  local host_alias
  local container
  network="$(read_env_value DATA_ANALYSIS_CLOUD_READONLY_DOCKER_NETWORK)"
  host_alias="$(read_env_value DATA_ANALYSIS_CLOUD_READONLY_DB_HOST_ALIAS)"
  network="${network:-enterprise-ai-cloud-readonly}"
  host_alias="${host_alias:-cloud-postgres}"
  container="$(cloud_container_id)"

  if [ -z "$container" ]; then
    if [ "$DRY_RUN" = true ]; then
      printf '<cloud-postgres-container>\n'
      return
    fi

    fail "Cloud PostgreSQL container was not found for CloudReadOnly grants."
  fi

  if ! docker network inspect "$network" >/dev/null 2>&1; then
    if [ "$DRY_RUN" = true ]; then
      printf '[dry-run] docker network create --driver bridge %s\n' "$network"
    else
      docker network create --driver bridge "$network" >/dev/null
      printf 'Created Cloud readonly Docker network: %s\n' "$network"
    fi
  fi

  if docker inspect "$container" \
    --format '{{range $name, $_ := .NetworkSettings.Networks}}{{println $name}}{{end}}' |
    grep -Fx "$network" >/dev/null; then
    printf 'Cloud PostgreSQL container is already attached to network %s.\n' "$network" >&2
  elif [ "$DRY_RUN" = true ]; then
    printf '[dry-run] docker network connect --alias %s %s %s\n' "$host_alias" "$network" "$container"
  else
    docker network connect --alias "$host_alias" "$network" "$container"
    printf 'Attached Cloud PostgreSQL container to network %s as %s.\n' "$network" "$host_alias" >&2
  fi

  printf '%s\n' "$container"
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
      fail "Unknown apply-cloud-readonly-grants option: $1"
      ;;
  esac
  shift
done

[ -f "$ENV_FILE" ] || fail "Missing deploy .env file: $ENV_FILE"
[ -f "$APPLY_SQL" ] || fail "Missing CloudReadOnly grants SQL: $APPLY_SQL"
[ -x "$CHECK_SCRIPT" ] || fail "Missing executable check script: $CHECK_SCRIPT"
command -v docker >/dev/null || fail "Required command not found: docker"

connection_string="$(read_env_value DATA_ANALYSIS_CLOUD_READONLY_CONNECTION_STRING)"
READONLY_USERNAME="${READONLY_USERNAME:-$(connection_part Username "$connection_string")}"
READONLY_PASSWORD="${READONLY_PASSWORD:-$(connection_part Password "$connection_string")}"
READONLY_DATABASE="${READONLY_DATABASE:-$(connection_part Database "$connection_string")}"

[ -n "$READONLY_USERNAME" ] || fail "Missing readonly username."
[ -n "$READONLY_PASSWORD" ] || fail "Missing readonly password."
[ -n "$READONLY_DATABASE" ] || fail "Missing readonly database."
validate_identifier "$READONLY_USERNAME"

cloud_container="$(ensure_cloud_readonly_network)"

printf 'Applying CloudReadOnly grants: role=%s database=%s sql=%s\n' \
  "$READONLY_USERNAME" "$READONLY_DATABASE" "$APPLY_SQL"

if [ "$DRY_RUN" = true ]; then
  printf '[dry-run] docker exec <cloud-postgres> psql -v readonly_user=%s -d %s < %s\n' \
    "$READONLY_USERNAME" "$READONLY_DATABASE" "$APPLY_SQL"
else
  docker exec -i \
    -e READONLY_USER="$READONLY_USERNAME" \
    -e READONLY_PASSWORD="$READONLY_PASSWORD" \
    -e READONLY_DATABASE="$READONLY_DATABASE" \
    "$cloud_container" sh -lc \
      'psql -v ON_ERROR_STOP=1 -v readonly_user="$READONLY_USER" -v readonly_password="$READONLY_PASSWORD" -U "${POSTGRES_USER:-postgres}" -d "$READONLY_DATABASE"' \
    < "$APPLY_SQL"
fi

"$CHECK_SCRIPT" \
  --env-file "$ENV_FILE" \
  --username "$READONLY_USERNAME" \
  --password "$READONLY_PASSWORD" \
  --database "$READONLY_DATABASE" \
  ${DRY_RUN:+--dry-run}
