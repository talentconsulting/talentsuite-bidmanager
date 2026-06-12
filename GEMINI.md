# TalentSuite Bid Manager

## Project Overview
This is a .NET Aspire-orchestrated bid management application designed to handle document ingestion, AI-driven chat/querying, and bid task management. The system is composed of several key projects:
- **`TalentSuite.AppHost`**: The .NET Aspire host for local orchestration and Azure infrastructure provisioning.
- **`TalentSuite.FrontEnd`**: Blazor WebAssembly frontend UI.
- **`TalentSuite.Server`**: ASP.NET Core backend API and domain logic.
- **`TalentSuite.Functions`**: Azure Functions for background processes (e.g., Service Bus message consumers, Google Drive sync).
- **`TalentSuite.Shared`**: Shared contracts, models, and messaging types.
- **`TalentSuite.SliceTests`**: Integration and slice tests.
- **`TalentSuite.ServiceDefaults`**: Default configurations for OpenTelemetry, health checks, and service discovery.

**Key Technologies:**
- **Language/Framework:** C# / .NET 10
- **Frontend:** Blazor WebAssembly
- **Backend:** ASP.NET Core
- **Database:** Microsoft SQL Server (Azure SQL in production)
- **Authentication/Authorization:** Keycloak (OIDC)
- **Messaging:** Azure Service Bus (emulator locally)
- **Storage:** Azure Blob Storage (Azurite locally)
- **AI/ML:** Azure OpenAI, Azure AI Document Intelligence, Azure AI Foundry, Azure AI Search.
- **Monitoring:** Grafana, Azure Monitor, Application Insights.
- **Deployment:** Azure Container Apps (via `azd` and GitHub Actions).

## Building and Running
The application requires the .NET SDK (targeting .NET 10), Docker Desktop (for local containers), and the Aspire CLI.

**Run the Full Local Stack:**
You can run the entire distributed application using .NET Aspire from the repository root:
```bash
aspire run
```
Alternatively, use the provided helper script which validates prerequisites before starting:
```bash
./scripts/run-local-all.sh
```

**Running Tests:**
The project uses slice/integration tests located in `TalentSuite.SliceTests`.
To build the tests:
```bash
dotnet build src/TalentSuite.SliceTests/TalentSuite.SliceTests.csproj -v minimal -nr:false -maxcpucount:1
```
To run the tests:
```bash
dotnet test src/TalentSuite.SliceTests/TalentSuite.SliceTests.csproj
```

## Development Conventions
- **Infrastructure as Code (IaC):** The project relies on `.NET Aspire` (`TalentSuite.AppHost`) to define both local and Azure environments. Azure resources are provisioned dynamically based on the Aspire application model (generating Bicep templates).
- **Configuration:** Local environment variables can be set using `.env.local` or `.env.azure.local`. For user secrets, use the `dotnet user-secrets` CLI tool targeted at the `TalentSuite.AppHost` project.
- **Authentication:** The backend relies on JWT bearer tokens issued by Keycloak. Role claims (`realm_access` / `resource_access`) are mapped to standard .NET claims.
- **AI Integrations:** The application supports document ingestion (Excel and PDFs via Azure Document Intelligence) chunking and processing via Azure OpenAI. The AI chat feature uses an Azure AI Foundry Agent for RAG (Retrieval-Augmented Generation) patterns.
- **CI/CD:** GitHub Actions workflows (e.g., `azure-deploy.yml`) handle deployments using `azd provision` and `azd deploy`. Secrets are explicitly synced to an Azure Key Vault post-deployment using shell scripts.
