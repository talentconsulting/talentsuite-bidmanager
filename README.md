# TalentSuite Bid Manager

Aspire-orchestrated bid management application with:
- Blazor WebAssembly frontend
- ASP.NET Core backend API
- Keycloak for authentication/authorization
- SQL Server (default) or in-memory repositories
- Azure Service Bus emulator for messaging

## Projects
- `TalentSuite.AppHost`: Aspire host and local orchestration
- `src/TalentSuite.FrontEnd`: Blazor WebAssembly UI
- `src/TalentSuite.Server`: API and domain logic
- `src/TalentSuite.Functions`: background handlers (Service Bus consumers)
- `src/TalentSuite.Shared`: shared contracts/models
- `src/TalentSuite.SliceTests`: slice/integration-style tests

## Prerequisites
- .NET SDK (project targets .NET 10)
- Docker Desktop (for local containers/emulators)
- Aspire CLI

## Run Locally
From repository root:

```bash
aspire run
```

Or use the executable helper script:

```bash
./scripts/run-local-all.sh
```

This script validates prerequisites (`dotnet`, `aspire`, `docker`) and starts the full local stack.

This starts the full local environment (frontend, backend, SQL Server, Keycloak, messaging emulator).

Infrastructure mode is controlled by one variable in `TalentSuite.AppHost`:
- `TALENTSUITE_INFRA_MODE=local` (default): local SQL container + Service Bus emulator + Azurite emulator
- `TALENTSUITE_INFRA_MODE=azure`: Azure SQL + Azure Service Bus + Azure Storage resources

When `TALENTSUITE_INFRA_MODE=azure`, Aspire derives the Keycloak JDBC URL from the provisioned Azure SQL `keycloakdb` resource automatically.

For local secret overrides used by `set-env.sh`, create:
- `.env.local`
- `.env.azure.local` (for azure mode only)

Example `.env.azure.local`:
```bash
AZURE_SUBSCRIPTION_ID=00000000-0000-0000-0000-000000000000
```

Then run:
```bash
source ./set-env.sh azure
```

Notes:
- The app uses HTTPS locally.
- On first run, Aspire may attempt to trust a dev certificate.

## Blob Storage Setup (Bid Library)
The Functions app writes submitted bid answers to Azure Blob Storage:
- Connection string key: `ConnectionStrings:bidstorage`
- Container: `bidlibrary` (created automatically if missing)

### Option 1: Run with Aspire (recommended)
No manual storage setup is required. `TalentSuite.AppHost` provisions an Azure Storage emulator resource named `bidstorage` and wires the connection string into the Functions app.

### Option 2: Run Functions directly (without AppHost)
You must provide `ConnectionStrings:bidstorage` in `src/TalentSuite.Functions/local.settings.json`.

Example using Azurite:

1. Start Azurite:
```bash
docker run --rm -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite
```

2. Set the local connection string in `src/TalentSuite.Functions/local.settings.json`:
```json
{
  "Values": {
    "ConnectionStrings:bidstorage": "UseDevelopmentStorage=true"
  }
}
```

3. Start Functions:
```bash
cd src/TalentSuite.Functions
func start
```

If `ConnectionStrings:bidstorage` is missing, Functions will fail with: `Connection string 'bidstorage' was not found.`

## Environment and Defaults
Configured in `TalentSuite.AppHost/AppHost.cs`:

- `AuthenticationEnabled` default: `true`
- `UseInMemoryData` default: `false` (SQL is default)
- SQL Server is provisioned with a persistent data volume: `talentsuite-sql-data`
- Keycloak uses SQL Server (`keycloakdb`)
- Service Bus emulator queues: `invite-user`, `bid-submitted`

Propagated runtime env vars include:
- `AUTHENTICATION_ENABLED`
- `USE_IN_MEMORY_DATA`
- `AzureServiceBus__InviteUserEntityName`
- `AzureServiceBus__BidSubmittedEntityName`

## Canonical Config Keys
Use plain keys in local `.env.azure.local` / `.env.local` and in GitHub Actions `vars`/`secrets`.

Example `.env.azure.local`:
```bash
AuthenticationEnabled=true
UseInMemoryData=false

SqlPassword=your-sql-admin-password
KeycloakPassword=your-keycloak-admin-password
KeycloakDbUsername=keycloak_admin
KeycloakDbPassword=your-keycloak-db-password
KeycloakClientId=talentsuite-frontend

InviteEmailEnabled=true
InviteFromEmail=no-reply@talentconsulting.uk
InviteSmtpHost=smtp.office365.com
InviteSmtpPort=587
InviteSmtpEnableSsl=true
InviteSmtpUsername=smtp-user
InviteSmtpPassword=your-smtp-password

GoogleDriveSyncEnabled=true
GoogleDriveSyncSourceContainerName=bidlibrary
GoogleDriveSyncDriveFolderId=your-google-drive-folder-id
GoogleDriveSyncServiceAccountJsonBase64=BASE64_OF_FULL_SERVICE_ACCOUNT_JSON

GrafanaEntraEnabled=true
GrafanaEntraClientId=your-grafana-app-registration-client-id
GrafanaEntraTenantId=your-entra-tenant-id
GrafanaEntraClientSecret=your-grafana-app-registration-client-secret
GrafanaPublicOrigin=https://grafana-dev.talentsuite.uk
GrafanaAzureMonitorSubscriptionId=00000000-0000-0000-0000-000000000000

KeyVaultName=kv-talentsuite-dev
```

`KeyVaultName` is optional. If set, CI uses that exact vault name (must be 3-24 chars, lowercase letters/numbers/hyphens).

## Grafana
Grafana is hosted as an Aspire-managed container:
- local: exposed by AppHost with a dynamic local URL
- Azure: deployed as a Container App named `grafana`

The Grafana image is defined in:
- `ops/grafana/Dockerfile`

Datasource provisioning is defined in:
- `ops/grafana/provisioning/datasources/azure-monitor.yaml`

Dashboard imports are available in:
- `ops/grafana/dashboards/`
- `ops/grafana/dashboards/README.md`

### What is preconfigured
- Microsoft Entra ID login is supported through Grafana's `auth.azuread` settings.
- Azure Monitor is provisioned as a datasource using managed identity.

Azure Monitor is built into Grafana. There is no separate Azure Monitor plugin package to install.

### Required config
Use these canonical keys in local env files and GitHub Actions `vars`/`secrets`:

`vars`:
- `GRAFANA_ENTRA_ENABLED`
- `GRAFANA_ENTRA_CLIENT_ID`
- `GRAFANA_ENTRA_TENANT_ID`
- `GRAFANA_PUBLIC_ORIGIN`

`secrets`:
- `GRAFANA_ENTRA_CLIENT_SECRET`

For Azure Monitor provisioning:
- `GrafanaAzureMonitorSubscriptionId` should be the Azure subscription that contains the monitored resources.

Required Azure RBAC for the Grafana managed identity at subscription scope:
- `Reader`
- `Monitoring Reader`
- `Resource Graph Data Reader`

Without those role assignments, Grafana can authenticate but Azure Monitor resource browsing may fail with errors such as `No subscriptions were found`.

### Local run
When you run:

```bash
aspire run
```

AppHost starts Grafana alongside the rest of the stack. The local URL is allocated dynamically by Aspire, so use the URL shown in the Aspire dashboard rather than assuming a fixed port.

### Azure deploy
Grafana is included in Azure AppHost deployment and can be deployed on its own through:
- workflow: `.github/workflows/azure-deploy.yml`
- inputs:
  - `deployment_mode=apps-only`
  - `deploy_target=grafana`

### Front Door and custom domain
The Front Door workflow supports a dedicated Grafana endpoint/domain:
- workflow: `.github/workflows/azure-frontdoor.yml`
- default Grafana domain input: `grafana-dev.talentsuite.uk`

For production, set the Grafana custom domain you want, for example:
- `grafana.talentsuite.uk`

### Entra app registration
The Grafana Entra app registration must include the correct redirect URI:
- dev: `https://grafana-dev.talentsuite.uk/login/azuread`
- prod example: `https://grafana.talentsuite.uk/login/azuread`

Recommended registration shape:
- platform type: `Web`
- supported account type: `Single tenant`
- do not register this as an SPA app

For Grafana server admin assignment through Entra:
- this repo enables `GF_AUTH_AZUREAD_ALLOW_ASSIGN_GRAFANA_ADMIN=true`
- add an app role to the Entra app registration with value `GrafanaAdmin`
- assign that app role to your user or admin group in the Enterprise Application
- on next sign-in, Grafana will grant server admin from that Entra role claim

Required delegated Microsoft Graph / OIDC permissions:
- `openid`
- `profile`
- `email`
- `offline_access`
- `User.Read`

`User.Read` on its own is not enough for Grafana sign-in. The OpenID Connect scopes above are also required.

These Entra app registration settings are not managed by Aspire/AppHost. Aspire only supplies the runtime Grafana configuration values such as client ID, tenant ID, and public origin. Redirect URIs, delegated API permissions, and credentials still need to be configured in Microsoft Entra.

If the redirect URI is missing or the client secret is wrong, Grafana login will fail even if the container is running.

## Email Setup (Invite Emails)
Invite emails are sent by `src/TalentSuite.Functions` using SMTP.

Required settings:
- `InviteEmail__Enabled` = `true`
- `InviteEmail__FromEmail`
- `InviteEmail__SmtpHost`

Optional settings:
- `InviteEmail__FrontendBaseUrl` (default `https://localhost:5173`)
- `InviteEmail__FromDisplayName` (default `TalentSuite`)
- `InviteEmail__SmtpPort` (default `587`)
- `InviteEmail__SmtpEnableSsl` (default `true`)
- `InviteEmail__SmtpUsername`
- `InviteEmail__SmtpPassword`

Recommended local setup (Aspire + user secrets):

```bash
dotnet user-secrets --project TalentSuite.AppHost set "InviteEmailEnabled" "true"
dotnet user-secrets --project TalentSuite.AppHost set "InviteFromEmail" "no-reply@yourdomain.com"
dotnet user-secrets --project TalentSuite.AppHost set "InviteSmtpHost" "smtp.yourprovider.com"
dotnet user-secrets --project TalentSuite.AppHost set "InviteSmtpPort" "587"
dotnet user-secrets --project TalentSuite.AppHost set "InviteSmtpEnableSsl" "true"
dotnet user-secrets --project TalentSuite.AppHost set "InviteSmtpUsername" "smtp-username"
dotnet user-secrets --project TalentSuite.AppHost set "InviteSmtpPassword" "smtp-password"
```

Then start locally:

```bash
aspire run
```

If you run the Functions project directly (without AppHost), configure equivalent values in:
- `src/TalentSuite.Functions/local.settings.json`

Behavior notes:
- If `InviteEmail__Enabled` is `false`, no email is sent.
- If `InviteEmail__SmtpHost` or `InviteEmail__FromEmail` is missing, sending is skipped and a warning is logged.

## Timer Sync: Azure Blob to Google Drive
`src/TalentSuite.Functions` now includes a timer-triggered function:
- `SyncAzureFilesToGoogleDriveTimerFunction`
- schedule: every 30 minutes (`0 */30 * * * *`)

It performs one-way sync from Azure Blob container to a Google Drive folder:
- uploads new blobs
- updates changed blobs
- skips unchanged blobs

Function settings (in `local.settings.json` / app settings):
- `GoogleDriveSync:Enabled`
- `GoogleDriveSync:SourceContainerName` (default `bidlibrary`)
- `GoogleDriveSync:DriveFolderId`
- `GoogleDriveSync:ServiceAccountJson` or `GoogleDriveSync:ServiceAccountJsonBase64`
- `ConnectionStrings:bidstorage`

## Azure AI Usage

### Bootstrap Resources
Use `scripts/ci/provision-foundry-agent-stack.sh` to create the baseline Azure AI resources for this app:
- Azure OpenAI account
- Azure OpenAI model deployment
- Azure AI Search service
- Azure AI Foundry account
- Azure AI Foundry project
- Azure AI Foundry agent
- Optional Foundry project connection to Azure AI Search when you pass a search index name

Example:

```bash
scripts/ci/provision-foundry-agent-stack.sh \
  --subscription "<subscription-id-or-name>" \
  --resource-group "<resource-group>" \
  --location "swedencentral" \
  --foundry-account "tsfoundrydev" \
  --foundry-project "proj-talentsuite" \
  --agent-name "talentsuite-agent" \
  --auto-index-blob-storage \
  --emit-azd-env
```

Notes:
- `--auto-index-blob-storage` discovers the Aspire-created bid content storage account in Azure, targets the `bidlibrary` container by default, creates Search datasource/index/indexer resources, runs the indexer, and wires the resulting index into the agent.
- If you omit both `--auto-index-blob-storage` and `--search-index-name`, the script still creates the Foundry agent, but without Azure AI Search tool wiring.
- The script prints the resulting `AzureOpenAI__*`, `AzureAIFoundry__ProjectEndpoint`, and `Agents__AgentId` values for app configuration.
- It depends on current Azure CLI support for `az cognitiveservices account project` and `az cognitiveservices account project connection`.

### Azure OpenAI (direct in code)
- Used by bid document ingestion in `src/TalentSuite.Server/Bids/Services/DocumentIngestionService.cs`.
- Flow:
  1. Azure AI Document Intelligence extracts document text.
  2. Azure OpenAI chat completion structures that text into strict JSON (company, summary, questions, etc.).
- API endpoint:
  - `POST /api/document` (`src/TalentSuite.Server/Bids/Controllers/BidDocumentController.cs`)
- Required config:
  - `AzureOpenAI:Endpoint`
  - `AzureOpenAI:ApiKey`
  - `AzureOpenAI:ChatDeployment`

### Azure AI Search (indirect via Azure AI Foundry Agent)
- There is no direct Azure AI Search SDK usage in this repository.
- Chat answers use an Azure AI Foundry Persistent Agent in:
  - `src/TalentSuite.Server/Bids/Services/AzureOpenAiChatService.cs`
- The agent’s retrieval behavior (including AI Search/knowledge connections/tools) is configured in Azure AI Foundry, not in this code.
- Chat API endpoint:
  - `POST /api/ai/questions/{Uri.EscapeDataString(q.Id)}` (`src/TalentSuite.Server/Bids/Controllers/ChatQuestionController.cs`)
- Required config:
  - `AzureAIFoundry:ProjectEndpoint`
  - `Agents:AgentId`

### How Foundry Project, Agent, Knowledge Source, OpenAI and AI Search work together
- `AzureAIFoundry:ProjectEndpoint` identifies the Azure AI Foundry Project that hosts your AI app assets and runtime resources.
- `Agents:AgentId` identifies the Persistent Agent inside that project.
- Chat requests come to `POST /api/ai/questions/{Uri.EscapeDataString(q.Id)}` and the backend calls `AzureOpenAiChatService`.
- The service creates or reuses a thread, adds the user message, and executes a run against the configured agent.
- Inside Foundry, the agent uses its configured knowledge/tool connections to retrieve relevant content (for example, via Azure AI Search over indexed documents).
- The retrieved context plus prompt are sent to the configured OpenAI model to generate the final answer.
- The backend returns the response text and thread id; the thread id is persisted so follow-up questions keep conversation context.
- In this repository, orchestration is in code, while retrieval/tool behavior is mostly configured in Foundry.

### Ingestion fallback behavior
- Service registration in `src/TalentSuite.Server/Bids/Extensions.cs` checks ingestion config.
- If required Document Intelligence or Azure OpenAI settings are missing, the app falls back to `InMemoryDocumentIngestionService`.

## Auth Model (Current)
- Frontend authenticates with Keycloak OIDC (`code` flow).
- Backend validates JWT bearer tokens.
- Role claims are mapped from Keycloak token payload (`realm_access` / `resource_access`) to `ClaimTypes.Role`.
- Admin policy accepts role `admin` (and `Admin` for compatibility).

## Dashboard APIs
Used by `src/TalentSuite.FrontEnd/Pages/Home.razor`:

- `GET /api/tasks/my`
  - Mention-driven actions assigned to current user.
- `GET /api/tasks/my-question-assignments`
  - Question assignments for current user.
  - Filtered to active bids (`BidStatus.Underway`).

The Home dashboard renders these as two independent cards with separate loading/error states.

## Running Tests
Build all slice tests:

```bash
dotnet build src/TalentSuite.SliceTests/TalentSuite.SliceTests.csproj -v minimal -nr:false -maxcpucount:1
```

Run a specific test class (example: dashboard assignments slice):

```bash
dotnet test src/TalentSuite.SliceTests/TalentSuite.SliceTests.csproj --filter "FullyQualifiedName~Question_assignments_dashboard"
```

## Useful File References
- App host: `TalentSuite.AppHost/AppHost.cs`
- Backend startup/auth: `src/TalentSuite.Server/Program.cs`
- Frontend startup/auth: `src/TalentSuite.FrontEnd/Program.cs`
- Dashboard page: `src/TalentSuite.FrontEnd/Pages/Home.razor`
- Tasks controller: `src/TalentSuite.Server/Bids/Controllers/TasksController.cs`
- New dashboard slice test: `src/TalentSuite.SliceTests/Bids/Question_assignments_dashboard.cs`

## GitHub Actions Deploy Mapping
Workflow: `.github/workflows/azure-deploy.yml`

The workflow runs:
- `azd provision`
- `azd deploy`
- `scripts/ci/tag-azure-resources.sh` (merges tags onto RG/resources):
  - `project=TalentSuite`
  - `owner=rgparkins`

Deployment scope is defined by `TalentSuite.AppHost/AppHost.cs` and `TALENTSUITE_INFRA_MODE`.

Required GitHub Actions configuration for environment `dev`:

`vars`:
- `AZURE_ENV_NAME`
- `AZURE_LOCATION`
- `AUTHENTICATION_ENABLED`
- `USE_IN_MEMORY_DATA`
- `KEYCLOAK_CLIENT_ID`
- `KEYCLOAK_DB_USERNAME`
- `KEY_VAULT_NAME` (optional but recommended)
- `INVITE_EMAIL_ENABLED`
- `INVITE_FROM_EMAIL`
- `INVITE_SMTP_HOST`
- `INVITE_SMTP_PORT`
- `INVITE_SMTP_ENABLE_SSL`
- `INVITE_SMTP_USERNAME`
- `GOOGLE_DRIVE_SYNC_ENABLED`
- `GOOGLE_DRIVE_SYNC_SOURCE_CONTAINER_NAME`
- `GOOGLE_DRIVE_SYNC_DRIVE_FOLDER_ID`
- `GRAFANA_ENTRA_ENABLED`
- `GRAFANA_ENTRA_CLIENT_ID`
- `GRAFANA_ENTRA_TENANT_ID`
- `GRAFANA_PUBLIC_ORIGIN`

`secrets`:
- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `SQL_PASSWORD`
- `KEYCLOAK_PASSWORD`
- `KEYCLOAK_DB_PASSWORD`
- `INVITE_SMTP_PASSWORD`
- `GOOGLE_DRIVE_SYNC_SERVICE_ACCOUNT_JSON_BASE64`
- `GRAFANA_ENTRA_CLIENT_SECRET`

No JSON secret is required.

When `TALENTSUITE_INFRA_MODE=azure`, deploy includes:
- App services:
  - `talentserver` (`src/TalentSuite.Server`)
  - `talentfrontend` (`src/TalentSuite.FrontEnd`)
  - `talentfunctions` (`src/TalentSuite.Functions`)
  - `keycloak` (Keycloak container resource)
  - `grafana` (`ops/grafana`)
- Data/messaging/storage:
  - Azure SQL server resource `sql`
  - Azure SQL databases:
    - `talentconsultingdb`
    - `keycloakdb`
  - Azure Service Bus namespace `messaging`
  - Service Bus queues:
    - `invite-user`
    - `bid-submitted`
  - Azure Storage account resource `storage`
  - Blob connection resource `bidstorage`

When `TALENTSUITE_INFRA_MODE=local`, AppHost runs local emulators/containers for:
- SQL Server
- Service Bus
- Azurite (Blob storage)

### Azure Deployment Order
For a clean Azure environment build, especially after deleting the resource group, deploy in this order:

1. `Azure Infra`
2. `Azure TalentServer`
3. `Azure Keycloak`
4. `Azure Frontend`
5. `Azure Functions`
6. `Azure Front Door`

Why this order:
- `Azure Infra` recreates the shared Azure resources and Container Apps environment.
- `Azure TalentServer` exposes the public API ingress that the static frontend needs.
- `Azure Keycloak` deploys authentication and can then import the realm against the live endpoint.
- `Azure Frontend` publishes static runtime config using the public `talentserver` and `keycloak` URLs.
- `Azure Functions` comes last because it depends on the shared infra and Service Bus wiring.
- `Azure Front Door` comes after app deployment so its origins can point at live frontend, API, Keycloak, and Grafana endpoints.

### Azure Secret Handling (CI)
The workflow uses explicit GitHub `vars` and `secrets` (no JSON blob). It:

1. Logs in with OIDC.
2. Seeds azd env from explicit per-key values.
3. Runs `azd provision` and `azd deploy`.
4. Post-processes Container Apps with `scripts/ci/sync-containerapp-secrets-keyvault.sh`:
   - writes real secret values to Key Vault
   - assigns managed identity access
   - maps Container App secret refs to `keyvaultref:... ,identityref:system`

Why this is required:
- Marking those AppHost parameters as Aspire `secret: true` caused `ContainerAppSecretInvalid` failures in CI for secret names like:
  - `kc-bootstrap-***-password`
  - `kc-db-password`
  - `keycloak-***-password`
  - `inviteemail--smtppassword`
- The placeholder + Key Vault ref sync approach avoids invalid empty inline secrets during `azd deploy`.
- In Azure, Keycloak bootstrap/admin and DB password env vars are both sourced from the same seeded secret values first, then rebound to Key Vault refs after deploy.

## Troubleshooting
- If authentication UI loops or role-based UI seems wrong, verify token claims (`roles`, `realm_access`, `resource_access`) and `/api/users/me-identity-debug`.
- If local state is inconsistent, restart `aspire run`.
- If needed, clear browser cookies/session for localhost origins.
