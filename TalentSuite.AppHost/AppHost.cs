using Projects;
using Azure.Provisioning.ServiceBus;
using Azure.Provisioning.Sql;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Resources;
using Azure.Provisioning.Roles;
using Azure.Provisioning.Storage;
using Azure.Provisioning.OperationalInsights;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Aspire.Hosting.Publishing;
using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;
using System.Text.Json;

var builder = DistributedApplication.CreateBuilder(args);

var local = builder.ExecutionContext.IsRunMode;
var azureEnvironmentName = builder.Configuration["AZURE_ENV_NAME"] ?? "dev";
var resourceTags = BuildResourceTags(
    azureEnvironmentName,
    builder.Configuration["AZD_INFRA_TAGS"],
    builder.Configuration["AZURE_RESOURCE_TAGS"]);

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

static BicepDictionary<string> ToBicepTags(IReadOnlyDictionary<string, string> tags)
{
    var bicepTags = new BicepDictionary<string>();
    foreach (var pair in tags)
    {
        bicepTags[pair.Key] = pair.Value;
    }

    return bicepTags;
}

static string GetLeadingWhitespace(string line)
{
    var index = 0;
    while (index < line.Length && char.IsWhiteSpace(line[index]))
    {
        index++;
    }

    return line.Substring(0, index);
}

static bool IsBareBicepIdentifier(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return false;
    }

    var first = value[0];
    if (!(char.IsLetter(first) || first == '_'))
    {
        return false;
    }

    for (var i = 1; i < value.Length; i++)
    {
        var ch = value[i];
        if (!(char.IsLetterOrDigit(ch) || ch == '_'))
        {
            return false;
        }
    }

    return true;
}

static string EscapeBicepString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

static string FormatBicepTagKey(string key)
{
    if (IsBareBicepIdentifier(key))
    {
        return key;
    }

    return $"'{EscapeBicepString(key)}'";
}

static string AddTagsToDeploymentScripts(string bicepContent, IReadOnlyDictionary<string, string> tags)
{
    var newline = bicepContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    var lines = bicepContent.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
    var changed = false;

    for (var i = 0; i < lines.Count; i++)
    {
        if (!lines[i].Contains("Microsoft.Resources/deploymentScripts@", StringComparison.Ordinal))
        {
            continue;
        }

        var resourceIndent = GetLeadingWhitespace(lines[i]);
        var hasTags = false;
        var identityIndex = -1;

        for (var j = i + 1; j < lines.Count; j++)
        {
            var trimmed = lines[j].TrimStart();
            var lineIndent = GetLeadingWhitespace(lines[j]);

            if (trimmed.StartsWith("resource ", StringComparison.Ordinal) && lineIndent.Length <= resourceIndent.Length)
            {
                break;
            }

            if (trimmed.StartsWith("tags:", StringComparison.Ordinal))
            {
                hasTags = true;
            }

            if (trimmed.StartsWith("identity:", StringComparison.Ordinal))
            {
                identityIndex = j;
                break;
            }
        }

        if (hasTags || identityIndex < 0)
        {
            continue;
        }

        var indent = GetLeadingWhitespace(lines[identityIndex]);
        var insertion = new List<string>
        {
            $"{indent}tags: {{"
        };

        foreach (var tag in tags)
        {
            insertion.Add($"{indent}  {FormatBicepTagKey(tag.Key)}: '{EscapeBicepString(tag.Value)}'");
        }

        insertion.Add($"{indent}}}");

        lines.InsertRange(identityIndex, insertion);
        changed = true;
        i = identityIndex + insertion.Count;
    }

    return changed ? string.Join(newline, lines) : bicepContent;
}

var keycloakPassword = builder.AddParameter(
                                "KeycloakPassword",
                                value: "admin",
                                secret: true,
                                publishValueAsDefault: false);
// var keycloakPasswordPlaceholder = builder.AddParameter(
//                                 "KeycloakPasswordPlaceholder",
//                                 value: "placeholder-keycloak-admin-password",
//                                 secret: false,
//                                 publishValueAsDefault: true);
var sqlPassword = builder.AddParameter(
                                "SqlPassword",
                                value: "Your_strong_password123!",
                                secret: true,
                                publishValueAsDefault: false);
var keycloakDbUsername = builder.AddParameter(
                                "KeycloakDbUsername",
                                secret: false,
                                value: "",
                                publishValueAsDefault: true);
var keycloakDbPassword = builder.AddParameter(
                                "KeycloakDbPassword",
                                value: "unused",
                                secret: true,
                                publishValueAsDefault: false);
// var keycloakDbPasswordPlaceholder = builder.AddParameter(
//                                 "KeycloakDbPasswordPlaceholder",
//                                 value: "placeholder-keycloak-db-password",
//                                 secret: false,
//                                 publishValueAsDefault: true);
var authenticationEnabled = builder.AddParameter(
                                "AuthenticationEnabled",
                                value: "true",
                                secret: false,
                                publishValueAsDefault: true);
var useInMemoryData = builder.AddParameter(
                                "UseInMemoryData",
                                value: "false",
                                secret: false,
                                publishValueAsDefault: true);
var inviteEmailEnabled = builder.AddParameter(
                                "InviteEmailEnabled",
                                value: "false",
                                secret: false,
                                publishValueAsDefault: true);
var inviteFrontendBaseUrl = builder.AddParameter(
                                "InviteFrontendBaseUrl",
                                value: "",
                                secret: false,
                                publishValueAsDefault: true);
var inviteFromEmail = builder.AddParameter(
                                "InviteFromEmail",
                                value: "",
                                secret: false,
                                publishValueAsDefault: true);
var inviteSmtpHost = builder.AddParameter(
                                "InviteSmtpHost",
                                value: "",
                                secret: false,
                                publishValueAsDefault: true);
var inviteSmtpPort = builder.AddParameter(
                                "InviteSmtpPort",
                                value: "587",
                                secret: false,
                                publishValueAsDefault: true);
var inviteSmtpEnableSsl = builder.AddParameter(
                                "InviteSmtpEnableSsl",
                                value: "true",
                                secret: false,
                                publishValueAsDefault: true);
var inviteSmtpUsername = builder.AddParameter(
                                "InviteSmtpUsername",
                                value: "",
                                secret: false,
                                publishValueAsDefault: true);
var inviteSmtpPassword = builder.AddParameter(
                                "InviteSmtpPassword",
                                value: "unused",
                                secret: true,
                                publishValueAsDefault: false);
var googleDriveSyncEnabled = builder.AddParameter(
                                "GoogleDriveSyncEnabled",
                                value: "false",
                                secret: false,
                                publishValueAsDefault: true);
var googleDriveSyncSourceContainerName = builder.AddParameter(
                                "GoogleDriveSyncSourceContainerName",
                                value: "bidlibrary",
                                secret: false,
                                publishValueAsDefault: true);
var googleDriveSyncDriveFolderId = builder.AddParameter(
                                "GoogleDriveSyncDriveFolderId",
                                value: "",
                                secret: false,
                                publishValueAsDefault: true);
var googleDriveSyncServiceAccountJsonBase64 = builder.AddParameter(
                                "GoogleDriveSyncServiceAccountJsonBase64",
                                value: "",
                                secret: true,
                                publishValueAsDefault: false);
var grafanaEntraEnabled = builder.AddParameter(
                                "GrafanaEntraEnabled",
                                value: "false",
                                secret: false,
                                publishValueAsDefault: true);
var grafanaEntraClientId = builder.AddParameter(
                                "GrafanaEntraClientId",
                                value: "",
                                secret: false,
                                publishValueAsDefault: true);
var grafanaEntraTenantId = builder.AddParameter(
                                "GrafanaEntraTenantId",
                                value: "",
                                secret: false,
                                publishValueAsDefault: true);
var grafanaEntraClientSecret = builder.AddParameter(
                                "GrafanaEntraClientSecret",
                                value: "",
                                secret: true,
                                publishValueAsDefault: false);
var grafanaPublicOrigin = builder.AddParameter(
                                "GrafanaPublicOrigin",
                                value: "https://grafana-dev.talentsuite.uk",
                                secret: false,
                                publishValueAsDefault: true);
var grafanaAzureMonitorSubscriptionId = builder.AddParameter(
                                "GrafanaAzureMonitorSubscriptionId",
                                value: "",
                                secret: false,
                                publishValueAsDefault: true);

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
        .PublishAsAzureContainerApp((infra, app) =>
        {
            app.Tags = ToBicepTags(resourceTags);
            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
            {
                identity.Tags = ToBicepTags(resourceTags);
            }
            app.Template ??= new();
            app.Template.Scale ??= new ContainerAppScale();
            app.Template.Scale.MinReplicas = 1;
            app.Template.Scale.MaxReplicas = 1;
        });
}
var messaging = builder.AddAzureServiceBus("messaging")
    .ConfigureInfrastructure(infra =>
    {
        foreach (var ns in infra.GetProvisionableResources().OfType<ServiceBusNamespace>())
            ns.Tags = ToBicepTags(resourceTags);
    });
if (local)
{
    messaging.RunAsEmulator();
}

messaging.AddServiceBusQueue("invite-user");
messaging.AddServiceBusQueue("bid-submitted");
messaging.AddServiceBusQueue("comment-saved-with-mentions");
var storage = builder.AddAzureStorage("storage")
    .ConfigureInfrastructure(infra =>
    {
        foreach (var sa in infra.GetProvisionableResources().OfType<StorageAccount>())
            sa.Tags = ToBicepTags(resourceTags);
    });
if (local)
{
    storage.RunAsEmulator(emulator => emulator
        .WithDataVolume("talentsuite-azurite-data", isReadOnly: false));
}

var bidStorage = local
    ? storage.AddBlobs("bidstorage")
    : builder.AddAzureStorage("bidcontentstorage")
        .ConfigureInfrastructure(infra =>
        {
            foreach (var sa in infra.GetProvisionableResources().OfType<StorageAccount>())
                sa.Tags = ToBicepTags(resourceTags);
        })
        .AddBlobs("bidstorage");

#region MsSqlServer

var msSql = builder.AddAzureSqlServer("sql");

var appDb = msSql.AddDatabase("talentconsultingdb");
var keycloakDb = msSql.AddDatabase("keycloakdb");

#endregion

#region Apps

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

functions.WithEnvironment("GoogleDriveSync__ServiceAccountJsonBase64", googleDriveSyncServiceAccountJsonBase64);

//if (!useLocalInfrastructure)
//{
//    functions.WithComputeEnvironment(privateAcaEnvironment!);
//}

#endregion

//IResourceBuilder<ProjectResource> server;
//IResourceBuilder<AzureSqlServerResource>? sql = null;
IResourceBuilder<AzureContainerAppEnvironmentResource>? defaultAcaEnvironment = null;
IResourceBuilder<AzureContainerAppEnvironmentResource>? privateAcaEnvironment = null;
if (local)
{
    // var localSql = builder.AddSqlServer("sql", password: sqlPassword, port: 14330)
    //     .WithDataVolume("talentsuite-sql-data", isReadOnly: false);
    // var appDb = localSql.AddDatabase("talentconsultingdb");
    // var keycloakDb = localSql.AddDatabase("keycloakdb");

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
        //"jdbc:sqlserver://sql:1433;databaseName=keycloakdb;encrypt=false;trustServerCertificate=true")
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
    // var logAnalytics = builder.AddAzureLogAnalyticsWorkspace("log-analytics");
    // var appInsights = builder.AddAzureApplicationInsights("talentbidmanager-insights")
    //     .WithLogAnalyticsWorkspace(logAnalytics);

    defaultAcaEnvironment = builder.AddAzureContainerAppEnvironment($"aca-{azureEnvironmentName}")
        .ConfigureInfrastructure(infra =>
        {
            foreach (var env in infra.GetProvisionableResources().OfType<ContainerAppManagedEnvironment>())
                env.Tags = ToBicepTags(resourceTags);

            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
                identity.Tags = ToBicepTags(resourceTags);
        });
    var appInsights = builder.AddBicepTemplate("application-insights", "Infrastructure/application-insights.bicep");



    //sql = builder.AddAzureSqlServer("sql")
    msSql
        .ConfigureInfrastructure(infra =>
        {
            var server = infra.GetProvisionableResources().OfType<SqlServer>().Single();
            server.AdministratorLogin = "sqladm72";
            server.AdministratorLoginPassword = sqlPassword.AsProvisioningParameter(infra);
            server.Tags = ToBicepTags(resourceTags);

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
                database.Tags = ToBicepTags(resourceTags);
            }

            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
            {
                identity.Tags = ToBicepTags(resourceTags);
            }

            foreach (var script in infra.GetProvisionableResources().OfType<ArmDeploymentScript>())
            {
                script.Tags = ToBicepTags(resourceTags);
            }

            if (server.Administrators is { } admin)
            {
                server.Administrators = new ServerExternalAdministrator
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
        //.WithAzureLogAnalyticsWorkspace(logAnalytics)
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
            containerAppEnvironment.Tags = ToBicepTags(resourceTags);

            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
                identity.Tags = ToBicepTags(resourceTags);
        });

    var keycloakIdentity = builder.AddAzureUserAssignedIdentity("keycloak-identity")
        .ConfigureInfrastructure(infra =>
        {
            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
                identity.Tags = ToBicepTags(resourceTags);
        });

    var talentserverIdentity = builder.AddAzureUserAssignedIdentity("talentserver-identity")
        .ConfigureInfrastructure(infra =>
        {
            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
                identity.Tags = ToBicepTags(resourceTags);
        });

    var talentfunctionsIdentity = builder.AddAzureUserAssignedIdentity("talentfunctions-identity")
        .ConfigureInfrastructure(infra =>
        {
            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
                identity.Tags = ToBicepTags(resourceTags);
        });

    foreach (var registryResource in builder.Resources.OfType<AzureContainerRegistryResource>())
    {
        builder.CreateResourceBuilder(registryResource).ConfigureInfrastructure(infra =>
        {
            foreach (var registry in infra.GetProvisionableResources().OfType<ContainerRegistryService>())
            {
                registry.Tags = ToBicepTags(resourceTags);
            }
        });
    }

    // var appDb = sql.AddDatabase("talentconsultingdb");
    // var keycloakDb = sql.AddDatabase("keycloakdb");

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
        .PublishAsAzureContainerApp((infra, app) =>
        {
            app.Tags = ToBicepTags(resourceTags);
            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
            {
                identity.Tags = ToBicepTags(resourceTags);
            }
        })
        .WaitFor(appDb)
        .WaitFor(keycloak);

    functions
        .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", appInsights.GetOutput("applicationInsightsConnectionString"))
        .WithAzureUserAssignedIdentity(talentfunctionsIdentity)
        .WithComputeEnvironment(privateAcaEnvironment!)
        .PublishAsAzureContainerApp((infra, app) =>
        {
            app.Tags = ToBicepTags(resourceTags);
            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
            {
                identity.Tags = ToBicepTags(resourceTags);
            }
        });
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
    .WithEnvironment("GRAFANA_AZURE_MONITOR_SUBSCRIPTION_ID", grafanaAzureMonitorSubscriptionId);
var grafanaHttpEndpoint = grafana.GetEndpoint("http");

if (local)
{
    grafana
        .WithEnvironment("GF_SERVER_ROOT_URL", grafanaHttpEndpoint)
        .WithEnvironment("GF_SERVER_DOMAIN", grafanaHttpEndpoint.Property(EndpointProperty.Host));
    grafana.WithEnvironment(context =>
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
        })
        .WithComputeEnvironment(privateAcaEnvironment!)
        .PublishAsAzureContainerApp((infra, app) =>
        {
            app.Tags = ToBicepTags(resourceTags);
            foreach (var identity in infra.GetProvisionableResources().OfType<UserAssignedIdentity>())
            {
                identity.Tags = ToBicepTags(resourceTags);
            }
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

if (!local)
{
    // Ensure generated deployment scripts and log analytics workspaces also receive
    // policy-compliant tags.
    builder.Eventing.Subscribe<BeforeStartEvent>((evt, ct) =>
    {
        foreach (var resource in evt.Model.Resources.OfType<AzureProvisioningResource>())
        {
            builder.CreateResourceBuilder(resource).ConfigureInfrastructure(infra =>
            {
                foreach (var script in infra.GetProvisionableResources().OfType<ArmDeploymentScript>())
                {
                    script.Tags = ToBicepTags(resourceTags);
                }

                foreach (var workspace in infra.GetProvisionableResources().OfType<OperationalInsightsWorkspace>())
                {
                    workspace.Tags = ToBicepTags(resourceTags);
                }
            });
        }

        return Task.CompletedTask;
    });

    builder.OnAfterPublish(async (evt, ct) =>
    {
        var candidateRoots = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "TalentSuite.AppHost", "aspire-output"),
            Path.Combine(Directory.GetCurrentDirectory(), "aspire-output")
        };

        string? outputRoot = null;
        foreach (var candidate in candidateRoots)
        {
            if (Directory.Exists(candidate))
            {
                outputRoot = candidate;
                break;
            }
        }

        if (outputRoot is null)
        {
            return;
        }

        foreach (var bicepFile in Directory.EnumerateFiles(outputRoot, "*-roles-sql.bicep", SearchOption.AllDirectories))
        {
            var content = await File.ReadAllTextAsync(bicepFile, ct);
            var updated = AddTagsToDeploymentScripts(content, resourceTags);

            if (!string.Equals(content, updated, StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(bicepFile, updated, ct);
            }
        }
    });

    server.WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataSender);
    functions.WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataReceiver);
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

