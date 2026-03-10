#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "seed-azd-env.sh failed at line $LINENO"' ERR

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
jq -e . /tmp/azd-initial.json >/dev/null || (echo "AZD_INITIAL_ENVIRONMENT_CONFIG is not valid JSON" && exit 1)

azd env new "$AZURE_ENV_NAME" --no-prompt || true

jq -r 'to_entries[] | "\(.key)=\(.value|tostring)"' /tmp/azd-initial.json > /tmp/azd-initial.env
azd env set --environment "$AZURE_ENV_NAME" --file /tmp/azd-initial.env --no-prompt || (echo "Failed to seed azd env from /tmp/azd-initial.env" && exit 1)

read_required_value() {
  local key="$1"
  local value
  value="$(jq -r --arg key "$key" '.[$key] // empty | tostring' /tmp/azd-initial.json)"
  if [ -z "$value" ] || [ "$value" = "null" ]; then
    echo "$key missing or empty in AZD_INITIAL_ENVIRONMENT_CONFIG"
    exit 1
  fi
  echo "$value"
}

read_value_or_default() {
  local key="$1"
  local default_value="$2"
  local value

  value="$(jq -r --arg key "$key" '.[$key] // empty | tostring' /tmp/azd-initial.json)"

  if [ -z "$value" ] || [ "$value" = "null" ]; then
    value="$default_value"
  fi

  value="$(printf '%s' "$value" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//')"
  echo "$value"
}

authentication_enabled="$(read_required_value "AuthenticationEnabled")"
use_in_memory_data="$(read_required_value "UseInMemoryData")"
sql_password="$(read_required_value "SqlPassword")"
keycloak_password="$(read_required_value "KeycloakPassword")"
keycloak_client_id="$(read_required_value "KeycloakClientId")"
keycloak_db_username="$(read_required_value "KeycloakDbUsername")"
keycloak_db_password="$(read_required_value "KeycloakDbPassword")"
invite_email_enabled="$(read_value_or_default "InviteEmailEnabled" "false")"
invite_from_email="$(read_value_or_default "InviteFromEmail" "")"
invite_smtp_host="$(read_value_or_default "InviteSmtpHost" "")"
invite_smtp_port="$(read_value_or_default "InviteSmtpPort" "587")"
invite_smtp_enable_ssl="$(read_value_or_default "InviteSmtpEnableSsl" "true")"
invite_smtp_username="$(read_required_value "InviteSmtpUsername")"
invite_smtp_password="$(read_required_value "InviteSmtpPassword")"

# Mask secrets in GitHub logs to reduce accidental exposure.
echo "::add-mask::$sql_password"
echo "::add-mask::$keycloak_password"
echo "::add-mask::$keycloak_db_password"
echo "::add-mask::$invite_smtp_password"

is_placeholder_value() {
  case "$1" in
    \<*\> | *your-smtp* ) return 0 ;;
    * ) return 1 ;;
  esac
}

test -n "$authentication_enabled" || (echo "AuthenticationEnabled missing in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
test -n "$use_in_memory_data" || (echo "UseInMemoryData missing in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
test -n "$keycloak_client_id" || (echo "KeycloakClientId missing in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
test -n "$keycloak_db_username" || (echo "KeycloakDbUsername missing or empty in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
[ "$keycloak_db_username" != "sa" ] || (echo "KeycloakDbUsername must not be 'sa' for Azure SQL" && exit 1)
[[ ! "$keycloak_db_username" =~ ^[Cc]loud[Ss][Aa] ]] || (echo "KeycloakDbUsername must not start with 'CloudSA' (invalid for Azure SQL login)." && exit 1)
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

# Azure SQL admin password rules: must not contain the admin login name and should not include whitespace.
sql_admin_login="sqladminrgp"
if [[ "$sql_password" =~ [[:space:]] ]]; then
  echo "SqlPassword must not contain whitespace."
  exit 1
fi
if [[ "${sql_password,,}" == *"${sql_admin_login,,}"* ]]; then
  echo "SqlPassword must not contain the SQL admin login name ('$sql_admin_login')."
  exit 1
fi

test -n "$invite_smtp_username" || (echo "InviteSmtpUsername missing or empty in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
test -n "$invite_smtp_password" || (echo "InviteSmtpPassword missing or empty in AZD_INITIAL_ENVIRONMENT_CONFIG" && exit 1)
is_placeholder_value "$invite_smtp_username" && (echo "InviteSmtpUsername appears to be a placeholder value" && exit 1)
is_placeholder_value "$invite_smtp_password" && (echo "InviteSmtpPassword appears to be a placeholder value" && exit 1)

azd env set --environment "$AZURE_ENV_NAME" AuthenticationEnabled "$authentication_enabled" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" UseInMemoryData "$use_in_memory_data" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" SqlPassword "$sql_password" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" Password "$sql_password" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" KeycloakPassword "$keycloak_password" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" KeycloakDbUsername "$keycloak_db_username" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" KeycloakDbPassword "$keycloak_db_password" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" InviteEmailEnabled "$invite_email_enabled" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" InviteFromEmail "$invite_from_email" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" InviteSmtpHost "$invite_smtp_host" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" InviteSmtpPort "$invite_smtp_port" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" InviteSmtpEnableSsl "$invite_smtp_enable_ssl" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" InviteSmtpUsername "$invite_smtp_username" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" InviteSmtpPassword "$invite_smtp_password" --no-prompt

azd env set --environment "$AZURE_ENV_NAME" AUTHENTICATION_ENABLED "$authentication_enabled" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" KEYCLOAK_CLIENT_ID "$keycloak_client_id" --no-prompt

# Fail fast if azd did not persist required Keycloak DB values.
vals_after_set="$(azd env get-values --environment "$AZURE_ENV_NAME" || (echo "Failed to read azd environment values after seed" && exit 1))"
echo "$vals_after_set" | grep -Eq '^KeycloakDbUsername=.+' || (echo "KeycloakDbUsername not persisted in azd env" && exit 1)
echo "$vals_after_set" | grep -Eq '^KeycloakDbPassword=.+' || (echo "KeycloakDbPassword not persisted in azd env" && exit 1)
