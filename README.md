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

This starts the full local environment (frontend, backend, SQL Server, Keycloak, messaging emulator).

Infrastructure mode is controlled by one variable in `TalentSuite.AppHost`:
- `TALENTSUITE_INFRA_MODE=local` (default): local SQL container + Service Bus emulator + Azurite emulator
- `TALENTSUITE_INFRA_MODE=azure`: Azure SQL + Azure Service Bus + Azure Storage resources

When `TALENTSUITE_INFRA_MODE=azure`, set AppHost parameter `KeycloakDbJdbcUrl` to your Azure SQL JDBC URL for the Keycloak database.

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
dotnet user-secrets --project TalentSuite.AppHost set "Parameters:InviteEmailEnabled" "true"
dotnet user-secrets --project TalentSuite.AppHost set "Parameters:InviteFromEmail" "no-reply@yourdomain.com"
dotnet user-secrets --project TalentSuite.AppHost set "Parameters:InviteSmtpHost" "smtp.yourprovider.com"
dotnet user-secrets --project TalentSuite.AppHost set "Parameters:InviteSmtpPort" "587"
dotnet user-secrets --project TalentSuite.AppHost set "Parameters:InviteSmtpEnableSsl" "true"
dotnet user-secrets --project TalentSuite.AppHost set "Parameters:InviteSmtpUsername" "smtp-username"
dotnet user-secrets --project TalentSuite.AppHost set "Parameters:InviteSmtpPassword" "smtp-password"
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

Deployment scope is defined by `TalentSuite.AppHost/AppHost.cs` and `TALENTSUITE_INFRA_MODE`.

When `TALENTSUITE_INFRA_MODE=azure`, deploy includes:
- App services:
  - `talentserver` (`src/TalentSuite.Server`)
  - `talentfrontend` (`src/TalentSuite.FrontEnd`)
  - `talentfunctions` (`src/TalentSuite.Functions`)
  - `keycloak` (Keycloak container resource)
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

## Troubleshooting
- If authentication UI loops or role-based UI seems wrong, verify token claims (`roles`, `realm_access`, `resource_access`) and `/api/users/me-identity-debug`.
- If local state is inconsistent, restart `aspire run`.
- If needed, clear browser cookies/session for localhost origins.
