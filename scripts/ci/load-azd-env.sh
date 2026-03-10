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

  if [ "${#value}" -ge 2 ] && [ "${value#\"}" != "$value" ] && [ "${value%\"}" != "$value" ]; then
    value="${value#\"}"
    value="${value%\"}"
  fi

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
