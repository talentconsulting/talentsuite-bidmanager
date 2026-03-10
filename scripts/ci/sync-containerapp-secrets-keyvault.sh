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

ensure_role_assignment() {
  local assignee_object_id="$1"
  local principal_type="$2"
  local scope="$3"
  local role_name="$4"

  local assignment_id
  assignment_id="$(az role assignment list \
    --assignee-object-id "$assignee_object_id" \
    --scope "$scope" \
    --role "$role_name" \
    --query '[0].id' -o tsv 2>/dev/null || true)"

  if [ -n "$assignment_id" ]; then
    return 0
  fi

  if ! az role assignment create \
    --assignee-object-id "$assignee_object_id" \
    --assignee-principal-type "$principal_type" \
    --scope "$scope" \
    --role "$role_name" >/dev/null 2>&1; then
    # Older Azure CLI versions may not support --assignee-principal-type here.
    if ! az role assignment create \
      --assignee-object-id "$assignee_object_id" \
      --scope "$scope" \
      --role "$role_name" >/dev/null 2>&1; then
      return 1
    fi
  fi

  # Azure RBAC propagation is eventually consistent.
  sleep 10
  return 0
}

require_env "AZURE_ENV_NAME"
require_env "AZURE_LOCATION"

resource_group="rg-${AZURE_ENV_NAME}"
keycloak_password="${KeycloakPassword:-}"
keycloak_db_password="${KeycloakDbPassword:-}"

test -n "$keycloak_password" || (echo "Missing KeycloakPassword" && exit 1)
test -n "$keycloak_db_password" || (echo "Missing KeycloakDbPassword" && exit 1)

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

# Ensure the current deployment principal has data-plane permission to write Key Vault secrets.
deployer_principal_id="${AZD_PRINCIPAL_ID:-}"
if [ -z "$deployer_principal_id" ]; then
  arm_token="$(az account get-access-token --resource-type arm --query accessToken -o tsv)"
  payload="$(printf '%s' "$arm_token" | cut -d'.' -f2 | tr '_-' '/+')"
  padding=$(( (4 - ${#payload} % 4) % 4 ))
  payload="${payload}$(printf '=%.0s' $(seq 1 "$padding"))"
  deployer_principal_id="$(printf '%s' "$payload" | base64 --decode --ignore-garbage 2>/dev/null | jq -r '.oid // empty')"
fi
test -n "$deployer_principal_id" || (echo "Could not resolve deployer principal id for Key Vault role assignment" && exit 1)

if ! ensure_role_assignment "$deployer_principal_id" "ServicePrincipal" "$key_vault_id" "Key Vault Secrets Officer"; then
  echo "Unable to assign 'Key Vault Secrets Officer' on $key_vault_name to deployer principal $deployer_principal_id."
  echo "Grant this once using an Owner/User Access Administrator identity, then re-run:"
  echo "az role assignment create --assignee-object-id $deployer_principal_id --assignee-principal-type ServicePrincipal --scope $key_vault_id --role \"Key Vault Secrets Officer\""
  exit 1
fi

set_secret_with_retry() {
  local name="$1"
  local value="$2"
  local attempts=10
  local delay=10
  local i

  for i in $(seq 1 "$attempts"); do
    if az keyvault secret set --vault-name "$key_vault_name" --name "$name" --value "$value" >/dev/null 2>&1; then
      return 0
    fi
    if [ "$i" -lt "$attempts" ]; then
      sleep "$delay"
    fi
  done

  echo "Failed to set Key Vault secret '$name' after RBAC propagation retries."
  az keyvault secret set --vault-name "$key_vault_name" --name "$name" --value "$value"
}

set_secret_with_retry "keycloak-admin-password" "$keycloak_password"
set_secret_with_retry "keycloak-db-password" "$keycloak_db_password"

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

  if ! ensure_role_assignment "$principal_id" "ServicePrincipal" "$key_vault_id" "Key Vault Secrets User"; then
    echo "Unable to assign 'Key Vault Secrets User' on $key_vault_name to container app $app_name principal $principal_id."
    echo "Grant this once using an Owner/User Access Administrator identity, then re-run:"
    echo "az role assignment create --assignee-object-id $principal_id --assignee-principal-type ServicePrincipal --scope $key_vault_id --role \"Key Vault Secrets User\""
    exit 1
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
