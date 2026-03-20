#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "import-keycloak-realm.sh failed at line $LINENO"' ERR

require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "Missing required environment variable: $name"
    exit 1
  fi
}

require_env "AZURE_ENV_NAME"
require_env "KeycloakPassword"

resource_group="rg-${AZURE_ENV_NAME}"
realm_name="TalentConsulting"
realm_file="TalentSuite.AppHost/keycloak/realms/TalentConsulting-realm.json"
admin_user="admin"
frontend_client_id="$(jq -r '.clients[] | select(.clientId == "talentsuite-frontend") | .clientId' "$realm_file")"
frontend_client_json="$(jq -c '.clients[] | select(.clientId == "talentsuite-frontend")' "$realm_file")"

test -f "$realm_file" || (echo "Realm file not found: $realm_file" && exit 1)
test -n "$frontend_client_id" || (echo "Realm file does not define the talentsuite-frontend client" && exit 1)
test -n "$frontend_client_json" || (echo "Realm file does not define the talentsuite-frontend client payload" && exit 1)

keycloak_fqdn="$(az containerapp show \
  --name "keycloak" \
  --resource-group "$resource_group" \
  --query properties.configuration.ingress.fqdn \
  -o tsv)"

test -n "$keycloak_fqdn" || (echo "Could not resolve Keycloak FQDN" && exit 1)

keycloak_base_url="https://${keycloak_fqdn}"
token_url="${keycloak_base_url}/realms/master/protocol/openid-connect/token"
realm_admin_url="${keycloak_base_url}/admin/realms/${realm_name}"
create_realm_url="${keycloak_base_url}/admin/realms"

for _ in $(seq 1 30); do
  if curl -fsS "${keycloak_base_url}/realms/master/.well-known/openid-configuration" >/dev/null 2>&1; then
    break
  fi
  sleep 10
done

access_token="$(curl -fsS \
  -X POST "$token_url" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "client_id=admin-cli" \
  --data-urlencode "username=${admin_user}" \
  --data-urlencode "password=${KeycloakPassword}" \
  --data-urlencode "grant_type=password" \
  | jq -r '.access_token // empty')"

test -n "$access_token" || (echo "Failed to obtain Keycloak admin token" && exit 1)

realm_status="$(curl -sS -o /tmp/keycloak-realm-check.json -w "%{http_code}" \
  -H "Authorization: Bearer ${access_token}" \
  "$realm_admin_url")"

if [ "$realm_status" = "200" ]; then
  client_status="$(curl -sS -o /tmp/keycloak-client-check.json -w "%{http_code}" \
    -H "Authorization: Bearer ${access_token}" \
    "${keycloak_base_url}/admin/realms/${realm_name}/clients?clientId=${frontend_client_id}")"

  if [ "$client_status" != "200" ]; then
    echo "Unexpected response checking Keycloak client '${frontend_client_id}': HTTP ${client_status}"
    cat /tmp/keycloak-client-check.json || true
    exit 1
  fi

  client_count="$(jq 'length' /tmp/keycloak-client-check.json)"
  if [ "$client_count" -gt 0 ]; then
    client_id_internal="$(jq -r '.[0].id // empty' /tmp/keycloak-client-check.json)"
    test -n "$client_id_internal" || (echo "Keycloak client '${frontend_client_id}' exists but id could not be resolved." && exit 1)

    client_update_url="${keycloak_base_url}/admin/realms/${realm_name}/clients/${client_id_internal}"
    merged_client_payload="$(jq -c \
      --argjson desired "$frontend_client_json" \
      '.[0]
       | .redirectUris = (((.redirectUris // []) + ($desired.redirectUris // [])) | unique)
       | .webOrigins = (((.webOrigins // []) + ($desired.webOrigins // [])) | unique)' \
      /tmp/keycloak-client-check.json)"

    update_status="$(printf '%s' "$merged_client_payload" \
      | curl -sS -o /tmp/keycloak-client-update.json -w "%{http_code}" \
          -X PUT "$client_update_url" \
          -H "Authorization: Bearer ${access_token}" \
          -H "Content-Type: application/json" \
          --data-binary @-)"

    if [ "$update_status" != "204" ] && [ "$update_status" != "200" ]; then
      echo "Failed to update Keycloak client '${frontend_client_id}': HTTP ${update_status}"
      cat /tmp/keycloak-client-update.json || true
      exit 1
    fi

    echo "Keycloak realm '${realm_name}' and client '${frontend_client_id}' already exist. Updated redirect URIs and web origins."
    exit 0
  fi

  echo "Keycloak realm '${realm_name}' exists but client '${frontend_client_id}' is missing. Creating client."
  create_client_status="$(jq -c '.clients[] | select(.clientId == "talentsuite-frontend")' "$realm_file" \
    | curl -sS -o /tmp/keycloak-client-create.json -w "%{http_code}" \
        -X POST "${keycloak_base_url}/admin/realms/${realm_name}/clients" \
        -H "Authorization: Bearer ${access_token}" \
        -H "Content-Type: application/json" \
        --data-binary @-)"

  if [ "$create_client_status" != "201" ] && [ "$create_client_status" != "409" ]; then
    echo "Failed to create Keycloak client '${frontend_client_id}': HTTP ${create_client_status}"
    cat /tmp/keycloak-client-create.json || true
    exit 1
  fi

  echo "Created Keycloak client '${frontend_client_id}'."
  exit 0
fi

if [ "$realm_status" != "404" ]; then
  echo "Unexpected response checking Keycloak realm '${realm_name}': HTTP ${realm_status}"
  cat /tmp/keycloak-realm-check.json || true
  exit 1
fi

create_status="$(curl -sS -o /tmp/keycloak-realm-create.json -w "%{http_code}" \
  -X POST "$create_realm_url" \
  -H "Authorization: Bearer ${access_token}" \
  -H "Content-Type: application/json" \
  --data-binary "@${realm_file}")"

if [ "$create_status" != "201" ] && [ "$create_status" != "409" ]; then
  echo "Failed to import Keycloak realm '${realm_name}': HTTP ${create_status}"
  cat /tmp/keycloak-realm-create.json || true
  exit 1
fi

echo "Imported Keycloak realm '${realm_name}'."
