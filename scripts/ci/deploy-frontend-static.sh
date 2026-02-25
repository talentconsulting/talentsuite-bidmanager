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

find_env_value() {
  local key value
  for key in "$@"; do
    value="$(echo "$azd_values" | sed -n "s/^${key}=//p" | head -n1)"
    if [ -n "$value" ]; then
      echo "$value"
      return 0
    fi
  done
  return 1
}

keycloak_base_url="$(find_env_value KEYCLOAK_HTTPS KEYCLOAK_HTTP KEYCLOAK_ENDPOINT SERVICE_KEYCLOAK_ENDPOINT || true)"
talentserver_base_url="$(find_env_value TALENTSERVER_HTTPS TALENTSERVER_HTTP TALENTSERVER_ENDPOINT SERVICE_TALENTSERVER_ENDPOINT || true)"
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

echo "Frontend URL: https://${storage_account}.z33.web.core.windows.net/"
