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

read_env_alias() {
  local primary="$1"
  local secondary="${2:-}"

  if [ -n "${!primary:-}" ]; then
    printf '%s' "${!primary}"
    return 0
  fi

  if [ -n "$secondary" ] && [ -n "${!secondary:-}" ]; then
    printf '%s' "${!secondary}"
    return 0
  fi

  return 1
}

read_required_env_alias() {
  local primary="$1"
  local secondary="${2:-}"
  local value

  value="$(read_env_alias "$primary" "$secondary" || true)"
  if [ -z "$value" ]; then
    if [ -n "$secondary" ]; then
      echo "Missing required environment variable: $primary or $secondary"
    else
      echo "Missing required environment variable: $primary"
    fi
    exit 1
  fi

  printf '%s' "$value"
}

read_env_alias_or_default() {
  local primary="$1"
  local secondary="${2:-}"
  local default_value="$3"
  local value

  value="$(read_env_alias "$primary" "$secondary" || true)"
  if [ -z "$value" ]; then
    value="$default_value"
  fi

  value="$(printf '%s' "$value" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//')"
  printf '%s' "$value"
}

require_env "AZURE_ENV_NAME"

azd env new "$AZURE_ENV_NAME" --no-prompt || true

normalize_secret_value() {
  local value="$1"

  # Remove Windows carriage returns if present.
  value="$(printf '%s' "$value" | tr -d '\r')"

  # Strip one pair of matching surrounding quotes.
  if [ "${#value}" -ge 2 ] && [ "${value#\"}" != "$value" ] && [ "${value%\"}" != "$value" ]; then
    value="${value#\"}"
    value="${value%\"}"
  elif [ "${#value}" -ge 2 ] && [ "${value#\'}" != "$value" ] && [ "${value%\'}" != "$value" ]; then
    value="${value#\'}"
    value="${value%\'}"
  fi

  # Handle escaped-quote wrappers like \"secret\".
  if [ "${#value}" -ge 4 ] && [ "${value#\\\"}" != "$value" ] && [ "${value%\\\"}" != "$value" ]; then
    value="${value#\\\"}"
    value="${value%\\\"}"
  fi

  echo "$value"
}

authentication_enabled="$(read_required_env_alias "AuthenticationEnabled" "AUTHENTICATION_ENABLED")"
use_in_memory_data="$(read_required_env_alias "UseInMemoryData" "USE_IN_MEMORY_DATA")"
sql_password="$(read_required_env_alias "SqlPassword" "SQL_PASSWORD")"
keycloak_password="$(read_required_env_alias "KeycloakPassword" "KEYCLOAK_PASSWORD")"
keycloak_client_id="$(read_required_env_alias "KeycloakClientId" "KEYCLOAK_CLIENT_ID")"
keycloak_db_username="$(read_required_env_alias "KeycloakDbUsername" "KEYCLOAK_DB_USERNAME")"
keycloak_db_password="$(read_required_env_alias "KeycloakDbPassword" "KEYCLOAK_DB_PASSWORD")"
invite_email_enabled="$(read_env_alias_or_default "InviteEmailEnabled" "INVITE_EMAIL_ENABLED" "false")"
invite_from_email="$(read_env_alias_or_default "InviteFromEmail" "INVITE_FROM_EMAIL" "")"
invite_smtp_host="$(read_env_alias_or_default "InviteSmtpHost" "INVITE_SMTP_HOST" "")"
invite_smtp_port="$(read_env_alias_or_default "InviteSmtpPort" "INVITE_SMTP_PORT" "587")"
invite_smtp_enable_ssl="$(read_env_alias_or_default "InviteSmtpEnableSsl" "INVITE_SMTP_ENABLE_SSL" "true")"
invite_smtp_username="$(read_required_env_alias "InviteSmtpUsername" "INVITE_SMTP_USERNAME")"
invite_smtp_password="$(read_required_env_alias "InviteSmtpPassword" "INVITE_SMTP_PASSWORD")"

sql_password="$(normalize_secret_value "$sql_password")"
keycloak_password="$(normalize_secret_value "$keycloak_password")"
keycloak_db_password="$(normalize_secret_value "$keycloak_db_password")"
invite_smtp_password="$(normalize_secret_value "$invite_smtp_password")"

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

test -n "$authentication_enabled" || (echo "AuthenticationEnabled/AUTHENTICATION_ENABLED missing" && exit 1)
test -n "$use_in_memory_data" || (echo "UseInMemoryData/USE_IN_MEMORY_DATA missing" && exit 1)
test -n "$keycloak_client_id" || (echo "KeycloakClientId/KEYCLOAK_CLIENT_ID missing" && exit 1)
test -n "$keycloak_db_username" || (echo "KeycloakDbUsername/KEYCLOAK_DB_USERNAME missing or empty" && exit 1)
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
sql_admin_login="tsinfrausr"
if [[ "$sql_password" =~ [[:space:]] ]]; then
  echo "SqlPassword must not contain whitespace."
  exit 1
fi
if [[ "$sql_password" =~ ^\".*\"$ ]] || [[ "$sql_password" =~ ^\'.*\'$ ]]; then
  echo "SqlPassword appears to be wrapped in quotes. Remove surrounding quotes from the value."
  exit 1
fi
if [[ "${sql_password,,}" == *"${sql_admin_login,,}"* ]]; then
  echo "SqlPassword must not contain the SQL admin login name ('$sql_admin_login')."
  exit 1
fi
if [[ "${sql_password,,}" == *"password"* ]]; then
  echo "SqlPassword must not contain the word 'password'."
  exit 1
fi
# Disallow control characters; printable punctuation is allowed.
if printf '%s' "$sql_password" | LC_ALL=C grep -q '[[:cntrl:]]'; then
  echo "SqlPassword must not contain control characters."
  exit 1
fi
if [[ ! "$sql_password" =~ ^[A-Za-z0-9\!@\#%\^\*\(\)_\+=\.,:-]+$ ]]; then
  echo "SqlPassword contains unsupported characters. Use only letters, numbers, and !@#%^*()_+=.,:-"
  exit 1
fi

test -n "$invite_smtp_username" || (echo "InviteSmtpUsername/INVITE_SMTP_USERNAME missing or empty" && exit 1)
test -n "$invite_smtp_password" || (echo "InviteSmtpPassword/INVITE_SMTP_PASSWORD missing or empty" && exit 1)
is_placeholder_value "$invite_smtp_username" && (echo "InviteSmtpUsername appears to be a placeholder value" && exit 1)
is_placeholder_value "$invite_smtp_password" && (echo "InviteSmtpPassword appears to be a placeholder value" && exit 1)

azd env set --environment "$AZURE_ENV_NAME" AuthenticationEnabled "$authentication_enabled" --no-prompt
azd env set --environment "$AZURE_ENV_NAME" UseInMemoryData "$use_in_memory_data" --no-prompt
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
