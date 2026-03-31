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

refresh_azd_outputs() {
  azd env refresh --environment "$AZURE_ENV_NAME" --no-prompt >/dev/null
}

azd_values_cache=""

load_azd_values() {
  azd_values_cache="$(azd env get-values --environment "$AZURE_ENV_NAME" 2>/dev/null || true)"
}

read_azd_value() {
  local key="$1"
  local value
  if [ -z "$azd_values_cache" ]; then
    load_azd_values
  fi

  value="$(printf '%s\n' "$azd_values_cache" | sed -n -E "s/^(export[[:space:]]+)?${key}=(.*)$/\\2/p" | head -n1)"
  if [ -z "$value" ]; then
    return 0
  fi

  if [ "${#value}" -ge 2 ] && [ "${value#\"}" != "$value" ] && [ "${value%\"}" != "$value" ]; then
    value="${value#\"}"
    value="${value%\"}"
  fi

  if [ -n "$value" ]; then
    printf '%s' "$value"
  fi
}

read_azd_url() {
  local key="$1"
  local value
  value="$(read_azd_value "$key")"
  if [[ "$value" == http://* || "$value" == https://* ]]; then
    printf '%s' "$value"
  fi
}

resolve_resource_group() {
  local resource_group
  for key in AZURE_RESOURCE_GROUP RESOURCE_GROUP AZURE_RESOURCE_GROUP_NAME; do
    resource_group="$(read_azd_value "$key")"
    if [ -n "$resource_group" ]; then
      printf '%s' "$resource_group"
      return 0
    fi
  done

  if [ -n "${AZURE_RESOURCE_GROUP:-}" ]; then
    printf '%s' "$AZURE_RESOURCE_GROUP"
    return 0
  fi

  resource_group="$(az resource list \
    --name talentserver \
    --resource-type "Microsoft.App/containerApps" \
    --query '[0].resourceGroup' \
    -o tsv 2>/dev/null || true)"
  if [ -n "$resource_group" ]; then
    printf '%s' "$resource_group"
    return 0
  fi

  resource_group="$(az resource list \
    --name keycloak \
    --resource-type "Microsoft.App/containerApps" \
    --query '[0].resourceGroup' \
    -o tsv 2>/dev/null || true)"
  if [ -n "$resource_group" ]; then
    printf '%s' "$resource_group"
    return 0
  fi

  resource_group="$(az storage account list \
    --query "[?tags.\"aspire-resource-name\"=='storage'][0].resourceGroup" \
    -o tsv 2>/dev/null || true)"
  if [ -n "$resource_group" ]; then
    printf '%s' "$resource_group"
    return 0
  fi

  printf 'rg-%s' "$AZURE_ENV_NAME"
}

extract_account_name_from_endpoint() {
  local endpoint="$1"
  if [ -z "$endpoint" ]; then
    return 0
  fi

  printf '%s' "$endpoint" | sed -E 's#^https?://([^.]+)\..*$#\1#'
}

storage_account_exists() {
  local resource_group="$1"
  local account_name="$2"

  [ -n "$account_name" ] || return 1

  az storage account show \
    --resource-group "$resource_group" \
    --name "$account_name" \
    --query 'name' \
    -o tsv >/dev/null 2>&1
}

resolve_storage_account_from_azure() {
  local resource_group="$1"

  az storage account list \
    --resource-group "$resource_group" \
    --query "[?tags.\"aspire-resource-name\"=='storage'][0].name" \
    -o tsv 2>/dev/null || true
}

resolve_static_website_storage_account_from_azure() {
  local resource_group="$1"

  az storage account list \
    --resource-group "$resource_group" \
    --query "[?primaryEndpoints.web != null][0].name" \
    -o tsv 2>/dev/null || true
}

resolve_storage_account_from_azure_name_hint() {
  local resource_group="$1"

  az storage account list \
    --resource-group "$resource_group" \
    --query "[?contains(name, 'storage')][0].name" \
    -o tsv 2>/dev/null || true
}

resolve_frontend_storage_account_by_excluding_bidstorage() {
  local resource_group="$1"
  local bidstorage_account=""
  local account_names=""
  local candidate_count=""
  local candidate=""

  bidstorage_account="$(az resource list \
    --resource-group "$resource_group" \
    --resource-type "Microsoft.Storage/storageAccounts/blobServices/containers" \
    --query "[?name=='default/bidstorage'].split(id, '/')[8] | [0]" \
    -o tsv 2>/dev/null || true)"

  account_names="$(az storage account list \
    --resource-group "$resource_group" \
    --query "[].name" \
    -o tsv 2>/dev/null || true)"

  candidate_count="$(printf '%s\n' "$account_names" | sed '/^$/d' | wc -l | tr -d ' ')"
  if [ "$candidate_count" = "1" ]; then
    printf '%s' "$account_names"
    return 0
  fi

  # In Azure mode the bid content account is created as "bidcontentstorage..."
  # while the static frontend account is the other storage account in the RG.
  candidate="$(printf '%s\n' "$account_names" | awk 'tolower($0) !~ /^bidcontentstorage/ { print; exit }')"
  if [ -n "$candidate" ]; then
    printf '%s' "$candidate"
    return 0
  fi

  if [ -n "$bidstorage_account" ]; then
    printf '%s\n' "$account_names" | awk -v excluded="$bidstorage_account" '$0 != excluded { print; exit }'
    return 0
  fi

  return 0
}

resource_group="$(resolve_resource_group)"

blob_endpoint="$(read_azd_url STORAGE_BLOBENDPOINT)"
if [ -z "$blob_endpoint" ]; then
  refresh_azd_outputs
  load_azd_values
  resource_group="$(resolve_resource_group)"
  blob_endpoint="$(read_azd_url STORAGE_BLOBENDPOINT)"
fi

storage_account=""
if [ -n "$blob_endpoint" ]; then
  storage_account="$(extract_account_name_from_endpoint "$blob_endpoint")"
  if ! storage_account_exists "$resource_group" "$storage_account"; then
    storage_account=""
  fi
fi

if [ -z "$storage_account" ]; then
  storage_web_endpoint="$(read_azd_url STORAGE_WEB_ENDPOINT)"
  storage_account="$(extract_account_name_from_endpoint "$storage_web_endpoint")"
  if ! storage_account_exists "$resource_group" "$storage_account"; then
    storage_account=""
  fi
fi

if [ -z "$storage_account" ]; then
  storage_account="$(resolve_storage_account_from_azure "$resource_group")"
fi

if [ -z "$storage_account" ]; then
  storage_account="$(resolve_static_website_storage_account_from_azure "$resource_group")"
fi

if [ -z "$storage_account" ]; then
  storage_account="$(resolve_storage_account_from_azure_name_hint "$resource_group")"
fi

if [ -z "$storage_account" ]; then
  storage_account="$(resolve_frontend_storage_account_by_excluding_bidstorage "$resource_group")"
fi

test -n "$storage_account" || (echo "Could not resolve frontend storage account from azd outputs or Azure resources in $resource_group" && exit 1)

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

authentication_enabled="${AuthenticationEnabled:-$(find_env_value AUTHENTICATION_ENABLED AZURE_AUTHENTICATION_ENABLED || true)}"
keycloak_client_id="${KeycloakClientId:-$(find_env_value KEYCLOAK_CLIENT_ID || true)}"
frontend_public_origin="${FrontendPublicOrigin:-$(find_env_value FRONTEND_PUBLIC_ORIGIN || true)}"
keycloak_public_origin="${KeycloakPublicOrigin:-$(find_env_value KEYCLOAK_PUBLIC_ORIGIN KEYCLOAK_BASE_URL || true)}"

keycloak_fqdn="$(az containerapp show \
  --resource-group "$resource_group" \
  --name "keycloak" \
  --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || true)"
talentserver_fqdn="$(az containerapp show \
  --resource-group "$resource_group" \
  --name "talentserver" \
  --query "properties.configuration.ingress.fqdn" -o tsv 2>/dev/null || true)"

keycloak_base_url=""
talentserver_base_url=""
[ -n "$keycloak_fqdn" ] && keycloak_base_url="https://${keycloak_fqdn}"
[ -n "$talentserver_fqdn" ] && talentserver_base_url="https://${talentserver_fqdn}"

if [ -n "$frontend_public_origin" ]; then
  talentserver_base_url="${frontend_public_origin%/}/api"
fi

test -n "$keycloak_base_url" || (echo "Could not resolve Keycloak endpoint from azd environment values" && exit 1)
test -n "$talentserver_base_url" || (echo "Could not resolve talentserver endpoint from azd environment values" && exit 1)
test -n "$authentication_enabled" || (echo "Could not resolve AUTHENTICATION_ENABLED from azd environment values" && exit 1)
test -n "$keycloak_client_id" || (echo "Could not resolve KEYCLOAK_CLIENT_ID from azd environment values" && exit 1)

if [ -n "$keycloak_public_origin" ]; then
  keycloak_authority="${keycloak_public_origin%/}/realms/TalentConsulting"
else
  keycloak_authority="${keycloak_base_url%/}/realms/TalentConsulting"
fi

keycloak_admin_base_url=""
declare -a keycloak_base_candidates=()
if [ -n "$keycloak_public_origin" ]; then
  keycloak_base_candidates+=("${keycloak_public_origin%/}")
fi
keycloak_base_candidates+=("${keycloak_base_url%/}")

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

frontend_url="$(az storage account show --name "$storage_account" --query 'primaryEndpoints.web' -o tsv)"
test -n "$frontend_url" || (echo "Could not resolve frontend static website endpoint for $storage_account" && exit 1)
frontend_url="${frontend_url%/}"
access_token=""
for candidate in "${keycloak_base_candidates[@]}"; do
  [ -n "$candidate" ] || continue

  token_url="${candidate}/realms/master/protocol/openid-connect/token"
  token_response_file="$(mktemp)"
  token_status="$(curl -sS \
    -o "$token_response_file" \
    -w "%{http_code}" \
    -X POST "$token_url" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    --data-urlencode "client_id=admin-cli" \
    --data-urlencode "username=admin" \
    --data-urlencode "password=${KeycloakPassword}" \
    --data-urlencode "grant_type=password")"

  if [ "$token_status" = "200" ]; then
    access_token="$(jq -r '.access_token // empty' "$token_response_file")"
    if [ -n "$access_token" ]; then
      keycloak_admin_base_url="$candidate"
      rm -f "$token_response_file"
      break
    fi
  fi

  echo "Keycloak admin token request failed for ${candidate}: HTTP ${token_status}"
  cat "$token_response_file" || true
  rm -f "$token_response_file"
done

test -n "$access_token" || (echo "Failed to obtain Keycloak admin token during frontend deploy" && exit 1)
test -n "$keycloak_admin_base_url" || (echo "Failed to determine Keycloak admin base URL during frontend deploy" && exit 1)

keycloak_client_query_url="${keycloak_admin_base_url}/admin/realms/TalentConsulting/clients?clientId=${keycloak_client_id}"
client_response_file="$(mktemp)"
client_status="$(curl -sS \
  -o "$client_response_file" \
  -w "%{http_code}" \
  -H "Authorization: Bearer ${access_token}" \
  "$keycloak_client_query_url")"

[ "$client_status" = "200" ] || (echo "Failed to query Keycloak client ${keycloak_client_id}: HTTP ${client_status}" && cat "$client_response_file" && exit 1)
client_payload="$(cat "$client_response_file")"
rm -f "$client_response_file"

client_id_internal="$(printf '%s' "$client_payload" | jq -r '.[0].id // empty')"
test -n "$client_id_internal" || (echo "Could not resolve Keycloak client ${keycloak_client_id}" && exit 1)

client_update_url="${keycloak_admin_base_url}/admin/realms/TalentConsulting/clients/${client_id_internal}"
client_update_payload_file="$(mktemp)"
printf '%s' "$client_payload" \
  | jq \
      --arg frontendUrl "$frontend_url" \
      --arg publicFrontendUrl "${frontend_public_origin%/}" \
      '.[0]
       | .redirectUris = (((.redirectUris // []) + [
            ($frontendUrl + "/*"),
            ($frontendUrl + "/authentication/login-callback"),
            ($frontendUrl + "/authentication/logout-callback"),
            ($frontendUrl + "/authentication/logged-out")
         ] + (if $publicFrontendUrl != "" then [
            ($publicFrontendUrl + "/*"),
            ($publicFrontendUrl + "/authentication/login-callback"),
            ($publicFrontendUrl + "/authentication/logout-callback"),
            ($publicFrontendUrl + "/authentication/logged-out")
         ] else [] end)) | unique)
       | .webOrigins = (((.webOrigins // []) + [
            $frontendUrl
         ] + (if $publicFrontendUrl != "" then [
            $publicFrontendUrl
         ] else [] end)) | unique)' > "$client_update_payload_file"

update_response_file="$(mktemp)"
update_status="$(curl -sS \
  -o "$update_response_file" \
  -w "%{http_code}" \
  -X PUT "$client_update_url" \
  -H "Authorization: Bearer ${access_token}" \
  -H "Content-Type: application/json" \
  --data-binary "@${client_update_payload_file}")"

if [ "$update_status" != "204" ] && [ "$update_status" != "200" ]; then
  echo "Failed to update Keycloak client ${keycloak_client_id}: HTTP ${update_status}"
  cat "$update_response_file" || true
  rm -f "$client_update_payload_file" "$update_response_file"
  exit 1
fi

rm -f "$client_update_payload_file" "$update_response_file"

echo "Frontend URL: ${frontend_url}/"
