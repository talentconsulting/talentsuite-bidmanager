#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "provision-foundry-agent-stack.sh failed at line $LINENO"' ERR

usage() {
  cat <<'EOF'
Usage:
  scripts/ci/provision-foundry-agent-stack.sh \
    --subscription <subscription-id-or-name> \
    --resource-group <resource-group> \
    --location <azure-region> \
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
    [--foundry-account <name>] \
    [--foundry-project <name>] \
    [--search-connection-name <name>] \
    [--storage-account <name>] \
    [--storage-container <name>] \
    [--search-datasource-name <name>] \
    [--search-indexer-name <name>] \
    [--search-index-name <name>] \
    [--auto-index-blob-storage] \
    [--agent-name <name>] \
    [--agent-instructions <text>] \
    [--emit-azd-env]

Purpose:
  Creates the Azure AI resources this application needs:
  - Azure OpenAI account and model deployment
  - Azure AI Search service
  - Azure AI Foundry resource
  - Azure AI Foundry project
  - Azure AI Foundry project connection to Azure AI Search
  - Azure AI Foundry agent
  - Optional Azure AI Search datasource/index/indexer over Aspire-managed blob storage

Notes:
  - The agent is created as a plain prompt agent unless --search-index-name is supplied.
  - If --search-index-name is supplied, the script also creates a Foundry project
    connection to Azure AI Search and wires that index into the agent's tool config.
  - If --auto-index-blob-storage is supplied, the script discovers the Aspire Azure
    Storage account for bid content, targets the bidlibrary container by default,
    creates Search datasource/index/indexer resources, and runs the indexer once.
  - The script uses the Azure AI agents REST API for agent creation and Azure CLI for
    the underlying Azure resources.
EOF
}

command -v az >/dev/null || (echo "Azure CLI is required." && exit 1)
command -v jq >/dev/null || (echo "jq is required." && exit 1)

require_value() {
  local name="$1"
  local value="$2"
  if [ -z "$value" ]; then
    echo "Missing required argument: $name"
    usage
    exit 1
  fi
}

slugify() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9-'
}

json_escape() {
  printf '%s' "$1" | jq -Rs .
}

retry_command() {
  local attempts="$1"
  local delay_seconds="$2"
  shift 2

  local attempt=1
  while true; do
    if "$@"; then
      return 0
    fi

    if [ "$attempt" -ge "$attempts" ]; then
      return 1
    fi

    echo "Retry $attempt/$attempts failed. Waiting ${delay_seconds}s before retry."
    sleep "$delay_seconds"
    attempt=$((attempt + 1))
  done
}

resolve_aspire_storage_account() {
  local rg="$1"
  local resolved=""

  resolved="$(az resource list \
    --resource-group "$rg" \
    --resource-type "Microsoft.Storage/storageAccounts" \
    --query "[?tags.\"aspire-resource-name\"=='bidcontentstorage'][0].name" \
    -o tsv 2>/dev/null || true)"
  if [ -n "$resolved" ]; then
    printf '%s' "$resolved"
    return 0
  fi

  resolved="$(az storage account list \
    --resource-group "$rg" \
    --query "[?starts_with(name, 'bidcontentstorage')][0].name" \
    -o tsv 2>/dev/null || true)"
  if [ -n "$resolved" ]; then
    printf '%s' "$resolved"
    return 0
  fi

  resolved="$(az resource list \
    --resource-group "$rg" \
    --resource-type "Microsoft.Storage/storageAccounts/blobServices/containers" \
    --query "[?name=='default/bidstorage'].split(id, '/')[8] | [0]" \
    -o tsv 2>/dev/null || true)"
  printf '%s' "$resolved"
}

ensure_search_managed_identity() {
  local rg="$1"
  local service_name="$2"

  az resource update \
    --resource-group "$rg" \
    --resource-type "Microsoft.Search/searchServices" \
    --name "$service_name" \
    --set identity.type=SystemAssigned >/dev/null
}

search_api() {
  local method="$1"
  local url="$2"
  local api_key="$3"
  local payload="${4:-}"

  if [ -n "$payload" ] || [ "$method" = "POST" ] || [ "$method" = "PUT" ] || [ "$method" = "PATCH" ]; then
    if [ -z "$payload" ]; then
      payload='{}'
    fi

    curl -fsS \
      -X "$method" \
      -H "Content-Type: application/json" \
      -H "api-key: $api_key" \
      "$url" \
      --data "$payload"
  else
    curl -fsS \
      -X "$method" \
      -H "api-key: $api_key" \
      "$url"
  fi
}

search_api_with_retry() {
  local method="$1"
  local url="$2"
  local api_key="$3"
  local payload="${4:-}"

  retry_command 6 20 search_api "$method" "$url" "$api_key" "$payload"
}

get_foundry_access_token() {
  local token=""

  token="$(az account get-access-token \
    --resource "https://ai.azure.com" \
    --query accessToken -o tsv 2>/dev/null || true)"
  if [ -n "$token" ]; then
    printf '%s' "$token"
    return 0
  fi

  token="$(az account get-access-token \
    --scope "https://ai.azure.com/.default" \
    --query accessToken -o tsv 2>/dev/null || true)"
  if [ -n "$token" ]; then
    printf '%s' "$token"
    return 0
  fi

  token="$(az account get-access-token \
    --resource "https://cognitiveservices.azure.com" \
    --query accessToken -o tsv 2>/dev/null || true)"
  printf '%s' "$token"
}

get_current_principal_object_id() {
  local arm_token payload padding principal_id

  arm_token="$(az account get-access-token --resource-type arm --query accessToken -o tsv 2>/dev/null || true)"
  if [ -z "$arm_token" ]; then
    return 1
  fi

  payload="$(printf '%s' "$arm_token" | cut -d'.' -f2 | tr '_-' '/+')"
  padding=$(( (4 - ${#payload} % 4) % 4 ))
  payload="${payload}$(printf '=%.0s' $(seq 1 "$padding"))"
  principal_id="$(printf '%s' "$payload" | base64 --decode --ignore-garbage 2>/dev/null | jq -r '.oid // empty')"

  if [ -n "$principal_id" ]; then
    printf '%s' "$principal_id"
    return 0
  fi

  return 1
}

ensure_role_assignment() {
  local principal_id="$1"
  local role_name="$2"
  local scope="$3"

  if ! az role assignment list \
    --assignee-object-id "$principal_id" \
    --scope "$scope" \
    --query "[?roleDefinitionName=='${role_name}'][0].id" \
    -o tsv | grep -q .; then
    az role assignment create \
      --assignee-object-id "$principal_id" \
      --assignee-principal-type ServicePrincipal \
      --role "$role_name" \
      --scope "$scope" >/dev/null
  fi
}

foundry_api() {
  local method="$1"
  local url="$2"
  local token="$3"
  local payload="${4:-}"
  local response_file
  local http_code

  response_file="$(mktemp)"

  if [ -n "$payload" ] || [ "$method" = "POST" ] || [ "$method" = "PUT" ] || [ "$method" = "PATCH" ]; then
    if [ -z "$payload" ]; then
      payload='{}'
    fi

    http_code="$(curl -sS \
      -X "$method" \
      -H "Authorization: Bearer $token" \
      -H "Content-Type: application/json" \
      -H "Accept: application/json" \
      "$url" \
      --data "$payload" \
      -o "$response_file" \
      -w '%{http_code}')"
  else
    http_code="$(curl -sS \
      -X "$method" \
      -H "Authorization: Bearer $token" \
      -H "Accept: application/json" \
      "$url" \
      -o "$response_file" \
      -w '%{http_code}')"
  fi

  if [ "$http_code" -lt 200 ] || [ "$http_code" -ge 300 ]; then
    echo "HTTP $http_code"
    cat "$response_file"
    rm -f "$response_file"
    return 1
  fi

  cat "$response_file"
  rm -f "$response_file"
}

foundry_api_with_retry() {
  local method="$1"
  local url="$2"
  local token="$3"
  local payload="${4:-}"

  retry_command 8 30 foundry_api "$method" "$url" "$token" "$payload"
}

subscription=""
resource_group=""
location=""
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
foundry_account_name=""
foundry_project_name=""
search_connection_name=""
storage_account_name=""
storage_container_name=""
search_datasource_name=""
search_indexer_name=""
search_index_name=""
auto_index_blob_storage="false"
agent_name=""
agent_instructions=""
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
    --foundry-account)
      foundry_account_name="${2:-}"
      shift 2
      ;;
    --foundry-project)
      foundry_project_name="${2:-}"
      shift 2
      ;;
    --search-connection-name)
      search_connection_name="${2:-}"
      shift 2
      ;;
    --storage-account)
      storage_account_name="${2:-}"
      shift 2
      ;;
    --storage-container)
      storage_container_name="${2:-}"
      shift 2
      ;;
    --search-datasource-name)
      search_datasource_name="${2:-}"
      shift 2
      ;;
    --search-indexer-name)
      search_indexer_name="${2:-}"
      shift 2
      ;;
    --search-index-name)
      search_index_name="${2:-}"
      shift 2
      ;;
    --auto-index-blob-storage)
      auto_index_blob_storage="true"
      shift 1
      ;;
    --agent-name)
      agent_name="${2:-}"
      shift 2
      ;;
    --agent-instructions)
      agent_instructions="${2:-}"
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

require_value "--subscription" "$subscription"
require_value "--resource-group" "$resource_group"
require_value "--location" "$location"

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
foundry_account_name="${foundry_account_name:-$(slugify "${resource_group}-foundry" | cut -c1-24)}"
foundry_project_name="${foundry_project_name:-proj-$(slugify "$resource_group" | cut -c1-18)}"
search_connection_name="${search_connection_name:-search-connection}"
storage_container_name="${storage_container_name:-bidlibrary}"
search_datasource_name="${search_datasource_name:-bidlibrary-datasource}"
search_index_name="${search_index_name:-}"
search_indexer_name="${search_indexer_name:-bidlibrary-indexer}"
agent_name="${agent_name:-talentsuite-agent}"
agent_instructions="${agent_instructions:-You are a helpful bid-writing assistant. Give concise, accurate answers and cite sources when search results are available.}"

if [ "$auto_index_blob_storage" = "true" ] && [ -z "$search_index_name" ]; then
  search_index_name="bidlibrary-index"
fi

az account set --subscription "$subscription"

az group create \
  --name "$resource_group" \
  --location "$location" >/dev/null

echo "Ensuring Azure OpenAI account $openai_account_name"
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

echo "Ensuring Azure OpenAI deployment $openai_model_deployment"
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
    --sku-name GlobalStandard \
    --sku-capacity "$openai_model_capacity" >/dev/null
fi

echo "Ensuring Azure AI Search service $search_service_name"
if ! az search service show --name "$search_service_name" --resource-group "$resource_group" >/dev/null 2>&1; then
  az search service create \
    --name "$search_service_name" \
    --resource-group "$resource_group" \
    --location "$location" \
    --sku "$search_sku" \
    --replica-count "$search_replicas" \
    --partition-count "$search_partitions" >/dev/null
fi

echo "Ensuring Azure AI Foundry account $foundry_account_name"
if ! az cognitiveservices account show --name "$foundry_account_name" --resource-group "$resource_group" >/dev/null 2>&1; then
  az cognitiveservices account create \
    --name "$foundry_account_name" \
    --resource-group "$resource_group" \
    --location "$location" \
    --kind AIServices \
    --sku S0 \
    --allow-project-management \
    --yes >/dev/null

fi

echo "Ensuring Azure AI Foundry custom subdomain $foundry_account_name"
az cognitiveservices account update \
  --name "$foundry_account_name" \
  --resource-group "$resource_group" \
  --custom-domain "$foundry_account_name" >/dev/null

echo "Ensuring Azure AI Foundry project $foundry_project_name"
if ! az cognitiveservices account project show \
  --name "$foundry_account_name" \
  --resource-group "$resource_group" \
  --project-name "$foundry_project_name" >/dev/null 2>&1; then
  az cognitiveservices account project create \
    --name "$foundry_account_name" \
    --resource-group "$resource_group" \
    --project-name "$foundry_project_name" \
    --location "$location" >/dev/null
fi

search_endpoint="https://${search_service_name}.search.windows.net"
search_primary_key="$(az search admin-key show \
  --service-name "$search_service_name" \
  --resource-group "$resource_group" \
  --query primaryKey -o tsv)"
storage_connection_id=""
storage_resource_id=""
if [ "$auto_index_blob_storage" = "true" ]; then
  if [ -z "$storage_account_name" ]; then
    storage_account_name="$(resolve_aspire_storage_account "$resource_group")"
  fi

  require_value "--storage-account (or discoverable Aspire bid content storage account)" "$storage_account_name"
  storage_resource_id="$(az storage account show \
    --name "$storage_account_name" \
    --resource-group "$resource_group" \
    --query id -o tsv)"
  require_value "storage account resource id" "$storage_resource_id"

  echo "Enabling system-assigned identity on Azure AI Search service $search_service_name"
  ensure_search_managed_identity "$resource_group" "$search_service_name"

  search_principal_id="$(az resource show \
    --resource-group "$resource_group" \
    --resource-type "Microsoft.Search/searchServices" \
    --name "$search_service_name" \
    --query identity.principalId -o tsv)"
  require_value "Azure AI Search managed identity principal id" "$search_principal_id"

  echo "Granting Azure AI Search managed identity Storage Blob Data Reader on $storage_account_name"
  if ! az role assignment list \
    --assignee-object-id "$search_principal_id" \
    --scope "$storage_resource_id" \
    --query "[?roleDefinitionName=='Storage Blob Data Reader'][0].id" \
    -o tsv | grep -q .; then
    az role assignment create \
      --assignee-object-id "$search_principal_id" \
      --assignee-principal-type ServicePrincipal \
      --role "Storage Blob Data Reader" \
      --scope "$storage_resource_id" >/dev/null
  fi

  echo "Waiting for Storage Blob Data Reader role assignment to propagate"
  sleep 30

  echo "Ensuring blob container $storage_container_name in storage account $storage_account_name"
  az storage container create \
    --name "$storage_container_name" \
    --account-name "$storage_account_name" \
    --auth-mode login >/dev/null

  echo "Ensuring Azure AI Search datasource $search_datasource_name"
  datasource_payload="$(jq -n \
    --arg name "$search_datasource_name" \
    --arg connectionString "ResourceId=${storage_resource_id};" \
    --arg containerName "$storage_container_name" \
    '{
      name: $name,
      type: "azureblob",
      credentials: {
        connectionString: $connectionString
      },
      container: {
        name: $containerName
      },
      dataChangeDetectionPolicy: {
        "@odata.type": "#Microsoft.Azure.Search.HighWaterMarkChangeDetectionPolicy",
        highWaterMarkColumnName: "metadata_storage_last_modified"
      }
    }')"
  search_api_with_retry PUT \
    "$search_endpoint/datasources/$search_datasource_name?api-version=2024-07-01" \
    "$search_primary_key" \
    "$datasource_payload" >/dev/null

  echo "Ensuring Azure AI Search index $search_index_name"
  index_payload="$(jq -n \
    --arg name "$search_index_name" \
    '{
      name: $name,
      fields: [
        {
          name: "id",
          type: "Edm.String",
          key: true,
          searchable: false,
          filterable: true,
          retrievable: true,
          sortable: false,
          facetable: false
        },
        {
          name: "content",
          type: "Edm.String",
          searchable: true,
          filterable: false,
          retrievable: true,
          sortable: false,
          facetable: false
        },
        {
          name: "metadata_storage_name",
          type: "Edm.String",
          searchable: true,
          filterable: true,
          retrievable: true,
          sortable: true,
          facetable: false
        },
        {
          name: "metadata_storage_path",
          type: "Edm.String",
          searchable: false,
          filterable: true,
          retrievable: true,
          sortable: false,
          facetable: false
        },
        {
          name: "metadata_storage_last_modified",
          type: "Edm.DateTimeOffset",
          searchable: false,
          filterable: true,
          retrievable: true,
          sortable: true,
          facetable: false
        },
        {
          name: "metadata_storage_content_type",
          type: "Edm.String",
          searchable: false,
          filterable: true,
          retrievable: true,
          sortable: false,
          facetable: true
        }
      ]
    }')"
  search_api_with_retry PUT \
    "$search_endpoint/indexes/$search_index_name?api-version=2024-07-01" \
    "$search_primary_key" \
    "$index_payload" >/dev/null

  echo "Ensuring Azure AI Search indexer $search_indexer_name"
  indexer_payload="$(jq -n \
    --arg name "$search_indexer_name" \
    --arg dataSourceName "$search_datasource_name" \
    --arg targetIndexName "$search_index_name" \
    '{
      name: $name,
      dataSourceName: $dataSourceName,
      targetIndexName: $targetIndexName,
      schedule: {
        interval: "PT30M"
      },
      parameters: {
        configuration: {
          dataToExtract: "contentAndMetadata",
          parsingMode: "default"
        }
      },
      fieldMappings: [
        {
          sourceFieldName: "metadata_storage_path",
          targetFieldName: "id",
          mappingFunction: {
            name: "base64Encode"
          }
        }
      ]
    }')"
  search_api_with_retry PUT \
    "$search_endpoint/indexers/$search_indexer_name?api-version=2024-07-01" \
    "$search_primary_key" \
    "$indexer_payload" >/dev/null

  echo "Running Azure AI Search indexer $search_indexer_name"
  search_api_with_retry POST \
    "$search_endpoint/indexers/$search_indexer_name/run?api-version=2024-07-01" \
    "$search_primary_key" >/dev/null
fi
openai_endpoint="$(az cognitiveservices account show \
  --name "$openai_account_name" \
  --resource-group "$resource_group" \
  --query properties.endpoint -o tsv)"
openai_api_key="$(az cognitiveservices account keys list \
  --name "$openai_account_name" \
  --resource-group "$resource_group" \
  --query key1 -o tsv)"
foundry_project_endpoint="https://${foundry_account_name}.services.ai.azure.com/api/projects/${foundry_project_name}"
foundry_account_id="$(az cognitiveservices account show \
  --name "$foundry_account_name" \
  --resource-group "$resource_group" \
  --query id -o tsv)"
require_value "Azure AI Foundry account resource id" "$foundry_account_id"
foundry_project_id="${foundry_account_id}/projects/${foundry_project_name}"
current_principal_id="$(get_current_principal_object_id || true)"
if [ -n "$current_principal_id" ]; then
  echo "Ensuring current principal has Azure AI User on Foundry account and project"
  ensure_role_assignment "$current_principal_id" "Azure AI User" "$foundry_account_id"
  ensure_role_assignment "$current_principal_id" "Azure AI User" "$foundry_project_id"
  echo "Waiting for Azure AI User role assignment to propagate"
  sleep 30
fi

connection_id=""
if [ -n "$search_index_name" ]; then
  echo "Ensuring project connection $search_connection_name to Azure AI Search"
  subscription_id="$(az account show --query id -o tsv)"
  management_token="$(az account get-access-token \
    --resource "https://management.azure.com" \
    --query accessToken -o tsv)"
  connection_id="/subscriptions/${subscription_id}/resourceGroups/${resource_group}/providers/Microsoft.CognitiveServices/accounts/${foundry_account_name}/projects/${foundry_project_name}/connections/${search_connection_name}"
  connection_url="https://management.azure.com${connection_id}?api-version=2025-06-01"
  connection_payload="$(jq -n \
    --arg target "$search_endpoint" \
    --arg searchKey "$search_primary_key" \
    '{
      properties: {
        category: "CognitiveSearch",
        target: $target,
        authType: "ApiKey",
        credentials: {
          key: $searchKey
        }
      }
    }')"

  curl -fsS \
    -X PUT \
    -H "Authorization: Bearer $management_token" \
    -H "Content-Type: application/json" \
    "$connection_url" \
    --data "$connection_payload" >/dev/null
fi

echo "Ensuring Foundry agent $agent_name"
agent_token="$(get_foundry_access_token)"
require_value "Foundry access token" "$agent_token"
assistants_response="$(foundry_api_with_retry GET \
  "$foundry_project_endpoint/assistants?api-version=v1" \
  "$agent_token" || true)"
if [ -n "$assistants_response" ] && ! printf '%s' "$assistants_response" | jq -e . >/dev/null 2>&1; then
  echo "Foundry assistants API returned a non-JSON response:"
  printf '%s\n' "$assistants_response" | head -c 1000
  echo
  exit 1
fi
existing_agent_id="$(printf '%s' "$assistants_response" \
  | jq -r --arg name "$agent_name" '.data[]? | select(.name == $name) | .id' \
  | head -n 1 || true)"

if [ -n "$existing_agent_id" ]; then
  agent_id="$existing_agent_id"
else
  agent_payload="$(jq -n \
    --arg model "$openai_model_deployment" \
    --arg name "$agent_name" \
    --arg instructions "$agent_instructions" \
    --arg connectionId "$connection_id" \
    --arg indexName "$search_index_name" '
    if $connectionId != "" and $indexName != "" then
      {
        model: $model,
        name: $name,
        instructions: $instructions,
        tools: [
          {
            type: "azure_ai_search"
          }
        ],
        tool_resources: {
          azure_ai_search: {
            indexes: [
              {
                project_connection_id: $connectionId,
                index_name: $indexName,
                query_type: "simple"
              }
            ]
          }
        }
      }
    else
      {
        model: $model,
        name: $name,
        instructions: $instructions
      }
    end
  ')"

  agent_response="$(foundry_api_with_retry POST \
    "$foundry_project_endpoint/assistants?api-version=v1" \
    "$agent_token" \
    "$agent_payload" || true)"
  if ! printf '%s' "$agent_response" | jq -e . >/dev/null 2>&1; then
    echo "Foundry agent create API returned a non-JSON response:"
    printf '%s\n' "$agent_response" | head -c 1000
    echo
    exit 1
  fi
  agent_id="$(printf '%s' "$agent_response" | jq -r '.id')"
fi

echo
echo "Created resources:"
echo "  Azure OpenAI account: $openai_account_name"
echo "  Azure OpenAI endpoint: $openai_endpoint"
echo "  Azure OpenAI deployment: $openai_model_deployment"
echo "  Azure AI Search service: $search_service_name"
echo "  Azure AI Search endpoint: $search_endpoint"
if [ -n "$storage_account_name" ]; then
  echo "  Aspire blob storage account: $storage_account_name"
  echo "  Aspire blob storage container: $storage_container_name"
fi
echo "  Azure AI Foundry account: $foundry_account_name"
echo "  Azure AI Foundry project: $foundry_project_name"
echo "  Azure AI Foundry project endpoint: $foundry_project_endpoint"
if [ -n "$connection_id" ]; then
  echo "  Azure AI Search project connection: $search_connection_name"
  echo "  Azure AI Search project connection id: $connection_id"
fi
if [ "$auto_index_blob_storage" = "true" ]; then
  echo "  Azure AI Search datasource: $search_datasource_name"
  echo "  Azure AI Search index: $search_index_name"
  echo "  Azure AI Search indexer: $search_indexer_name"
fi
echo "  Azure AI Foundry agent name: $agent_name"
echo "  Azure AI Foundry agent id: $agent_id"

echo
echo "App configuration values:"
echo "  AzureOpenAI__Endpoint=$openai_endpoint"
echo "  AzureOpenAI__ApiKey=$openai_api_key"
echo "  AzureOpenAI__ChatDeployment=$openai_model_deployment"
echo "  AzureAIFoundry__ProjectEndpoint=$foundry_project_endpoint"
echo "  Agents__AgentId=$agent_id"

if [ "$emit_azd_env" = "true" ]; then
  echo
  echo "azd env commands:"
  echo "  azd env set AZURE_OPENAI_ENDPOINT \"$openai_endpoint\""
  echo "  azd env set AZURE_OPENAI_CHAT_DEPLOYMENT \"$openai_model_deployment\""
  echo "  azd env set AZURE_AI_FOUNDRY_PROJECT_ENDPOINT \"$foundry_project_endpoint\""
  echo "  azd env set AGENTS_AGENT_ID \"$agent_id\""
fi
