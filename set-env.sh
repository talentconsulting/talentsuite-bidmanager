#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   source ./set-env.sh local
#   source ./set-env.sh azure
#
# Use "source" so vars stay in your current shell.

MODE="${1:-local}"

if [[ "$MODE" == "local" ]]; then
  export TALENTSUITE_INFRA_MODE="local"

  # AppHost parameters (optional overrides)
  export Parameters__AuthenticationEnabled="true"
  export Parameters__UseInMemoryData="false"
  export Parameters__SqlPassword="Your_strong_password123!"
  export Parameters__KeycloakPassword="admin"

  # Optional AI ingestion settings (leave blank to use in-memory ingestion fallback)
  export DocumentIntelligence__Endpoint=""
  export DocumentIntelligence__ApiKey=""
  export AzureOpenAI__Endpoint=""
  export AzureOpenAI__ApiKey=""
  export AzureOpenAI__ChatDeployment="gpt-4.1"

  echo "Local mode env vars set."
elif [[ "$MODE" == "azure" ]]; then
  export TALENTSUITE_INFRA_MODE="azure"

  # Required for Keycloak DB in azure mode
  export Parameters__KeycloakDbJdbcUrl="jdbc:sqlserver://<server>.database.windows.net:1433;databaseName=keycloakdb;encrypt=true;trustServerCertificate=false;hostNameInCertificate=*.database.windows.net;loginTimeout=30;"
  export Parameters__KeycloakDbUsername="<db-username>"
  export Parameters__KeycloakDbPassword="<db-password>"

  # Other AppHost params
  export Parameters__AuthenticationEnabled="true"
  export Parameters__UseInMemoryData="false"
  export Parameters__KeycloakPassword="<keycloak-admin-password>"

  # AI settings
  export DocumentIntelligence__Endpoint="https://<doc-intel>.cognitiveservices.azure.com/"
  export DocumentIntelligence__ApiKey="<doc-intel-key>"
  export AzureOpenAI__Endpoint="https://<openai>.openai.azure.com/"
  export AzureOpenAI__ApiKey="<openai-key>"
  export AzureOpenAI__ChatDeployment="gpt-4.1"

  echo "Azure mode env vars set."
else
  echo "Unknown mode: $MODE (use 'local' or 'azure')"
  return 1 2>/dev/null || exit 1
fi