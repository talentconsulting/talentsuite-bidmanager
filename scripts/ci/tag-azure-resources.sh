#!/usr/bin/env bash
set -euo pipefail

trap 'echo "tag-azure-resources.sh failed at line $LINENO"' ERR

require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    echo "$name is required"
    exit 1
  fi
}

require_env "AZURE_ENV_NAME"

resource_group="rg-${AZURE_ENV_NAME}"
project_tag="TalentSuite"
owner_tag="rgparkins"

echo "Applying tags to resource group: $resource_group"
az group update \
  --name "$resource_group" \
  --set "tags.project=$project_tag" "tags.owner=$owner_tag" \
  >/dev/null

resource_ids="$(az resource list \
  --resource-group "$resource_group" \
  --query "[?starts_with(id, '/subscriptions/') && !contains(type, 'operationResults')].id" \
  -o tsv)"
if [ -z "$resource_ids" ]; then
  echo "No resources found in $resource_group"
  exit 0
fi

echo "Applying tags to resources in $resource_group"
while IFS= read -r resource_id; do
  [ -n "$resource_id" ] || continue
  if ! az tag update \
    --resource-id "$resource_id" \
    --operation Merge \
    --tags project="$project_tag" owner="$owner_tag" \
    >/dev/null 2>&1; then
    echo "Warning: could not tag resource '$resource_id'; continuing."
  fi
done <<< "$resource_ids"

echo "Tagging complete."
