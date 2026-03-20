#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "provision-azure-ai-stack.sh failed at line $LINENO"' ERR

usage() {
  cat <<'EOF'
Usage:
  scripts/ci/provision-azure-ai-stack.sh \
    --subscription <target-subscription-id-or-name> \
    --resource-group <target-resource-group> \
    --location <azure-region> \
    [--source-manifest <path>] \
    [--openai-account <name>] \
    [--openai-sku <sku>] \
    [--openai-model-deployment <deployment-name>] \
    [--openai-model-name <model-name>] \
    [--openai-model-version <version>] \
    [--openai-model-capacity <capacity>] \
    [--search-service <name>] \
    [--search-sku <sku>] \
    [--search-replicas <count>] \
    [--search-partitions <count>] \
    [--foundry-hub <name>] \
    [--foundry-project <name>] \
    [--agent-id <id>] \
    [--emit-azd-env]

Purpose:
  Creates a target Azure AI stack for this application:
  - Azure OpenAI account and one model deployment
  - Azure AI Search service
  - Azure AI Foundry hub + project when Azure ML CLI support is available

Notes:
  - Foundry project creation requires the Azure ML CLI extension. If it is not
    available, the script still creates OpenAI and Search and prints the exact
    next command to complete Foundry setup.
  - Agent cloning is not automated here. If you already know the target agent
    id, pass --agent-id so the emitted app settings are complete.
EOF
}

command -v az >/dev/null || (echo "Azure CLI is required." && exit 1)
command -v jq >/dev/null || (echo "jq is required." && exit 1)

subscription=""
resource_group=""
location=""
source_manifest=""
openai_account_name=""
openai_sku=""
openai_model_deployment=""
openai_model_name=""
openai_model_version=""
openai_model_capacity=""
search_service_name=""
search_sku=""
search_replicas=""
search_partitions=""
foundry_hub_name=""
foundry_project_name=""
agent_id=""
emit_azd_env="false"

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
    --location)
      location="${2:-}"
      shift 2
      ;;
    --source-manifest)
      source_manifest="${2:-}"
      shift 2
      ;;
    --openai-account)
      openai_account_name="${2:-}"
      shift 2
      ;;
    --openai-sku)
      openai_sku="${2:-}"
      shift 2
      ;;
    --openai-model-deployment)
      openai_model_deployment="${2:-}"
      shift 2
      ;;
    --openai-model-name)
      openai_model_name="${2:-}"
      shift 2
      ;;
    --openai-model-version)
      openai_model_version="${2:-}"
      shift 2
      ;;
    --openai-model-capacity)
      openai_model_capacity="${2:-}"
      shift 2
      ;;
    --search-service)
      search_service_name="${2:-}"
      shift 2
      ;;
    --search-sku)
      search_sku="${2:-}"
      shift 2
      ;;
    --search-replicas)
      search_replicas="${2:-}"
      shift 2
      ;;
    --search-partitions)
      search_partitions="${2:-}"
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
    --emit-azd-env)
      emit_azd_env="true"
      shift 1
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

require_value() {
  local name="$1"
  local value="$2"
  if [ -z "$value" ]; then
    echo "Missing required argument: $name"
    usage
    exit 1
  fi
}

read_manifest_value() {
  local expr="$1"
  if [ -z "$source_manifest" ]; then
    return 0
  fi
  jq -r "$expr // empty" "$source_manifest"
}

slugify() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9-'
}

require_value "--subscription" "$subscription"
require_value "--resource-group" "$resource_group"
require_value "--location" "$location"

if [ -n "$source_manifest" ] && [ ! -f "$source_manifest" ]; then
  echo "Manifest not found: $source_manifest"
  exit 1
fi

openai_account_name="${openai_account_name:-$(read_manifest_value '.openai.name')}"
openai_sku="${openai_sku:-$(read_manifest_value '.openai.sku')}"
openai_model_deployment="${openai_model_deployment:-$(read_manifest_value '.openai.deployments[0].name')}"
openai_model_name="${openai_model_name:-$(read_manifest_value '.openai.deployments[0].model')}"
openai_model_version="${openai_model_version:-$(read_manifest_value '.openai.deployments[0].modelVersion')}"
openai_model_capacity="${openai_model_capacity:-$(read_manifest_value '.openai.deployments[0].skuCapacity')}"
search_service_name="${search_service_name:-$(read_manifest_value '.search.name')}"
search_sku="${search_sku:-$(read_manifest_value '.search.sku')}"
search_replicas="${search_replicas:-$(read_manifest_value '.search.replicaCount')}"
search_partitions="${search_partitions:-$(read_manifest_value '.search.partitionCount')}"
foundry_hub_name="${foundry_hub_name:-$(read_manifest_value '.foundry.hubName')}"
foundry_project_name="${foundry_project_name:-$(read_manifest_value '.foundry.projectName')}"
agent_id="${agent_id:-$(read_manifest_value '.foundry.agentId')}"

openai_account_name="${openai_account_name:-$(slugify "${resource_group}-openai" | cut -c1-24)}"
openai_sku="${openai_sku:-S0}"
openai_model_deployment="${openai_model_deployment:-gpt-4-1}"
openai_model_name="${openai_model_name:-gpt-4.1}"
openai_model_version="${openai_model_version:-2025-04-14}"
openai_model_capacity="${openai_model_capacity:-10}"
search_service_name="${search_service_name:-$(slugify "${resource_group}-search" | cut -c1-24)}"
search_sku="${search_sku:-basic}"
search_replicas="${search_replicas:-1}"
search_partitions="${search_partitions:-1}"
foundry_hub_name="${foundry_hub_name:-$(slugify "${resource_group}-foundry" | cut -c1-24)}"
foundry_project_name="${foundry_project_name:-proj-$(slugify "$resource_group" | cut -c1-18)}"

az account set --subscription "$subscription"

az group create \
  --name "$resource_group" \
  --location "$location" >/dev/null

echo "Creating or updating Azure OpenAI account $openai_account_name in $resource_group"
if ! az cognitiveservices account show --name "$openai_account_name" --resource-group "$resource_group" >/dev/null 2>&1; then
  az cognitiveservices account create \
    --name "$openai_account_name" \
    --resource-group "$resource_group" \
    --location "$location" \
    --kind OpenAI \
    --sku "$openai_sku" \
    --custom-domain "$openai_account_name" \
    --yes >/dev/null
fi

echo "Creating or updating Azure OpenAI deployment $openai_model_deployment"
if ! az cognitiveservices account deployment show \
  --name "$openai_account_name" \
  --resource-group "$resource_group" \
  --deployment-name "$openai_model_deployment" >/dev/null 2>&1; then
  az cognitiveservices account deployment create \
    --name "$openai_account_name" \
    --resource-group "$resource_group" \
    --deployment-name "$openai_model_deployment" \
    --model-format OpenAI \
    --model-name "$openai_model_name" \
    --model-version "$openai_model_version" \
    --sku-name Standard \
    --sku-capacity "$openai_model_capacity" >/dev/null
fi

echo "Creating or updating Azure AI Search service $search_service_name"
if ! az search service show --name "$search_service_name" --resource-group "$resource_group" >/dev/null 2>&1; then
  az search service create \
    --name "$search_service_name" \
    --resource-group "$resource_group" \
    --location "$location" \
    --sku "$search_sku" \
    --replica-count "$search_replicas" \
    --partition-count "$search_partitions" >/dev/null
fi

foundry_created="false"
if az extension show --name ml >/dev/null 2>&1 || az extension add --name ml >/dev/null 2>&1; then
  if ! az ml workspace show --resource-group "$resource_group" --name "$foundry_hub_name" >/dev/null 2>&1; then
    echo "Creating Azure AI Foundry hub $foundry_hub_name"
    az ml workspace create \
      --resource-group "$resource_group" \
      --location "$location" \
      --name "$foundry_hub_name" \
      --kind hub >/dev/null
  fi

  hub_id="$(az ml workspace show --resource-group "$resource_group" --name "$foundry_hub_name" --query id -o tsv)"

  if ! az ml workspace show --resource-group "$resource_group" --name "$foundry_project_name" >/dev/null 2>&1; then
    echo "Creating Azure AI Foundry project $foundry_project_name"
    az ml workspace create \
      --resource-group "$resource_group" \
      --location "$location" \
      --name "$foundry_project_name" \
      --kind project \
      --hub-id "$hub_id" >/dev/null
  fi

  foundry_created="true"
else
  echo "Azure ML CLI extension is unavailable. Skipping Foundry hub/project creation."
fi

openai_endpoint="$(az cognitiveservices account show \
  --name "$openai_account_name" \
  --resource-group "$resource_group" \
  --query properties.endpoint -o tsv)"
openai_api_key="$(az cognitiveservices account keys list \
  --name "$openai_account_name" \
  --resource-group "$resource_group" \
  --query key1 -o tsv)"
search_endpoint="https://${search_service_name}.search.windows.net"
search_primary_key="$(az search admin-key show \
  --service-name "$search_service_name" \
  --resource-group "$resource_group" \
  --query primaryKey -o tsv 2>/dev/null || true)"
foundry_project_endpoint="https://${foundry_hub_name}.services.ai.azure.com/api/projects/${foundry_project_name}"

echo
echo "Created resources:"
echo "  Azure OpenAI account: $openai_account_name"
echo "  Azure OpenAI endpoint: $openai_endpoint"
echo "  Azure OpenAI deployment: $openai_model_deployment"
echo "  Azure AI Search service: $search_service_name"
echo "  Azure AI Search endpoint: $search_endpoint"
if [ "$foundry_created" = "true" ]; then
  echo "  Azure AI Foundry hub: $foundry_hub_name"
  echo "  Azure AI Foundry project: $foundry_project_name"
else
  echo "  Azure AI Foundry hub: not created automatically"
  echo "  Azure AI Foundry project: not created automatically"
fi

echo
echo "App configuration values:"
echo "  AzureOpenAI__Endpoint=$openai_endpoint"
echo "  AzureOpenAI__ApiKey=$openai_api_key"
echo "  AzureOpenAI__ChatDeployment=$openai_model_deployment"
echo "  AzureAIFoundry__ProjectEndpoint=$foundry_project_endpoint"
echo "  Agents__AgentId=${agent_id:-<create-agent-in-foundry>}"

echo
echo "Optional Search values:"
echo "  AZURE_AI_SEARCH_ENDPOINT=$search_endpoint"
echo "  AZURE_AI_SEARCH_ADMIN_KEY=${search_primary_key:-<not-read>}"

if [ "$emit_azd_env" = "true" ]; then
  echo
  echo "azd env commands:"
  echo "  azd env set AZURE_OPENAI_ENDPOINT \"$openai_endpoint\""
  echo "  azd env set AZURE_OPENAI_CHAT_DEPLOYMENT \"$openai_model_deployment\""
  echo "  azd env set AZURE_AI_FOUNDRY_PROJECT_ENDPOINT \"$foundry_project_endpoint\""
  if [ -n "$agent_id" ]; then
    echo "  azd env set AGENTS_AGENT_ID \"$agent_id\""
  fi
fi

if [ "$foundry_created" != "true" ]; then
  echo
  echo "Next step for Foundry:"
  echo "  Install/enable the Azure ML CLI extension, then create the hub/project:"
  echo "  az extension add --name ml"
  echo "  az ml workspace create --resource-group \"$resource_group\" --location \"$location\" --name \"$foundry_hub_name\" --kind hub"
  echo "  hub_id=\$(az ml workspace show --resource-group \"$resource_group\" --name \"$foundry_hub_name\" --query id -o tsv)"
  echo "  az ml workspace create --resource-group \"$resource_group\" --location \"$location\" --name \"$foundry_project_name\" --kind project --hub-id \"\$hub_id\""
fi
