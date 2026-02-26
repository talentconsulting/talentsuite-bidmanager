#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "sync-containerapp-secrets-keyvault.sh failed at line $LINENO"' ERR

require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "Missing required environment variable: $name"
    exit 1
  fi
}

require_env "AZURE_ENV_NAME"
require_env "AZURE_LOCATION"

resource_group="rg-${AZURE_ENV_NAME}"
keycloak_password="${KeycloakPassword:-${Parameters__KeycloakPassword:-}}"
keycloak_db_password="${KeycloakDbPassword:-${Parameters__KeycloakDbPassword:-}}"

test -n "$keycloak_password" || (echo "Missing KeycloakPassword/Parameters__KeycloakPassword" && exit 1)
test -n "$keycloak_db_password" || (echo "Missing KeycloakDbPassword/Parameters__KeycloakDbPassword" && exit 1)

echo "::add-mask::$keycloak_password"
echo "::add-mask::$keycloak_db_password"

key_vault_name="$(az keyvault list --resource-group "$resource_group" --query '[0].name' -o tsv 2>/dev/null || true)"

if [ -z "$key_vault_name" ]; then
  rg_hash="$(printf '%s' "$resource_group" | sha256sum | cut -c1-8)"
  key_vault_name="$(printf 'kv%s%s' "$AZURE_ENV_NAME" "$rg_hash" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9' | cut -c1-24)"
  az keyvault create \
    --name "$key_vault_name" \
    --resource-group "$resource_group" \
    --location "$AZURE_LOCATION" \
    --enable-rbac-authorization true >/dev/null
fi

key_vault_id="$(az keyvault show --name "$key_vault_name" --resource-group "$resource_group" --query id -o tsv)"
key_vault_uri="$(az keyvault show --name "$key_vault_name" --resource-group "$resource_group" --query properties.vaultUri -o tsv)"

az keyvault secret set --vault-name "$key_vault_name" --name "keycloak-admin-password" --value "$keycloak_password" >/dev/null
az keyvault secret set --vault-name "$key_vault_name" --name "keycloak-db-password" --value "$keycloak_db_password" >/dev/null

mapfile -t app_names < <(az containerapp list --resource-group "$resource_group" --query '[].name' -o tsv)

if [ "${#app_names[@]}" -eq 0 ]; then
  echo "No Container Apps found in $resource_group; skipping Key Vault secret sync."
  exit 0
fi

for app_name in "${app_names[@]}"; do
  mapfile -t secret_refs < <(
    az containerapp show --name "$app_name" --resource-group "$resource_group" -o json \
      | jq -r '.properties.template.containers[]?.env[]? | select(.secretRef != null) | .secretRef' \
      | sort -u
  )

  if [ "${#secret_refs[@]}" -eq 0 ]; then
    continue
  fi

  az containerapp identity assign --name "$app_name" --resource-group "$resource_group" --system-assigned >/dev/null
  principal_id="$(az containerapp show --name "$app_name" --resource-group "$resource_group" --query identity.principalId -o tsv)"
  test -n "$principal_id" || (echo "Failed to resolve managed identity principalId for $app_name" && exit 1)

  role_assignment_id="$(az role assignment list \
    --assignee-object-id "$principal_id" \
    --assignee-principal-type ServicePrincipal \
    --scope "$key_vault_id" \
    --role "Key Vault Secrets User" \
    --query '[0].id' -o tsv)"

  if [ -z "$role_assignment_id" ]; then
    az role assignment create \
      --assignee-object-id "$principal_id" \
      --assignee-principal-type ServicePrincipal \
      --scope "$key_vault_id" \
      --role "Key Vault Secrets User" >/dev/null
  fi

  secret_updates=()
  for secret_ref in "${secret_refs[@]}"; do
    case "$secret_ref" in
      kc-db-password)
        secret_updates+=("${secret_ref}=keyvaultref:${key_vault_uri}secrets/keycloak-db-password,identityref:system")
        ;;
      kc-bootstrap-*-password|keycloak-*-password)
        secret_updates+=("${secret_ref}=keyvaultref:${key_vault_uri}secrets/keycloak-admin-password,identityref:system")
        ;;
    esac
  done

  if [ "${#secret_updates[@]}" -gt 0 ]; then
    az containerapp secret set \
      --name "$app_name" \
      --resource-group "$resource_group" \
      --secrets "${secret_updates[@]}" >/dev/null
    echo "Patched Key Vault secret refs for $app_name"
  fi
done

echo "Container App secret refs are backed by Key Vault ($key_vault_name)."
