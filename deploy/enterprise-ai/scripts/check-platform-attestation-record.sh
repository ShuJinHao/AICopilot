#!/usr/bin/env bash
set -euo pipefail

RECORD_PATH=""

usage() {
  cat <<'USAGE'
Usage:
  deploy/enterprise-ai/scripts/check-platform-attestation-record.sh --record <path>

Lint a filled AICopilot runner platform attestation record for AI-SEC-010.
This script does not verify GitHub, Vault, OIDC, or runner infrastructure by
itself. It only prevents an obviously incomplete manual attestation record from
being accepted as release evidence.

The filled production record should normally be stored in the release or
infrastructure change log, not committed to this repository.

Options:
  --record <path>   Filled attestation markdown file.
  --help            Show this help.
USAGE
}

fail() {
  printf '%s\n' "$*" >&2
  exit 64
}

normalize_record_line() {
  printf '%s' "$1" | sed -E 's/^[[:space:]-]+//; s/[[:space:]]+$//'
}

require_text() {
  local pattern="$1"
  local description="$2"

  if ! grep -Eq "$pattern" "$RECORD_PATH"; then
    fail "Platform attestation record is missing required evidence: $description"
  fi
}

require_non_empty_field() {
  local field="$1"
  local line
  line="$(grep -E "^[[:space:]-]*${field}:[[:space:]]*[^[:space:]].*$" "$RECORD_PATH" | head -n 1 || true)"

  if [ -z "$line" ]; then
    fail "Platform attestation record is missing required sign-off value: $field"
  fi

  line="$(normalize_record_line "$line")"
  if printf '%s\n' "$line" | grep -Eiq ':[[:space:]]*(n/?a|none|not applicable|pending|unassigned|unsigned)[[:space:]]*$'; then
    fail "Platform attestation record has an invalid sign-off value: $field"
  fi
}

require_non_empty_evidence_field() {
  local field="$1"
  local description="$2"
  local line
  line="$(grep -E "^[[:space:]-]*${field}:[[:space:]]*[^[:space:]].*$" "$RECORD_PATH" | head -n 1 || true)"

  if [ -z "$line" ]; then
    fail "Platform attestation record is missing required evidence: $description"
  fi

  line="$(normalize_record_line "$line")"
  if printf '%s\n' "$line" | grep -Eiq ':[[:space:]]*(n/?a|none|not applicable|pending|unassigned|unsigned)[[:space:]]*$'; then
    fail "Platform attestation record has invalid required evidence: $description"
  fi
}

require_credential_strategy_evidence() {
  if grep -Eiq 'approved infrastructure exception' "$RECORD_PATH"; then
    require_non_empty_evidence_field 'Ticket or change id' 'approved exception ticket or change id'
    require_non_empty_evidence_field 'Exception owner' 'approved exception owner'
    require_non_empty_evidence_field 'Due date' 'approved exception due date'
    require_non_empty_evidence_field 'Current mitigation' 'approved exception mitigation'
    return
  fi

  if grep -Eiq '(OIDC/Vault|short-lived credentials).*(implemented|enabled|active)|(implemented|enabled|active).*(OIDC/Vault|short-lived credentials)' "$RECORD_PATH"; then
    return
  fi

  fail "Platform attestation record is missing required evidence: credential strategy implementation or approved infrastructure exception"
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --record)
      shift
      RECORD_PATH="${1:-}"
      ;;
    --record=*)
      RECORD_PATH="${1#--record=}"
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      fail "Unknown check-platform-attestation-record option: $1"
      ;;
  esac
  shift
done

[ -n "$RECORD_PATH" ] || fail "--record is required."
[ -f "$RECORD_PATH" ] || fail "Platform attestation record not found: $RECORD_PATH"

if grep -Eq '<[^>]+>' "$RECORD_PATH"; then
  fail "Platform attestation record still contains template placeholders."
fi

if grep -Eq '(^|[[:space:]])- \[ \]' "$RECORD_PATH"; then
  fail "Platform attestation record contains unchecked checklist items."
fi

if grep -Eiq '(^|[^[:alnum:]_])(TODO|TBD|FIXME|pending|not[[:space:]]+verified|unverified|not[[:space:]]+implemented|not[[:space:]]+available|unknown|n/?a)([^[:alnum:]_]|$)' "$RECORD_PATH"; then
  fail "Platform attestation record contains unresolved placeholder wording."
fi

require_text 'AI-SEC-010' 'AI-SEC-010 scope'
require_text 'check-runner-security-attestation\.sh' 'runner machine attestation command'
require_text 'GitHub production environment' 'GitHub production environment section'
require_text 'Environment secrets.*restricted|restricted.*Environment secrets|secret inventory' 'production environment secret restriction evidence'
require_text 'contents:[[:space:]]*read' 'least-privilege workflow permissions'
require_text 'self-hosted.*iiot-linux-prod|iiot-linux-prod.*self-hosted' 'self-hosted production runner label'
require_text 'No production.*secret-touching.*GitHub hosted runners|production.*secret-touching.*do not use GitHub hosted runners|no GitHub hosted runners' 'no hosted runner evidence for production or secret-touching workflows'
require_credential_strategy_evidence
require_non_empty_field 'Platform owner'
require_non_empty_field 'Release owner'

printf 'AICopilot platform attestation record lint passed: %s\n' "$RECORD_PATH"
