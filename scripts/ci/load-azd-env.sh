#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "load-azd-env.sh failed at line $LINENO"' ERR

require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "Missing required environment variable: $name"
    exit 1
  fi
}

require_env "AZURE_ENV_NAME"
require_env "GITHUB_ENV"

normalize_env_value() {
  local value="$1"

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

azd_env_file="$(mktemp)"
azd env get-values --environment "$AZURE_ENV_NAME" > "$azd_env_file"

while IFS= read -r line; do
  [ -z "$line" ] && continue
  case "$line" in
    \#*) continue ;;
  esac

  key="${line%%=*}"
  value="${line#*=}"

  key="$(printf '%s' "$key" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//')"
  value="$(printf '%s' "$value" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//')"
  value="$(normalize_env_value "$value")"

  [ -n "$key" ] || continue

  # Mask only sensitive values; masking everything hides useful diagnostics
  # (for example booleans such as true/false).
  if [ -n "$value" ]; then
    case "$key" in
      SqlPassword|Password|KeycloakPassword|KeycloakDbPassword|InviteSmtpPassword|GoogleDriveSyncServiceAccountJsonBase64)
        echo "::add-mask::$value"
        ;;
      *ConnectionString*|*Secret*|*Token*|*ClientSecret*|*ApiKey*)
        echo "::add-mask::$value"
        ;;
    esac
  fi

  printf '%s=%s\n' "$key" "$value" >> "$GITHUB_ENV"
done < "$azd_env_file"
