#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "configure-keycloak-smtp.sh failed at line $LINENO"' ERR

require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "Missing required environment variable: $name"
    exit 1
  fi
}

require_env "AZURE_ENV_NAME"
require_env "KeycloakPassword"
require_env "InviteSmtpHost"
require_env "InviteSmtpPort"
require_env "InviteFromEmail"
require_env "InviteSmtpUsername"
require_env "InviteSmtpPassword"

resource_group="rg-${AZURE_ENV_NAME}"
realm_name="TalentConsulting"
admin_user="admin"

keycloak_fqdn="$(az containerapp show \
  --name "keycloak" \
  --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn \
  -o tsv)"

test -n "$keycloak_fqdn" || (echo "Could not resolve Keycloak FQDN" && exit 1)

keycloak_base_url="https://${keycloak_fqdn}"
token_url="${keycloak_base_url}/realms/master/protocol/openid-connect/token"
realm_admin_url="${keycloak_base_url}/admin/realms/${realm_name}"

access_token="$(curl -fsS \
  -X POST "$token_url" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "client_id=admin-cli" \
  --data-urlencode "username=${admin_user}" \
  --data-urlencode "password=${KeycloakPassword}" \
  --data-urlencode "grant_type=password" \
  | jq -r '.access_token // empty')"

test -n "$access_token" || (echo "Failed to obtain Keycloak admin token" && exit 1)

# Port 465 = direct SSL; port 587 = STARTTLS; otherwise honour InviteSmtpEnableSsl
smtp_ssl="false"
smtp_starttls="false"
if [ "${InviteSmtpEnableSsl:-false}" = "true" ]; then
  if [ "${InviteSmtpPort}" = "587" ]; then
    smtp_starttls="true"
  else
    smtp_ssl="true"
  fi
fi

smtp_payload="$(jq -n \
  --arg host    "${InviteSmtpHost}" \
  --arg port    "${InviteSmtpPort}" \
  --arg from    "${InviteFromEmail}" \
  --arg ssl     "$smtp_ssl" \
  --arg starttls "$smtp_starttls" \
  --arg user    "${InviteSmtpUsername}" \
  --arg password "${InviteSmtpPassword}" \
  '{
    smtpServer: {
      host:            $host,
      port:            $port,
      from:            $from,
      fromDisplayName: "TalentSuite",
      ssl:             $ssl,
      starttls:        $starttls,
      auth:            "true",
      user:            $user,
      password:        $password
    }
  }')"

update_status="$(printf '%s' "$smtp_payload" \
  | curl -sS -o /tmp/keycloak-smtp-update.json -w "%{http_code}" \
      -X PUT "$realm_admin_url" \
      -H "Authorization: Bearer ${access_token}" \
      -H "Content-Type: application/json" \
      --data-binary @-)"

if [ "$update_status" != "204" ] && [ "$update_status" != "200" ]; then
  echo "Failed to configure Keycloak SMTP for realm '${realm_name}': HTTP ${update_status}"
  cat /tmp/keycloak-smtp-update.json || true
  exit 1
fi

echo "Configured Keycloak SMTP for realm '${realm_name}' using ${InviteSmtpHost}:${InviteSmtpPort}"
