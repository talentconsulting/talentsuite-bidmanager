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

load_exports_from_json_object() {
  local json="$1"
  if ! command -v jq >/dev/null 2>&1; then
    fail "jq is required to parse AZD_INITIAL_ENVIRONMENT_CONFIG in azure mode."
  fi

  while IFS='=' read -r key value; do
    export_pair "$key" "$value"
  done < <(printf '%s' "$json" | jq -r 'to_entries[] | "\(.key)=\(.value|tostring)"')
}

ensure_parameters_aliases() {
  local key alias
  while IFS='=' read -r key _; do
    [ -n "$key" ] || continue
    case "$key" in
      Parameters__*) continue ;;
      *)
        if [ -z "${!key:-}" ]; then
          continue
        fi
        alias="Parameters__${key}"
        if [ -z "${!alias:-}" ]; then
          export "$alias=${!key}"
        fi
        ;;
    esac
  done < <(env)
}

if [[ "$MODE" == "local" ]]; then
  export TALENTSUITE_INFRA_MODE="local"

  # AppHost parameters (optional overrides)
  export Parameters__AuthenticationEnabled="true"
  export Parameters__UseInMemoryData="false"
  export Parameters__SqlPassword="Your_strong_password123!"
  export Parameters__KeycloakPassword="admin"

  # Optional AI ingestion settings (leave blank to use in-memory ingestion fallback)
  export DocumentIntelligence__Endpoint="https://rgp-doc-intelligence.cognitiveservices.azure.com/"
  export DocumentIntelligence__ApiKey=""
  export AzureOpenAI__Endpoint="https://rgp-test-epomai.openai.azure.com/"
  export AzureOpenAI__ApiKey=""
  export AzureOpenAI__ChatDeployment="gpt-4.1"
  export AzureAIFoundry__ProjectEndpoint="https://rgp-foundry.services.ai.azure.com/api/projects/proj-bidproj"
  export Agents__AgentId=""

  echo "Local mode env vars set."
elif [[ "$MODE" == "azure" ]]; then
  export TALENTSUITE_INFRA_MODE="azure"

  if [ -n "${AZD_INITIAL_ENVIRONMENT_CONFIG:-}" ]; then
    load_exports_from_json_object "$AZD_INITIAL_ENVIRONMENT_CONFIG"
  else
    if ! command -v azd >/dev/null 2>&1; then
      fail "azd is required in azure mode unless AZD_INITIAL_ENVIRONMENT_CONFIG is provided."
    fi

    if [ -n "${AZURE_ENV_NAME:-}" ]; then
      :
    elif [ -f ".azure/config.json" ] && command -v jq >/dev/null 2>&1; then
      AZURE_ENV_NAME="$(jq -r '.defaultEnvironment // empty' .azure/config.json)"
    fi

    [ -n "${AZURE_ENV_NAME:-}" ] || fail "Set AZURE_ENV_NAME or AZD_INITIAL_ENVIRONMENT_CONFIG before sourcing set-env.sh azure."
    load_exports_from_azd_env_text < <(azd env get-values --environment "$AZURE_ENV_NAME")
  fi

  ensure_parameters_aliases

  # Reasonable defaults if not present in supplied config.
  export Parameters__AuthenticationEnabled="${Parameters__AuthenticationEnabled:-${AuthenticationEnabled:-true}}"
  export Parameters__UseInMemoryData="${Parameters__UseInMemoryData:-${UseInMemoryData:-false}}"
  export Parameters__SqlPassword="${Parameters__SqlPassword:-${SqlPassword:-}}"
  export Parameters__KeycloakPassword="${Parameters__KeycloakPassword:-${KeycloakPassword:-}}"
  export Parameters__InviteEmailEnabled="${Parameters__InviteEmailEnabled:-${InviteEmailEnabled:-false}}"
  export Parameters__InviteFromEmail="${Parameters__InviteFromEmail:-${InviteFromEmail:-}}"
  export Parameters__InviteSmtpHost="${Parameters__InviteSmtpHost:-${InviteSmtpHost:-}}"
  export Parameters__InviteSmtpPort="${Parameters__InviteSmtpPort:-${InviteSmtpPort:-587}}"
  export Parameters__InviteSmtpEnableSsl="${Parameters__InviteSmtpEnableSsl:-${InviteSmtpEnableSsl:-true}}"
  export Parameters__InviteSmtpUsername="${Parameters__InviteSmtpUsername:-${InviteSmtpUsername:-}}"
  export Parameters__InviteSmtpPassword="${Parameters__InviteSmtpPassword:-${InviteSmtpPassword:-}}"

  test -n "${Parameters__KeycloakDbUsername:-${KeycloakDbUsername:-}}" || fail "Missing Keycloak DB username (Parameters__KeycloakDbUsername or KeycloakDbUsername)."
  test -n "${Parameters__KeycloakDbPassword:-${KeycloakDbPassword:-}}" || fail "Missing Keycloak DB password (Parameters__KeycloakDbPassword or KeycloakDbPassword)."

  echo "Azure mode env vars set from configuration."
else
  echo "Unknown mode: $MODE (use 'local' or 'azure')"
  return 1 2>/dev/null || exit 1
fi
