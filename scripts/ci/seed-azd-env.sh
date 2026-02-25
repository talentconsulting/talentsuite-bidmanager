#!/usr/bin/env bash
set -euo pipefail

require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "Missing required environment variable: $name"
    exit 1
  fi
}

require_env "AZURE_ENV_NAME"
require_env "AZD_INITIAL_ENVIRONMENT_CONFIG"

printf '%s' "$AZD_INITIAL_ENVIRONMENT_CONFIG" > /tmp/azd-initial.json
jq -e . /tmp/azd-initial.json >/dev/null

azd env new "$AZURE_ENV_NAME" --no-prompt || true

jq -r 'to_entries[] | "\(.key)=\(.value|tostring)"' /tmp/azd-initial.json > /tmp/azd-initial.env
azd env set --environment "$AZURE_ENV_NAME" --file /tmp/azd-initial.env --no-prompt

authentication_enabled="$(jq -er '.AuthenticationEnabled // empty' /tmp/azd-initial.json)"
sql_password="$(jq -er '.SqlPassword // empty' /tmp/azd-initial.json)"
keycloak_password="$(jq -er '.KeycloakPassword // empty' /tmp/azd-initial.json)"
keycloak_client_id="$(jq -er '.KeycloakClientId // empty' /tmp/azd-initial.json)"
keycloak_db_username="$(jq -er '.KeycloakDbUsername // empty' /tmp/azd-initial.json)"
keycloak_db_password="$(jq -er '.KeycloakDbPassword // empty' /tmp/azd-initial.json)"
invite_smtp_username="$(jq -er '.InviteSmtpUsername // empty' /tmp/azd-initial.json)"
invite_smtp_password="$(jq -er '.InviteSmtpPassword // empty' /tmp/azd-initial.json)"

# Intentional no-op read to preserve existing validation behavior.
test -n "$keycloak_password" || (echo "KeycloakPassword missing in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)

is_placeholder_value() {
  case "$1" in
    \<*\> | *your-smtp* ) return 0 ;;
    * ) return 1 ;;
  esac
}

test -n "$authentication_enabled" || (echo "AuthenticationEnabled missing in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
test -n "$keycloak_client_id" || (echo "KeycloakClientId missing in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
test -n "$keycloak_db_username" || (echo "KeycloakDbUsername missing or empty in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
[ "$keycloak_db_username" != "sa" ] || (echo "KeycloakDbUsername must not be 'sa' for Azure SQL" && exit 1)
is_placeholder_value "$keycloak_db_username" && (echo "KeycloakDbUsername appears to be a placeholder value" && exit 1)
is_placeholder_value "$keycloak_client_id" && (echo "KeycloakClientId appears to be a placeholder value" && exit 1)

validate_strong_password() {
  local value="$1"
  local field="$2"
  if [ "${#value}" -lt 12 ] \
    || [[ ! "$value" =~ [A-Z] ]] \
    || [[ ! "$value" =~ [a-z] ]] \
    || [[ ! "$value" =~ [0-9] ]] \
    || [[ ! "$value" =~ [^A-Za-z0-9] ]]; then
    echo "$field must be at least 12 chars and include upper, lower, number, and special character"
    exit 1
  fi
}

validate_strong_password "$sql_password" "SqlPassword"
validate_strong_password "$keycloak_db_password" "KeycloakDbPassword"

test -n "$invite_smtp_username" || (echo "InviteSmtpUsername missing or empty in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
test -n "$invite_smtp_password" || (echo "InviteSmtpPassword missing or empty in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
is_placeholder_value "$invite_smtp_username" && (echo "InviteSmtpUsername appears to be a placeholder value" && exit 1)
is_placeholder_value "$invite_smtp_password" && (echo "InviteSmtpPassword appears to be a placeholder value" && exit 1)

# Add explicit alias keys used by frontend/runtime config resolution.
azd env set --environment "$AZURE_ENV_NAME" AUTHENTICATION_ENABLED "$authentication_enabled" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" KEYCLOAK_CLIENT_ID "$keycloak_client_id" --no-prompt

# Fail fast if azd did not persist required Keycloak DB values.
vals_after_set="$(azd env get-values --environment "$AZURE_ENV_NAME")"
echo "$vals_after_set" | grep -Eq '^KeycloakDbUsername=.+' || (echo "KeycloakDbUsername not persisted in azd env" && exit 1)
echo "$vals_after_set" | grep -Eq '^KeycloakDbPassword=.+' || (echo "KeycloakDbPassword not persisted in azd env" && exit 1)
