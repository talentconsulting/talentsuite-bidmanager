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
require_env "DEPLOYER_CLIENT_ID"
require_env "KeycloakPassword"

blob_endpoint="$(azd env get-value STORAGE_BLOBENDPOINT --environment "$AZURE_ENV_NAME")"
storage_account="$(echo "$blob_endpoint" | sed -E 's#^https?://([^.]+)\..*$#\1#')"
test -n "$storage_account" || (echo "Could not resolve storage account from STORAGE_BLOBENDPOINT" && exit 1)

storage_account_id="$(az storage account show --name "$storage_account" --query id -o tsv)"
deployer_object_id="$(az ad sp show --id "$DEPLOYER_CLIENT_ID" --query id -o tsv)"
test -n "$storage_account_id" || (echo "Could not resolve storage account id" && exit 1)
test -n "$deployer_object_id" || (echo "Could not resolve deployer service principal object id" && exit 1)

existing_role_assignment="$(az role assignment list \
  --assignee-object-id "$deployer_object_id" \
  --scope "$storage_account_id" \
  --role "Storage Blob Data Contributor" \
  --query '[0].id' -o tsv)"

if [ -z "$existing_role_assignment" ]; then
  az role assignment create \
    --assignee-object-id "$deployer_object_id" \
    --assignee-principal-type ServicePrincipal \
    --scope "$storage_account_id" \
    --role "Storage Blob Data Contributor"
  # RBAC assignment can take a short time to become effective for data plane operations.
  sleep 30
fi

dotnet publish src/TalentSuite.FrontEnd/TalentSuite.FrontEnd.csproj -c Release -o /tmp/talentsuite-frontend-publish

azd_values="$(azd env get-values --environment "$AZURE_ENV_NAME")"

normalize_env_value() {
  local value="$1"
  if [ "${#value}" -ge 2 ] && [ "${value#\"}" != "$value" ] && [ "${value%\"}" != "$value" ]; then
    value="${value#\"}"
    value="${value%\"}"
  fi
  printf '%s' "$value"
}

find_env_value() {
  local key value
  for key in "$@"; do
    value="$(echo "$azd_values" | sed -n "s/^${key}=//p" | head -n1)"
    if [ -n "$value" ]; then
      normalize_env_value "$value"
      return 0
    fi
  done
  return 1
}

keycloak_base_url="$(find_env_value KEYCLOAK_HTTPS KEYCLOAK_HTTP KEYCLOAK_ENDPOINT || true)"
talentserver_base_url="$(find_env_value TALENTSERVER_HTTPS TALENTSERVER_HTTP TALENTSERVER_ENDPOINT || true)"
authentication_enabled="$(find_env_value AUTHENTICATION_ENABLED AZURE_AUTHENTICATION_ENABLED || true)"
keycloak_client_id="$(find_env_value KEYCLOAK_CLIENT_ID || true)"

# Fallback: resolve endpoints directly from Container Apps ingress FQDN.
if [ -z "$keycloak_base_url" ]; then
  keycloak_fqdn="$(az containerapp show \
    --resource-group "rg-${AZURE_ENV_NAME}" \
    --name "keycloak" \
    --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || true)"
  [ -n "$keycloak_fqdn" ] && keycloak_base_url="https://${keycloak_fqdn}"
fi

if [ -z "$talentserver_base_url" ]; then
  talentserver_fqdn="$(az containerapp show \
    --resource-group "rg-${AZURE_ENV_NAME}" \
    --name "talentserver" \
    --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || true)"
  [ -n "$talentserver_fqdn" ] && talentserver_base_url="https://${talentserver_fqdn}"
fi

test -n "$keycloak_base_url" || (echo "Could not resolve Keycloak endpoint from azd environment values" && exit 1)
test -n "$talentserver_base_url" || (echo "Could not resolve talentserver endpoint from azd environment values" && exit 1)
test -n "$authentication_enabled" || (echo "Could not resolve AUTHENTICATION_ENABLED from azd environment values" && exit 1)
test -n "$keycloak_client_id" || (echo "Could not resolve KEYCLOAK_CLIENT_ID from azd environment values" && exit 1)

keycloak_authority="${keycloak_base_url%/}/realms/TalentConsulting"

jq -n \
  --arg auth "$authentication_enabled" \
  --arg authority "$keycloak_authority" \
  --arg clientId "$keycloak_client_id" \
  --arg apiBase "${talentserver_base_url%/}" \
  --arg strict "true" \
  '{
    AUTHENTICATION_ENABLED: $auth,
    KEYCLOAK_AUTHORITY: $authority,
    KEYCLOAK_CLIENT_ID: $clientId,
    TALENTSERVER_HTTPS: $apiBase,
    STRICT_CONFIGURATION: $strict
  }' > /tmp/talentsuite-frontend-publish/wwwroot/appsettings.json

az storage blob service-properties update \
  --account-name "$storage_account" \
  --auth-mode login \
  --static-website \
  --index-document index.html \
  --404-document index.html

az storage blob upload-batch \
  --account-name "$storage_account" \
  --auth-mode login \
  --destination '$web' \
  --source /tmp/talentsuite-frontend-publish/wwwroot \
  --overwrite

# Ensure browsers always fetch the latest runtime config and entrypoint.
az storage blob update \
  --account-name "$storage_account" \
  --auth-mode login \
  --container-name '$web' \
  --name appsettings.json \
  --content-cache-control "no-cache, no-store, must-revalidate" >/dev/null

az storage blob update \
  --account-name "$storage_account" \
  --auth-mode login \
  --container-name '$web' \
  --name index.html \
  --content-cache-control "no-cache, no-store, must-revalidate" >/dev/null

frontend_url="https://${storage_account}.z33.web.core.windows.net"
keycloak_token_url="${keycloak_base_url%/}/realms/master/protocol/openid-connect/token"
keycloak_client_query_url="${keycloak_base_url%/}/admin/realms/TalentConsulting/clients?clientId=${keycloak_client_id}"

access_token="$(curl -fsS \
  -X POST "$keycloak_token_url" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "client_id=admin-cli" \
  --data-urlencode "username=admin" \
  --data-urlencode "password=${KeycloakPassword}" \
  --data-urlencode "grant_type=password" \
  | jq -r '.access_token // empty')"

test -n "$access_token" || (echo "Failed to obtain Keycloak admin token during frontend deploy" && exit 1)

client_payload="$(curl -fsS \
  -H "Authorization: Bearer ${access_token}" \
  "$keycloak_client_query_url")"

client_id_internal="$(printf '%s' "$client_payload" | jq -r '.[0].id // empty')"
test -n "$client_id_internal" || (echo "Could not resolve Keycloak client ${keycloak_client_id}" && exit 1)

client_update_url="${keycloak_base_url%/}/admin/realms/TalentConsulting/clients/${client_id_internal}"
printf '%s' "$client_payload" \
  | jq \
      --arg frontendUrl "$frontend_url" \
      '.[0]
       | .redirectUris = (((.redirectUris // []) + [
            ($frontendUrl + "/*"),
            ($frontendUrl + "/authentication/login-callback"),
            ($frontendUrl + "/authentication/logout-callback"),
            ($frontendUrl + "/authentication/logged-out")
         ]) | unique)
       | .webOrigins = (((.webOrigins // []) + [
            $frontendUrl
         ]) | unique)' \
  | curl -fsS \
      -X PUT "$client_update_url" \
      -H "Authorization: Bearer ${access_token}" \
      -H "Content-Type: application/json" \
      --data-binary @-

echo "Frontend URL: ${frontend_url}/"
