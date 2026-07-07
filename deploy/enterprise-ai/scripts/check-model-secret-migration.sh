#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-$(cd "$SCRIPT_DIR/.." && pwd)}"
ENV_FILE="${ENV_FILE:-$DEPLOY_DIR/.env}"
COMPOSE_FILE="${COMPOSE_FILE:-$DEPLOY_DIR/docker-compose.yaml}"
DRY_RUN=false

usage() {
  cat <<'USAGE'
Usage:
  deploy/enterprise-ai/scripts/check-model-secret-migration.sh [options]

Verifies that AICopilot model and embedding API keys have no legacy encv1
or unprotected non-empty values left in the production database, and that
all encv2 values can be decrypted with the current API key encryption key.

Options:
  --env-file <path>       Deploy .env file. Default: deploy/enterprise-ai/.env.
  --compose-file <path>   Docker compose file. Default: deploy/enterprise-ai/docker-compose.yaml.
  --dry-run               Print the SQL check without connecting to services.
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

check_model_secret_decryptability() {
  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] docker compose run aicopilot-migration with MigrationWorker__CheckSecretsOnly=true to verify encv2 decryptability with the current key.\n'
    return
  fi

  if ! compose run --rm --no-deps \
    -e MigrationWorker__CheckSecretsOnly=true \
    aicopilot-migration; then
    printf 'AICopilot model secret migration attestation failed while verifying encv2 decryptability with the current encryption key.\n' >&2
    printf 'Run a deploy including --services migration, restore the correct AICOPILOT_API_KEY_ENCRYPTION_KEY, or ask an administrator to re-enter affected API keys.\n' >&2
    exit 65
  fi

  printf 'AICopilot model secret decryptability attestation passed with the current encryption key.\n'
}

check_model_secret_migration() {
  local postgres_user
  local postgres_db
  local result_file

  if [ "$DRY_RUN" = true ]; then
    printf '[dry-run] docker compose exec postgres psql and verify language/embedding api_key legacy_count=0 and unprotected_count=0.\n'
    check_model_secret_decryptability
    return
  fi

  postgres_user="$(read_env_value POSTGRES_USER)"
  postgres_db="$(read_env_value POSTGRES_DB)"
  [ -n "$postgres_user" ] || fail "POSTGRES_USER is required in $ENV_FILE."
  [ -n "$postgres_db" ] || fail "POSTGRES_DB is required in $ENV_FILE."

  result_file="$(mktemp "${TMPDIR:-/tmp}/aicopilot-secret-migration.XXXXXX")"
  if ! compose exec -T postgres psql \
    -v ON_ERROR_STOP=1 \
    -U "$postgres_user" \
    -d "$postgres_db" \
    -t -A -F '|' \
    -c "
      SELECT 'aigateway.language_models' AS table_name,
             count(*) FILTER (WHERE api_key LIKE 'encv1:%') AS legacy_count,
             count(*) FILTER (WHERE api_key IS NOT NULL AND api_key <> '' AND api_key NOT LIKE 'encv2:%') AS unprotected_count
        FROM aigateway.language_models
      UNION ALL
      SELECT 'rag.embedding_models',
             count(*) FILTER (WHERE api_key LIKE 'encv1:%'),
             count(*) FILTER (WHERE api_key IS NOT NULL AND api_key <> '' AND api_key NOT LIKE 'encv2:%')
        FROM rag.embedding_models;" > "$result_file"; then
    printf 'AICopilot model secret migration attestation failed while querying PostgreSQL.\n' >&2
    printf 'Verify the postgres service is running, migration has created aigateway/rag schemas, and rerun a deploy including --services migration before starting runtime services.\n' >&2
    if [ -s "$result_file" ]; then
      cat "$result_file" >&2
    fi
    rm -f "$result_file"
    exit 65
  fi

  if ! awk -F'|' '
    NF == 3 {
      found += 1
      if ($2 != "0" || $3 != "0") {
        bad = 1
      }
    }
    END {
      exit !(found == 2 && bad != 1)
    }
  ' "$result_file"; then
    printf 'AICopilot model secret migration attestation failed. Expected legacy_count=0 and unprotected_count=0.\n' >&2
    cat "$result_file" >&2
    rm -f "$result_file"
    exit 65
  fi

  printf 'AICopilot model secret migration attestation passed:\n'
  awk -F'|' '{ printf "  - %s legacy_count=%s unprotected_count=%s\n", $1, $2, $3 }' "$result_file"
  rm -f "$result_file"
  check_model_secret_decryptability
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
    --dry-run)
      DRY_RUN=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown check-model-secret-migration option: $1"
      ;;
  esac
  shift
done

if [ "$DRY_RUN" != true ]; then
  [ -f "$ENV_FILE" ] || fail "Missing deploy .env file: $ENV_FILE"
  [ -f "$COMPOSE_FILE" ] || fail "Missing docker compose file: $COMPOSE_FILE"
  command -v docker >/dev/null || fail "Required command not found: docker"
  docker compose version >/dev/null
fi

check_model_secret_migration
