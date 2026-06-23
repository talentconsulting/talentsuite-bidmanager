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

**Pattern:** Feature-folder / Vertical Slice Architecture — each feature (Bids, Users, Health, Messaging, Security) owns its controllers, services, data, and models in one folder. No shared base classes crossing feature boundaries.

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

## Coding Conventions

### Naming

- **Controllers:** `{Feature}Controller` — `[ApiController]`, `[Authorize]`, explicit HTTP verb attributes
- **Services:** `I{Feature}Service` interface + sealed `{Feature}Service` implementation
- **Repositories:** `I{Feature}Repository` interface (e.g. `IManageBids`) + `SqlServer{Feature}Repository` implementation; `InMemory{Feature}Repository` for tests
- **Data models:** `{Entity}DataModel` (DB layer); `{Entity}Model` (service layer); `{Entity}Response` / `{Entity}Request` (HTTP DTOs)
- **Mappers:** `{Feature}Mapper` — Riok.Mapperly source-generated, registered as DI dependency
- **Test files:** `{Feature}_{scenario}.cs` (e.g. `Bid_files.cs`, `Draft_management.cs`), inherit from `SliceTestBase`

### Method signatures

- All async methods take `CancellationToken ct` as the last parameter (non-optional in controllers, `ct = default` in services)
- Throw `InvalidOperationException` / `KeyNotFoundException` for domain errors; no custom exception hierarchy

### DI Registration

- Each feature registers via an extension method in `Program.cs`: `builder.AddBidServices()`, `builder.AddUserServices()`, etc.
- Mappers registered as singleton, repositories and services as scoped

### Data Access (Dapper)

- Use `CommandDefinition` for all parameterized queries
- JSON columns (`NVARCHAR(MAX)`) with SQL Server JSON functions (`JSON_MODIFY`, `OPENJSON`, `JSON_VALUE`)
- Upsert with `IF EXISTS … BEGIN UPDATE … ELSE INSERT … END`
- Schema initialization once per process via `SemaphoreSlim` lock in `EnsureSchemaAsync()`
- Explicit transaction management: `await connection.BeginTransactionAsync()`

### Tests (NUnit + `Microsoft.AspNetCore.Mvc.Testing`)

- Arrange-Act-Assert with HTTP calls against `TestWebApplicationFactory` (no mocks)
- Assert HTTP status codes: `Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK))`
- Extract common setup into helper methods (`CreateBidAsync()`, etc.)
- No Testcontainers — use the in-memory repository swap in `TestWebApplicationFactory`

## Tech Stack Summary

| Concern | Choice |
| ------- | ------ |
| Runtime | .NET 10 |
| API | ASP.NET Core 10 Web API (controllers) |
| Frontend | Blazor WebAssembly |
| Background | Azure Functions v4 |
| Orchestration | .NET Aspire 13 |
| Data | Dapper + SQL Server |
| Auth | JWT Bearer + Keycloak OIDC |
| Messaging | Azure Service Bus (direct SDK) |
| AI | Azure AI Document Intelligence, Azure OpenAI, Azure AI Foundry |
| Mapping | Riok.Mapperly (source-generated) |
| Observability | OpenTelemetry → Azure Monitor |
| Testing | NUnit + `Microsoft.AspNetCore.Mvc.Testing` |

## dotnet-claude-kit Skills

Use these skills when working in this codebase:

| Task | Skill |
| ---- | ----- |
| Add a new feature end-to-end | `/feature-dev` |
| Review a PR or recent changes | `/code-review` |
| Fix a broken build | `/build-fix` |
| Check code quality & health | `/health-check` (after init) |
| Security audit | `/security-scan` |
| Logging improvements | `/serilog` or `/opentelemetry` |
| Resilience / retry policies | `/resilience` |
| New API endpoint | `/minimal-api` or `/openapi` |
| Architecture decisions | `/architecture-advisor` |
| Scaffold a new feature slice | `/vertical-slice` |
