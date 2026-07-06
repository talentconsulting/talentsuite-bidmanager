using Projects;
using System.Text.Json;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Primitives;
using Azure.Provisioning.ServiceBus;
using Azure.Provisioning.Sql;
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

var local = builder.ExecutionContext.IsRunMode;
var azureEnvironmentName = builder.Configuration["AZURE_ENV_NAME"] ?? "dev";
var resourceTags = BuildResourceTags(
    azureEnvironmentName,
    Environment.GetEnvironmentVariable("AZD_INFRA_TAGS") ?? builder.Configuration["AZD_INFRA_TAGS"],
    Environment.GetEnvironmentVariable("AZURE_RESOURCE_TAGS") ?? builder.Configuration["AZURE_RESOURCE_TAGS"]);

// Azure Policy requires these tags on every resource, including the deployment
// scripts Aspire generates for SQL role assignments. A single infrastructure
// resolver stamps them on all taggable resources as Bicep is emitted.
builder.Services.Configure<AzureProvisioningOptions>(options =>
    options.ProvisioningBuildOptions.InfrastructureResolvers.Add(new ApplyTagsResolver(resourceTags)));

IResourceBuilder<ParameterResource> Param(string name, string value, bool secret = false) =>
    builder.AddParameter(name, value: value, secret: secret, publishValueAsDefault: !secret);

var keycloakPassword = Param("KeycloakPassword", "admin", secret: true);
var sqlPassword = Param("SqlPassword", "Your_strong_password123!", secret: true);
var keycloakDbUsername = Param("KeycloakDbUsername", "");
var keycloakDbPassword = Param("KeycloakDbPassword", "unused", secret: true);
var authenticationEnabled = Param("AuthenticationEnabled", "true");
var useInMemoryData = Param("UseInMemoryData", "false");
var inviteEmailEnabled = Param("InviteEmailEnabled", "false");
var inviteFrontendBaseUrl = Param("InviteFrontendBaseUrl", "");
var inviteFromEmail = Param("InviteFromEmail", "");
var inviteSmtpHost = Param("InviteSmtpHost", "");
var inviteSmtpPort = Param("InviteSmtpPort", "587");
var inviteSmtpEnableSsl = Param("InviteSmtpEnableSsl", "true");
var inviteSmtpUsername = Param("InviteSmtpUsername", "");
var inviteSmtpPassword = Param("InviteSmtpPassword", "unused", secret: true);
var googleDriveSyncEnabled = Param("GoogleDriveSyncEnabled", "false");
var googleDriveSyncSourceContainerName = Param("GoogleDriveSyncSourceContainerName", "bidlibrary");
var googleDriveSyncDriveFolderId = Param("GoogleDriveSyncDriveFolderId", "");
var googleDriveSyncServiceAccountJsonBase64 = Param("GoogleDriveSyncServiceAccountJsonBase64", "", secret: true);
var grafanaEntraEnabled = Param("GrafanaEntraEnabled", "false");
var grafanaEntraClientId = Param("GrafanaEntraClientId", "");
var grafanaEntraTenantId = Param("GrafanaEntraTenantId", "");
var grafanaEntraClientSecret = Param("GrafanaEntraClientSecret", "", secret: true);
var grafanaPublicOrigin = Param("GrafanaPublicOrigin", "https://grafana-dev.talentsuite.uk");
var grafanaAzureMonitorSubscriptionId = Param("GrafanaAzureMonitorSubscriptionId", "");

var keycloak = builder.AddKeycloak(
            "keycloak",
            adminPassword: keycloakPassword,
            port: local ? null : 80)
    .WithEnvironment("KC_DB", "mssql")
    .WithOtlpExporter();

var keycloakHttpEndpoint = keycloak.Resource.GetEndpoint("http");

if (local)
{
    keycloak.WithRealmImport("./keycloak/realms");
}
else
{
    keycloak
        .WithEndpoint("http", endpoint => endpoint.IsExternal = true, createIfNotExists: false)
        .WithArgs("--proxy-headers=xforwarded")
        .WithArgs("--http-enabled=true")
        .WithArgs("--hostname-strict=false")
        .PublishAsAzureContainerApp((_, app) =>
        {
            app.Template ??= new();
            app.Template.Scale ??= new ContainerAppScale();
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 1;
        });
}

var messaging = builder.AddAzureServiceBus("messaging");
if (local)
{
    messaging.RunAsEmulator();
}

messaging.AddServiceBusQueue("invite-user");
messaging.AddServiceBusQueue("bid-submitted");
messaging.AddServiceBusQueue("comment-saved-with-mentions");

var storage = builder.AddAzureStorage("storage");
if (local)
{
    storage.RunAsEmulator(emulator => emulator
        .WithDataVolume("talentsuite-azurite-data", isReadOnly: false));
}

var bidStorage = local
    ? storage.AddBlobs("bidstorage")
    : builder.AddAzureStorage("bidcontentstorage").AddBlobs("bidstorage");

var msSql = builder.AddAzureSqlServer("sql");
var appDb = msSql.AddDatabase("talentconsultingdb");
var keycloakDb = msSql.AddDatabase("keycloakdb");

var server = builder.AddProject<TalentSuite_Server>("talentserver");

var functions = builder.AddProject<TalentSuite_Functions>("talentfunctions")
    .WithReference(server)
    .WithReference(bidStorage)
    .WithReference(messaging)
    .WithEnvironment("WEBSITES_PORT", "8080")
    .WithEnvironment("ASPNETCORE_URLS", "http://+:8080")
    .WithEnvironment("InviteEmail__Enabled", inviteEmailEnabled)
    .WithEnvironment("InviteEmail__FrontendBaseUrl", inviteFrontendBaseUrl)
    .WithEnvironment("InviteEmail__FromEmail", inviteFromEmail)
    .WithEnvironment("InviteEmail__FromDisplayName", "TalentSuite")
    .WithEnvironment("InviteEmail__SmtpHost", inviteSmtpHost)
    .WithEnvironment("InviteEmail__SmtpPort", inviteSmtpPort)
    .WithEnvironment("InviteEmail__SmtpEnableSsl", inviteSmtpEnableSsl)
    .WithEnvironment("InviteEmail__SmtpUsername", inviteSmtpUsername)
    .WithEnvironment("InviteEmail__SmtpPassword", inviteSmtpPassword)
    .WithEnvironment("GoogleDriveSync__Enabled", googleDriveSyncEnabled)
    .WithEnvironment("GoogleDriveSync__SourceContainerName", googleDriveSyncSourceContainerName)
    .WithEnvironment("GoogleDriveSync__DriveFolderId", googleDriveSyncDriveFolderId)
    .WithEnvironment("GoogleDriveSync__ServiceAccountJsonBase64", googleDriveSyncServiceAccountJsonBase64)
    .WithEnvironment(context =>
    {
        if (context.EnvironmentVariables.TryGetValue("ConnectionStrings__messaging", out var value)
            && value is not null)
        {
            context.EnvironmentVariables["AzureWebJobsServiceBus"] = value;
        }

        if (context.EnvironmentVariables.TryGetValue("ConnectionStrings__storage", out var storageValue)
            && storageValue is not null)
        {
            context.EnvironmentVariables["AzureWebJobsStorage"] = storageValue;
        }
    })
    .WaitFor(messaging)
    .WaitFor(server);

if (local)
{
    functions.WithEnvironment("InviteEmail__FrontendBaseUrl", "https://localhost:5173");
}

IResourceBuilder<AzureContainerAppEnvironmentResource>? privateAcaEnvironment = null;

if (local)
{
    msSql.RunAsContainer(opt =>
    {
        opt.WithImage("mssql/server:2022-latest")
           .WithImagePullPolicy(ImagePullPolicy.Always)
           .WithDataVolume("talentsuite-sql-data")
           .WithLifetime(ContainerLifetime.Persistent)
           .WithHostPort(14330)
           .WithIconName("DatabaseColor")
           .WithPassword(sqlPassword)
           .WithDbGate();
    });

    keycloak
        .WithEnvironment("KC_DB_URL", keycloakDb.Resource.JdbcConnectionString)
        .WithEnvironment("KC_DB_USERNAME", "sa")
        .WithEnvironment("KC_DB_PASSWORD", sqlPassword)
        .WaitFor(keycloakDb);

    server.WithReference(appDb)
        .WithReference(keycloak)
        .WithReference(messaging)
        .WithEnvironment("KEYCLOAK_HTTP", keycloakHttpEndpoint)
        .WithEnvironment("KEYCLOAK_AUTHORITY", ReferenceExpression.Create($"{keycloakHttpEndpoint}/realms/TalentConsulting"))
        .WithEnvironment("AUTHENTICATION_ENABLED", authenticationEnabled)
        .WithEnvironment("USE_IN_MEMORY_DATA", useInMemoryData)
        .WithEnvironment("AzureServiceBus__InviteUserEntityName", "invite-user")
        .WithEnvironment("AzureServiceBus__BidSubmittedEntityName", "bid-submitted")
        .WithEnvironment("AzureServiceBus__CommentSavedWithMentionsEntityName", "comment-saved-with-mentions")
        .WithEnvironment("KEYCLOAK_REALM", "TalentConsulting")
        .WithEnvironment("KEYCLOAK_ADMIN_REALM", "master")
        .WithEnvironment("KEYCLOAK_ADMIN_USERNAME", "admin")
        .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", keycloakPassword)
        .WithEnvironment("KEYCLOAK_ADMIN_CLIENT_ID", "admin-cli")
        .WaitFor(appDb)
        .WaitFor(keycloak);
}
else
{
    // No apps target this environment (everything runs in the private one below);
    // kept because removing it would tear down already-deployed infrastructure.
    _ = builder.AddAzureContainerAppEnvironment($"aca-{azureEnvironmentName}");

    var appInsights = builder.AddBicepTemplate("application-insights", "Infrastructure/application-insights.bicep");

    msSql.ConfigureInfrastructure(infra =>
    {
        var sqlServer = infra.GetProvisionableResources().OfType<SqlServer>().Single();
        sqlServer.AdministratorLogin = "sqladm72";
        sqlServer.AdministratorLoginPassword = sqlPassword.AsProvisioningParameter(infra);

        foreach (var database in infra.GetProvisionableResources().OfType<SqlDatabase>())
        {
            database.Sku = new SqlSku
            {
                Name = "GP_S_Gen5",
                Tier = "GeneralPurpose",
                Family = "Gen5",
                Capacity = 2
            };
            database.RequestedBackupStorageRedundancy = SqlBackupStorageRedundancy.Local;
            database.AutoPauseDelay = 60;
            database.MinCapacity = 0.5;
            database.UseFreeLimit = false;
        }

        if (sqlServer.Administrators is { } admin)
        {
            sqlServer.Administrators = new ServerExternalAdministrator
            {
                AdministratorType = admin.AdministratorType,
                Login = admin.Login,
                Sid = admin.Sid,
                TenantId = admin.TenantId,
                IsAzureADOnlyAuthenticationEnabled = false
            };
        }
    });

    _ = builder.AddBicepTemplate("sql-connection-policy", "Infrastructure/sql-connection-policy.bicep")
        .WithParameter("sqlServerName", msSql.Resource.NameOutputReference);

    var privateNetwork = builder.AddBicepTemplate("private-network", "Infrastructure/private-network.bicep")
        .WithParameter("sqlServerName", msSql.Resource.NameOutputReference);

    privateAcaEnvironment = builder.AddAzureContainerAppEnvironment($"aca-{azureEnvironmentName}-private")
        .ConfigureInfrastructure(infra =>
        {
            var containerAppEnvironment = infra.GetProvisionableResources()
                .OfType<ContainerAppManagedEnvironment>()
                .Single();

            containerAppEnvironment.VnetConfiguration = new ContainerAppVnetConfiguration
            {
                InfrastructureSubnetId = privateNetwork
                    .GetOutput("acaInfrastructureSubnetId")
                    .AsProvisioningParameter(infra, "acaInfrastructureSubnetId")
            };
        });

    var keycloakIdentity = builder.AddAzureUserAssignedIdentity("keycloak-identity");
    var talentserverIdentity = builder.AddAzureUserAssignedIdentity("talentserver-identity");
    var talentfunctionsIdentity = builder.AddAzureUserAssignedIdentity("talentfunctions-identity");

    keycloak
        .WithEnvironment("KC_DB_URL", keycloakDb.Resource.JdbcConnectionString)
        .WithEnvironment("KC_DB_USERNAME", keycloakDbUsername)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakPassword)
        .WithEnvironment("KC_DB_PASSWORD", keycloakDbPassword)
        .WithAzureUserAssignedIdentity(keycloakIdentity)
        .WithComputeEnvironment(privateAcaEnvironment)
        .WaitFor(keycloakDb);

    server
        .WithReference(appDb)
        .WithReference(keycloak)
        .WithReference(messaging)
        .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsights.GetOutput("applicationInsightsConnectionString"))
        .WithEnvironment("AUTHENTICATION_ENABLED", authenticationEnabled)
        .WithEnvironment("USE_IN_MEMORY_DATA", useInMemoryData)
        .WithEnvironment("AzureServiceBus__InviteUserEntityName", "invite-user")
        .WithEnvironment("AzureServiceBus__BidSubmittedEntityName", "bid-submitted")
        .WithEnvironment("AzureServiceBus__CommentSavedWithMentionsEntityName", "comment-saved-with-mentions")
        .WithEnvironment("KEYCLOAK_REALM", "TalentConsulting")
        .WithEnvironment("KEYCLOAK_ADMIN_REALM", "master")
        .WithEnvironment("KEYCLOAK_ADMIN_USERNAME", "admin")
        .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", keycloakPassword)
        .WithEnvironment("KEYCLOAK_ADMIN_CLIENT_ID", "admin-cli")
        .WithAzureUserAssignedIdentity(talentserverIdentity)
        .WithComputeEnvironment(privateAcaEnvironment)
        .WaitFor(appDb)
        .WaitFor(keycloak);

    functions
        .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsights.GetOutput("applicationInsightsConnectionString"))
        .WithAzureUserAssignedIdentity(talentfunctionsIdentity)
        .WithComputeEnvironment(privateAcaEnvironment);

    server.WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataSender);
    functions.WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataReceiver);
}

var grafana = builder.AddDockerfile("grafana", "../ops/grafana")
    .WithHttpEndpoint(targetPort: 3000, name: "http")
    .WithExternalHttpEndpoints()
    .WithEnvironment("GF_SERVER_HTTP_PORT", "3000")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
    .WithEnvironment("GF_USERS_DEFAULT_THEME", "system")
    .WithEnvironment("GF_AUTH_AZUREAD_ENABLED", grafanaEntraEnabled)
    .WithEnvironment("GF_AUTH_AZUREAD_NAME", "Microsoft Entra ID")
    .WithEnvironment("GF_AUTH_AZUREAD_CLIENT_ID", grafanaEntraClientId)
    .WithEnvironment("GF_AUTH_AZUREAD_CLIENT_SECRET", grafanaEntraClientSecret)
    .WithEnvironment("GF_AUTH_AZUREAD_ALLOWED_ORGANIZATIONS", grafanaEntraTenantId)
    .WithEnvironment("GF_AUTH_AZUREAD_ALLOW_SIGN_UP", "true")
    .WithEnvironment("GF_AUTH_AZUREAD_ALLOW_ASSIGN_GRAFANA_ADMIN", "true")
    .WithEnvironment("GF_AUTH_AZUREAD_AUTO_LOGIN", "false")
    .WithEnvironment("GF_AUTH_AZUREAD_USE_PKCE", "true")
    .WithEnvironment("GF_AUTH_AZUREAD_SCOPES", "openid email profile")
    .WithEnvironment("GF_AUTH_AZUREAD_CLIENT_AUTHENTICATION", "client_secret_post")
    .WithEnvironment("GF_AZURE_MANAGED_IDENTITY_ENABLED", "true")
    .WithEnvironment("GRAFANA_AZURE_MONITOR_SUBSCRIPTION_ID", grafanaAzureMonitorSubscriptionId)
    .WithEnvironment(context =>
    {
        var tenantId = context.EnvironmentVariables.TryGetValue("GF_AUTH_AZUREAD_ALLOWED_ORGANIZATIONS", out var value)
            ? value?.ToString()
            : null;
        tenantId = string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId;
        context.EnvironmentVariables["GF_AUTH_AZUREAD_AUTH_URL"] =
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize";
        context.EnvironmentVariables["GF_AUTH_AZUREAD_TOKEN_URL"] =
            $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
    });

if (local)
{
    var grafanaHttpEndpoint = grafana.GetEndpoint("http");
    grafana
        .WithEnvironment("GF_SERVER_ROOT_URL", grafanaHttpEndpoint)
        .WithEnvironment("GF_SERVER_DOMAIN", grafanaHttpEndpoint.Property(EndpointProperty.Host));
}
else
{
    grafana
        .WithEnvironment("GF_SERVER_ROOT_URL", grafanaPublicOrigin)
        .WithEnvironment(context =>
        {
            if (context.EnvironmentVariables.TryGetValue("GF_SERVER_ROOT_URL", out var value)
                && Uri.TryCreate(value?.ToString(), UriKind.Absolute, out var publicUri))
            {
                context.EnvironmentVariables["GF_SERVER_DOMAIN"] = publicUri.Authority;
                context.EnvironmentVariables["GF_SECURITY_CSRF_TRUSTED_ORIGINS"] = publicUri.GetLeftPart(UriPartial.Authority);
            }

            context.EnvironmentVariables["GF_SECURITY_COOKIE_SECURE"] = "true";
            context.EnvironmentVariables["GF_SECURITY_COOKIE_SAMESITE"] = "lax";
            context.EnvironmentVariables["GF_SECURITY_CSRF_ADDITIONAL_HEADERS"] = "X-Forwarded-Host";
        })
        .WithComputeEnvironment(privateAcaEnvironment!)
        .PublishAsAzureContainerApp((_, app) =>
        {
            app.Configuration ??= new();
            app.Configuration.Ingress ??= new();
            app.Configuration.Ingress.External = true;
            app.Configuration.Ingress.TargetPort = 3000;
            app.Template ??= new();
            app.Template.Scale ??= new ContainerAppScale();
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 1;
        });
}

if (local)
{
    builder.AddProject<TalentSuite_FrontEnd>("talentfrontend")
        .WithEnvironment("AUTHENTICATION_ENABLED", authenticationEnabled)
        .WithEnvironment("USE_IN_MEMORY_DATA", useInMemoryData)
        .WithEnvironment("KEYCLOAK_HTTP", keycloakHttpEndpoint)
        .WithEnvironment("KEYCLOAK_AUTHORITY", ReferenceExpression.Create($"{keycloakHttpEndpoint}/realms/TalentConsulting"))
        .WithReference(keycloak)
        .WithReference(server)
        .WaitFor(keycloak)
        .WaitFor(server);
}

builder.Build().Run();

static Dictionary<string, string> BuildResourceTags(
    string environmentName,
    string? azdInfraTagsJson,
    string? azureResourceTagsJson)
{
    var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["project"] = "talentsuite",
        ["Owner"] = "rgparkins",
        ["azd-env-name"] = environmentName
    };

    MergeTagsFromJson(azdInfraTagsJson, tags);
    MergeTagsFromJson(azureResourceTagsJson, tags);

    return tags;
}

static void MergeTagsFromJson(string? rawJson, IDictionary<string, string> tags)
{
    if (string.IsNullOrWhiteSpace(rawJson))
    {
        return;
    }

    try
    {
        using var document = JsonDocument.Parse(rawJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                tags[property.Name] = property.Value.ToString();
            }
        }
    }
    catch (JsonException)
    {
        // Keep default tags if an override payload is malformed.
    }
}

internal sealed class ApplyTagsResolver(IReadOnlyDictionary<string, string> tags) : InfrastructureResolver
{
    public override void ResolveProperties(ProvisionableConstruct construct, ProvisioningBuildOptions options)
    {
        base.ResolveProperties(construct, options);

        if (construct is not ProvisionableResource { IsExistingResource: false })
        {
            return;
        }

        var property = construct.GetType().GetProperty("Tags");
        if (property?.GetValue(construct) is not BicepDictionary<string> constructTags)
        {
            return;
        }

        if (constructTags is IBicepValue { Kind: BicepValueKind.Expression } or IBicepValue { IsOutput: true })
        {
            // Tags bound to an expression (e.g. an empty module parameter) can't be
            // mutated in place — replace the whole dictionary with literal values.
            constructTags = [];
            property.SetValue(construct, constructTags);
        }

        foreach (var (key, value) in tags)
        {
            constructTags[key] = value;
        }
    }
}
