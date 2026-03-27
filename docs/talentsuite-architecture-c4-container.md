# TalentSuite C4 Container Diagram

This diagram shows the main runtime containers for TalentSuite in the Azure deployment shape.

```mermaid
flowchart LR
    classDef person fill:#f7f3e8,stroke:#8a6d3b,color:#222;
    classDef system fill:#eef4fb,stroke:#4a6fa5,color:#222;
    classDef internal fill:#ffffff,stroke:#5b7083,color:#222;
    classDef data fill:#f4f8f1,stroke:#5a8f4d,color:#222;
    classDef external fill:#faf1f4,stroke:#b56576,color:#222;

    User["Bid Team User"]:::person
    Admin["Platform/Admin User"]:::person

    subgraph TS["TalentSuite Platform"]
        FrontDoor["Azure Front Door<br/>Routing + custom domains"]:::system
        Frontend["TalentSuite.FrontEnd<br/>Blazor WebAssembly SPA"]:::internal
        Server["TalentSuite.Server<br/>ASP.NET Core API + orchestration"]:::internal
        Functions["TalentSuite.Functions<br/>Azure Functions worker for async tasks"]:::internal
        Keycloak["Keycloak<br/>OIDC identity provider"]:::internal
        Grafana["Grafana<br/>Operations dashboards"]:::internal
        Sql["Azure SQL Database<br/>Bids + users persistence"]:::data
        ServiceBus["Azure Service Bus<br/>Async commands/events"]:::data
        HostStorage["Azure Storage<br/>Functions host / static website support"]:::data
        BidStorage["Azure Storage (bidcontentstorage)<br/>Bid library files"]:::data
    end

    OpenAI["Azure OpenAI / AI Foundry / AI Search<br/>Document analysis + answer generation"]:::external
    DocIntel["Azure AI Document Intelligence<br/>Tender document extraction"]:::external
    GoogleDrive["Google Drive<br/>Mirrored bid library"]:::external
    Entra["Microsoft Entra ID<br/>Grafana authentication"]:::external

    User -->|"Uses web UI"| FrontDoor
    Admin -->|"Views ops dashboards"| FrontDoor

    FrontDoor -->|"dev.talentsuite.uk"| Frontend
    FrontDoor -->|"api routes"| Server
    FrontDoor -->|"auth-dev.talentsuite.uk"| Keycloak
    FrontDoor -->|"grafana-dev.talentsuite.uk"| Grafana

    Frontend -->|"JSON/HTTP API"| Server
    Frontend -->|"OIDC login"| Keycloak

    Server -->|"Read/write bids, users"| Sql
    Server -->|"Publishes events"| ServiceBus
    Server -->|"Ingests documents"| DocIntel
    Server -->|"AI chat, drafting, search"| OpenAI

    Functions -->|"Consumes messages"| ServiceBus
    Functions -->|"Reads/writes bid files"| BidStorage
    Functions -->|"Reads app/API data"| Server
    Functions -->|"Mirrors files"| GoogleDrive
    Functions -->|"Invite/comment emails"| User

    Keycloak -->|"Admin/user directory integration"| Sql

    Grafana -->|"Azure Monitor via managed identity"| FrontDoor
    Grafana -->|"Azure Monitor via managed identity"| Server
    Grafana -->|"Azure Monitor via managed identity"| Functions
    Grafana -->|"Azure Monitor via managed identity"| ServiceBus
    Grafana -->|"Azure Monitor via managed identity"| HostStorage
    Grafana -->|"Azure Monitor via managed identity"| BidStorage
    Admin -->|"Microsoft Entra sign-in"| Entra
    Grafana -->|"OIDC / role claims"| Entra

    Functions -->|"Host state / triggers"| HostStorage
```

## Container responsibilities

- `TalentSuite.FrontEnd`
  - User-facing SPA for bid ingestion, management, drafting, and review.
- `TalentSuite.Server`
  - Core application API, business workflow orchestration, persistence, and AI integration.
- `TalentSuite.Functions`
  - Background processing for bid-library export, Google Drive sync, invites, mentions, and health.
- `Keycloak`
  - User authentication and token issuance for the main application.
- `Grafana`
  - Operational dashboards backed by Azure Monitor and authenticated with Microsoft Entra ID.

## Data stores and platform services

- `Azure SQL Database`
  - Stores bid and user data.
- `Azure Service Bus`
  - Carries asynchronous commands and events between the API and Functions.
- `Azure Storage`
  - Main storage account supports static/frontend and Functions host infrastructure.
- `Azure Storage (bidcontentstorage)`
  - Dedicated bid library document storage.

## External dependencies

- `Azure AI Document Intelligence`
  - Extracts structure/content from uploaded tender documents.
- `Azure OpenAI / AI Foundry / AI Search`
  - Supports AI-assisted drafting, search, and bid reasoning flows.
- `Google Drive`
  - Receives mirrored copies of bid-library files.
- `Microsoft Entra ID`
  - Provides Grafana sign-in and optional admin role assignment.
