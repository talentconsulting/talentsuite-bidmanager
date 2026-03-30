#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   source ./set-env.sh local
#   source ./set-env.sh azure
#
# Use "source" so vars stay in your current shell.

MODE="${1:-local}"

fail() {
  echo "$1"
  return 1 2>/dev/null || exit 1
}

trim() {
  local value="$1"
  value="${value#"${value%%[![:space:]]*}"}"
  value="${value%"${value##*[![:space:]]}"}"
  printf '%s' "$value"
}

export_pair() {
  local key="$1"
  local value="$2"
  [ -n "$key" ] || return 0
  export "$key=$value"
}

load_exports_from_azd_env_text() {
  local line key value
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    case "$line" in
      \#*) continue ;;
    esac

    key="${line%%=*}"
    value="${line#*=}"

    key="$(trim "$key")"
    value="$(trim "$value")"

    if [ "${#value}" -ge 2 ] && [ "${value#\"}" != "$value" ] && [ "${value%\"}" != "$value" ]; then
      value="${value#\"}"
      value="${value%\"}"
    fi

    export_pair "$key" "$value"
  done
}

load_local_env_file() {
  local file_path="$1"
  [ -f "$file_path" ] || return 0
  load_exports_from_azd_env_text < "$file_path"
}

if [[ "$MODE" == "local" ]]; then
  load_local_env_file ".env.local"
  export TALENTSUITE_INFRA_MODE="local"

  # AppHost parameters (optional overrides)
  export AuthenticationEnabled="${AuthenticationEnabled:-true}"
  export UseInMemoryData="${UseInMemoryData:-false}"
  export SqlPassword="${SqlPassword:-Your_strong_password123!}"
  export KeycloakPassword="${KeycloakPassword:-admin}"

  # Optional AI ingestion settings (leave blank to use in-memory ingestion fallback)
  export DocumentIntelligence__Endpoint="https://rgp-doc-intelligence.cognitiveservices.azure.com/"
  export DocumentIntelligence__ApiKey=""
  export AzureOpenAI__Endpoint="https://rgp-test-epomai.openai.azure.com/"
  export AzureOpenAI__ApiKey=""
  export AzureOpenAI__ChatDeployment="gpt-4-1"
  export AzureAIFoundry__ProjectEndpoint="https://rgp-foundry.services.ai.azure.com/api/projects/proj-bidproj"
  export Agents__AgentId=""

  echo "Local mode env vars set."
elif [[ "$MODE" == "azure" ]]; then
  load_local_env_file ".env.local"
  load_local_env_file ".env.azure.local"
  export TALENTSUITE_INFRA_MODE="azure"

  if [ -n "${AZURE_ENV_NAME:-}" ]; then
    :
  elif [ -f ".azure/config.json" ] && command -v jq >/dev/null 2>&1; then
    AZURE_ENV_NAME="$(jq -r '.defaultEnvironment // empty' .azure/config.json)"
  fi

  if [ -n "${AZURE_ENV_NAME:-}" ] && command -v azd >/dev/null 2>&1; then
    load_exports_from_azd_env_text < <(azd env get-values --environment "$AZURE_ENV_NAME")
  fi

  # Reasonable defaults if not present in supplied config.
  export AuthenticationEnabled="${AuthenticationEnabled:-true}"
  export UseInMemoryData="${UseInMemoryData:-false}"
  export InviteEmailEnabled="${InviteEmailEnabled:-false}"
  export InviteFromEmail="${InviteFromEmail:-}"
  export InviteSmtpHost="${InviteSmtpHost:-}"
  export InviteSmtpPort="${InviteSmtpPort:-587}"
  export InviteSmtpEnableSsl="${InviteSmtpEnableSsl:-true}"
  export InviteSmtpUsername="${InviteSmtpUsername:-}"
  export InviteSmtpPassword="${InviteSmtpPassword:-}"

  test -n "${KeycloakDbUsername:-}" || fail "Missing KeycloakDbUsername."
  test -n "${KeycloakDbPassword:-}" || fail "Missing KeycloakDbPassword."
  test -n "${AZURE_SUBSCRIPTION_ID:-}" || fail "Missing AZURE_SUBSCRIPTION_ID. Set it in .env.azure.local or your shell environment."

  echo "Azure mode env vars set from configuration."
else
  echo "Unknown mode: $MODE (use 'local' or 'azure')"
  return 1 2>/dev/null || exit 1
fi
