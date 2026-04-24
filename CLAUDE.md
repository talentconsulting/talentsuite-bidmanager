# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build TalentSuite.sln

# Run all tests
dotnet test src/TalentSuite.Server.Tests/TalentSuite.Server.Tests.csproj --configuration Release --no-restore -v minimal
dotnet test src/TalentSuite.SliceTests/TalentSuite.SliceTests.csproj --configuration Release --no-restore -v minimal

# Run a single test (by name pattern)
dotnet test src/TalentSuite.SliceTests/TalentSuite.SliceTests.csproj --filter "FullyQualifiedName~TestClassName"

# Local dev (requires Docker for SQL Server, Keycloak, Service Bus Emulator, Azurite)
dotnet run --project TalentSuite.AppHost
```

## Architecture

This is an Aspire-orchestrated .NET 10 multi-service application for managing tender bids with AI-assisted document ingestion.

### Services

| Project | Role |
|---------|------|
| `TalentSuite.AppHost` | Aspire host — orchestrates all services locally and provisions Azure resources |
| `src/TalentSuite.Server` | ASP.NET Core 10 Web API (controllers pattern) |
| `src/TalentSuite.FrontEnd` | Blazor WebAssembly SPA |
| `src/TalentSuite.Functions` | Azure Functions v4 (background jobs, email) |
| `src/TalentSuite.Shared` | DTOs and messaging contracts shared across services |
| `TalentSuite.ServiceDefaults` | Shared Aspire configuration, health checks, OpenTelemetry |

### Data Flow

**Authentication:** Frontend authenticates via Keycloak OIDC → backend validates JWT Bearer tokens → role-based authorization via `realm_access` / `resource_access` claims.

**Document Ingestion:** User uploads PDF/Excel → `DocumentIngestionService` sends to Azure AI Document Intelligence (text extraction) → Azure OpenAI chunks and parses content into structured questions → stored in SQL Server.

**AI Chat:** User asks question about a bid → `AzureOpenAiChatService` → Azure AI Foundry Persistent Agent (backed by Azure AI Search over indexed bid content) → Azure OpenAI completion.

**Async Messaging:** User actions publish to Azure Service Bus → `TalentSuite.Functions` consumes messages to send invite emails (`invite-user`), handle bid submissions (`bid-submitted`), and notify comment mentions (`comment-saved-with-mentions`). A timer trigger syncs blobs to Google Drive every 30 minutes.

### Server internals (`src/TalentSuite.Server`)

Code is organised by feature (Bids, Users, Health, Messaging, Security). Each feature folder contains Controllers, Services, and Data sub-folders. Data access uses Dapper against SQL Server in production; an `InMemoryBidRepository` is available for tests. Object mapping is done via Riok.Mapperly (source-generated, no reflection).

### Frontend internals (`src/TalentSuite.FrontEnd`)

Blazor WASM SPA. Key page groups under `Pages/Bids/`: bid list (`Home.razor`), detail/question management (`Manage.razor`), document upload (`Ingest.razor`), ingestion job history (`IngestionJobs.razor`), and parsed content review (`IngestSummary.razor`). API calls go through typed `HttpClient` services registered in `Program.cs`.

### Testing approach

- **`TalentSuite.Server.Tests`** — NUnit unit tests against server-side logic.
- **`TalentSuite.SliceTests`** — NUnit integration/slice tests using `Microsoft.AspNetCore.Mvc.Testing`; spin up the real server with the in-memory repository so no external dependencies are needed.

### Local vs Azure mode

`AppHost.cs` checks `TALENTSUITE_INFRA_MODE`. When `azure`, it provisions Azure Container Apps, Azure SQL, Service Bus, Storage, OpenAI, AI Foundry, and AI Search. Locally it uses Docker containers (SQL Server, Keycloak, Service Bus Emulator, Azurite). The `azure.yaml` manifest drives `azd` deployments; separate GitHub Actions workflows handle individual service deploys (`azure-talentserver.yml`, `azure-frontend.yml`, `azure-functions.yml`).