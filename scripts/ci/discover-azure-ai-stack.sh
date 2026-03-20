#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "discover-azure-ai-stack.sh failed at line $LINENO"' ERR

usage() {
  cat <<'EOF'
Usage:
  scripts/ci/discover-azure-ai-stack.sh \
    --subscription <subscription-id-or-name> \
    --resource-group <resource-group> \
    [--openai-account <name>] \
    [--search-service <name>] \
    [--foundry-hub <name>] \
    [--foundry-project <name>] \
    [--agent-id <id>] \
    [--output <path>]

Purpose:
  Inventories Azure OpenAI, Azure AI Search, and Azure AI Foundry resources from
  an existing subscription/resource group and writes a compact JSON manifest that
  can be used as input to provision-azure-ai-stack.sh.

Notes:
  - Requires Azure CLI login.
  - If multiple matching resources exist and no explicit name is passed, the
    script selects the first match returned by Azure CLI.
  - The Foundry project endpoint is derived from hub/project names because the
    app uses the project endpoint shape:
      https://<hub-name>.services.ai.azure.com/api/projects/<project-name>
EOF
}

require_value() {
  local name="$1"
  local value="$2"
  if [ -z "$value" ]; then
    echo "Missing required argument: $name"
    usage
    exit 1
  fi
}

command -v az >/dev/null || (echo "Azure CLI is required." && exit 1)
command -v jq >/dev/null || (echo "jq is required." && exit 1)

subscription=""
resource_group=""
openai_account_name=""
search_service_name=""
foundry_hub_name=""
foundry_project_name=""
agent_id=""
output_path=""

while [ $# -gt 0 ]; do
  case "$1" in
    --subscription)
      subscription="${2:-}"
      shift 2
      ;;
    --resource-group)
      resource_group="${2:-}"
      shift 2
      ;;
    --openai-account)
      openai_account_name="${2:-}"
      shift 2
      ;;
    --search-service)
      search_service_name="${2:-}"
      shift 2
      ;;
    --foundry-hub)
      foundry_hub_name="${2:-}"
      shift 2
      ;;
    --foundry-project)
      foundry_project_name="${2:-}"
      shift 2
      ;;
    --agent-id)
      agent_id="${2:-}"
      shift 2
      ;;
    --output)
      output_path="${2:-}"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1"
      usage
      exit 1
      ;;
  esac
done

require_value "--subscription" "$subscription"
require_value "--resource-group" "$resource_group"

az account set --subscription "$subscription"

resolve_name() {
  local explicit_name="$1"
  local query_cmd="$2"

  if [ -n "$explicit_name" ]; then
    printf '%s' "$explicit_name"
    return 0
  fi

  eval "$query_cmd"
}

openai_account_name="$(resolve_name \
  "$openai_account_name" \
  "az cognitiveservices account list --resource-group '$resource_group' --query \"[?kind=='OpenAI'][0].name\" -o tsv")"

search_service_name="$(resolve_name \
  "$search_service_name" \
  "az search service list --resource-group '$resource_group' --query '[0].name' -o tsv")"

if [ -z "$foundry_hub_name" ] || [ -z "$foundry_project_name" ]; then
  ml_workspaces_json="$(az resource list \
    --resource-group "$resource_group" \
    --resource-type Microsoft.MachineLearningServices/workspaces \
    -o json)"

  if [ -z "$foundry_hub_name" ]; then
    foundry_hub_name="$(printf '%s' "$ml_workspaces_json" | jq -r '
      map(select((.kind // "" | ascii_downcase) == "hub")) | .[0].name // empty
    ')"
  fi

  if [ -z "$foundry_project_name" ]; then
    foundry_project_name="$(printf '%s' "$ml_workspaces_json" | jq -r '
      map(select((.kind // "" | ascii_downcase) == "project")) | .[0].name // empty
    ')"
  fi
fi

openai_json="{}"
openai_deployments_json="[]"
openai_primary_key=""
if [ -n "$openai_account_name" ]; then
  openai_json="$(az cognitiveservices account show \
    --name "$openai_account_name" \
    --resource-group "$resource_group" \
    -o json)"

  openai_deployments_json="$(az cognitiveservices account deployment list \
    --name "$openai_account_name" \
    --resource-group "$resource_group" \
    -o json 2>/dev/null || printf '[]')"

  openai_primary_key="$(az cognitiveservices account keys list \
    --name "$openai_account_name" \
    --resource-group "$resource_group" \
    --query key1 -o tsv 2>/dev/null || true)"
fi

search_json="{}"
search_primary_key=""
if [ -n "$search_service_name" ]; then
  search_json="$(az search service show \
    --name "$search_service_name" \
    --resource-group "$resource_group" \
    -o json)"

  search_primary_key="$(az search admin-key show \
    --service-name "$search_service_name" \
    --resource-group "$resource_group" \
    --query primaryKey -o tsv 2>/dev/null || true)"
fi

foundry_resources_json="$(az resource list \
  --resource-group "$resource_group" \
  --resource-type Microsoft.MachineLearningServices/workspaces \
  -o json)"

foundry_hub_json="$(printf '%s' "$foundry_resources_json" | jq -c --arg name "$foundry_hub_name" '
  map(select(.name == $name)) | .[0] // {}
')"

foundry_project_json="$(printf '%s' "$foundry_resources_json" | jq -c --arg name "$foundry_project_name" '
  map(select(.name == $name)) | .[0] // {}
')"

foundry_project_endpoint=""
if [ -n "$foundry_hub_name" ] && [ -n "$foundry_project_name" ]; then
  foundry_project_endpoint="https://${foundry_hub_name}.services.ai.azure.com/api/projects/${foundry_project_name}"
fi

manifest_json="$(jq -n \
  --arg subscription "$subscription" \
  --arg resourceGroup "$resource_group" \
  --arg agentId "$agent_id" \
  --arg foundryProjectEndpoint "$foundry_project_endpoint" \
  --arg openaiPrimaryKey "$openai_primary_key" \
  --arg searchPrimaryKey "$search_primary_key" \
  --argjson openai "$openai_json" \
  --argjson openaiDeployments "$openai_deployments_json" \
  --argjson search "$search_json" \
  --argjson foundryHub "$foundry_hub_json" \
  --argjson foundryProject "$foundry_project_json" \
  '{
    source: {
      subscription: $subscription,
      resourceGroup: $resourceGroup
    },
    openai: {
      name: ($openai.name // ""),
      location: ($openai.location // ""),
      sku: ($openai.sku.name // ""),
      endpoint: ($openai.properties.endpoint // ""),
      primaryKey: $openaiPrimaryKey,
      deployments: ($openaiDeployments | map({
        name: (.name // ""),
        model: (.properties.model.name // ""),
        modelVersion: (.properties.model.version // ""),
        modelFormat: (.properties.model.format // ""),
        skuName: (.sku.name // ""),
        skuCapacity: (.sku.capacity // 0)
      }))
    },
    search: {
      name: ($search.name // ""),
      location: ($search.location // ""),
      sku: ($search.sku.name // ""),
      endpoint: (if ($search.name // "") != "" then ("https://" + $search.name + ".search.windows.net") else "" end),
      primaryKey: $searchPrimaryKey,
      replicaCount: ($search.replicaCount // 0),
      partitionCount: ($search.partitionCount // 0)
    },
    foundry: {
      hubName: ($foundryHub.name // ""),
      hubResourceId: ($foundryHub.id // ""),
      projectName: ($foundryProject.name // ""),
      projectResourceId: ($foundryProject.id // ""),
      projectEndpoint: $foundryProjectEndpoint,
      agentId: $agentId
    }
  }')"

if [ -n "$output_path" ]; then
  printf '%s\n' "$manifest_json" > "$output_path"
  echo "Wrote manifest to $output_path"
else
  printf '%s\n' "$manifest_json"
fi
