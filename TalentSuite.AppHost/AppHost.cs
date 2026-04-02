using Projects;
using Azure.Provisioning.ServiceBus;
using Azure.Provisioning.Sql;
using Azure.Provisioning.AppContainers;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Azure.AppContainers;
using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.Expressions;

var builder = DistributedApplication.CreateBuilder(args);
const string InfrastructureModeVariable = "TALENTSUITE_INFRA_MODE";
const string ForceAzureInfrastructureVariable = "TALENTSUITE_FORCE_AZURE_INFRA";
var infrastructureMode = Environment.GetEnvironmentVariable(InfrastructureModeVariable)
                         ?? "local";
var forceAzureInfrastructure = string.Equals(
    Environment.GetEnvironmentVariable(ForceAzureInfrastructureVariable),
    "true",
    StringComparison.OrdinalIgnoreCase);
var useLocalInfrastructure = !forceAzureInfrastructure
    && string.Equals(infrastructureMode, "local", StringComparison.OrdinalIgnoreCase);

var keycloakPassword = builder.AddParameter(
                                "KeycloakPassword",
                                value: "admin",
                                secret: true,
                                publishValueAsDefault: false);
var keycloakPasswordPlaceholder = builder.AddParameter(
                                "KeycloakPasswordPlaceholder",
                                value: "placeholder-keycloak-admin-password",
                                secret: false,
                                publishValueAsDefault: true);
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
var keycloakDbPasswordPlaceholder = builder.AddParameter(
                                "KeycloakDbPasswordPlaceholder",
                                value: "placeholder-keycloak-db-password",
                                secret: false,
                                publishValueAsDefault: true);
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
var keycloakContainerAdminPassword = keycloakPassword;
var keycloakContainerDbPassword = keycloakDbPassword;

var keycloak = builder.AddKeycloak(
            "keycloak",
            adminPassword: keycloakContainerAdminPassword,
            port: useLocalInfrastructure ? null : 80)
    .WithEnvironment("KC_DB", "mssql");
var keycloakHttpEndpoint = keycloak.Resource.GetEndpoint("http");

if (useLocalInfrastructure)
{
    keycloak.WithRealmImport("./keycloak/realms");
}
else
{
    keycloak
        .WithEndpoint("http", endpoint => endpoint.IsExternal = true, createIfNotExists: false)
        .WithArgs("--proxy-headers=xforwarded")
        .WithArgs("--http-enabled=true")
        .WithArgs("--hostname-strict=false");
}
var messaging = builder.AddAzureServiceBus("messaging");
if (useLocalInfrastructure)
{
    messaging.RunAsEmulator();
}

messaging.AddServiceBusQueue("invite-user");
messaging.AddServiceBusQueue("bid-submitted");
messaging.AddServiceBusQueue("comment-saved-with-mentions");
var storage = builder.AddAzureStorage("storage");
if (useLocalInfrastructure)
{
    storage.RunAsEmulator(emulator => emulator
        .WithDataVolume("talentsuite-azurite-data", isReadOnly: false));
}

var bidStorage = useLocalInfrastructure
    ? storage.AddBlobs("bidstorage")
    : builder.AddAzureStorage("bidcontentstorage").AddBlobs("bidstorage");
IResourceBuilder<ProjectResource> server;
IResourceBuilder<AzureSqlServerResource>? sql = null;
IResourceBuilder<AzureContainerAppEnvironmentResource>? defaultAcaEnvironment = null;
IResourceBuilder<AzureContainerAppEnvironmentResource>? privateAcaEnvironment = null;
if (useLocalInfrastructure)
{
    var localSql = builder.AddSqlServer("sql", password: sqlPassword, port: 14330)
        .WithDataVolume("talentsuite-sql-data", isReadOnly: false);
    var appDb = localSql.AddDatabase("talentconsultingdb");
    var keycloakDb = localSql.AddDatabase("keycloakdb");

    keycloak
        .WithEnvironment("KC_DB_URL",
            "jdbc:sqlserver://sql:1433;databaseName=keycloakdb;encrypt=false;trustServerCertificate=true")
        .WithEnvironment("KC_DB_USERNAME", "sa")
        .WithEnvironment("KC_DB_PASSWORD", sqlPassword)
        .WaitFor(keycloakDb);

    server = builder.AddProject<TalentSuite_Server>("talentserver")
        .WithReference(appDb)
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
    defaultAcaEnvironment = builder.AddAzureContainerAppEnvironment("aca-dev");

    sql = builder.AddAzureSqlServer("sql")
        .ConfigureInfrastructure(infra =>
        {
            var server = infra.GetProvisionableResources().OfType<SqlServer>().Single();
            server.AdministratorLogin = "sqladm72";
            server.AdministratorLoginPassword = sqlPassword.AsProvisioningParameter(infra);

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
    var privateNetwork = builder.AddBicepTemplate("private-network", "Infrastructure/private-network.bicep")
        .WithParameter("sqlServerName", sql.Resource.NameOutputReference);
    privateAcaEnvironment = builder.AddAzureContainerAppEnvironment("aca-dev-private")
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
    var appDb = sql.AddDatabase("talentconsultingdb");
    var keycloakDb = sql.AddDatabase("keycloakdb");

    keycloak
        .WithEnvironment("KC_DB_URL", keycloakDb.Resource.JdbcConnectionString)
        .WithEnvironment("KC_DB_USERNAME", keycloakDbUsername)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakContainerAdminPassword)
        .WithEnvironment("KC_DB_PASSWORD", keycloakContainerDbPassword)
        .WithComputeEnvironment(privateAcaEnvironment)
        .PublishAsAzureContainerApp((infra, app) =>
        {
            var privateEnvironmentName = privateAcaEnvironment!.Resource.NameOutputReference
                .AsProvisioningParameter(infra, "privateContainerAppsEnvironmentName");
            app.EnvironmentId = new BicepValue<ResourceIdentifier>(
                (BicepExpression)BicepFunction.Interpolate(
                    $"resourceId('Microsoft.App/managedEnvironments', {privateEnvironmentName})"));
        })
        .WaitFor(keycloakDb);
    
    server = builder.AddProject<TalentSuite_Server>("talentserver")
        .WithReference(appDb)
        .WithReference(keycloak)
        .WithReference(messaging)
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
        .WithComputeEnvironment(privateAcaEnvironment)
        .PublishAsAzureContainerApp((infra, app) =>
        {
            var privateEnvironmentName = privateAcaEnvironment!.Resource.NameOutputReference
                .AsProvisioningParameter(infra, "privateContainerAppsEnvironmentName");
            app.EnvironmentId = new BicepValue<ResourceIdentifier>(
                (BicepExpression)BicepFunction.Interpolate(
                    $"resourceId('Microsoft.App/managedEnvironments', {privateEnvironmentName})"));
        })
        .WaitFor(appDb)
        .WaitFor(keycloak);
}

var functions = builder.AddProject<TalentSuite_Functions>("talentfunctions")
    .WithReference(server)
    .WithReference(bidStorage)
    .WithEnvironment("WEBSITES_PORT", "8080")
    .WithEnvironment("ASPNETCORE_URLS", "http://+:8080")
    .WithEnvironment("InviteEmail__Enabled", inviteEmailEnabled)
    .WithEnvironment("InviteEmail__FrontendBaseUrl", "https://localhost:5173")
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
    .WaitFor(messaging)
    .WaitFor(server);

if (!useLocalInfrastructure)
{
    functions.WithComputeEnvironment(defaultAcaEnvironment!);
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

if (useLocalInfrastructure)
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
        .WithComputeEnvironment(privateAcaEnvironment)
        .PublishAsAzureContainerApp((infra, app) =>
        {
            var privateEnvironmentName = privateAcaEnvironment!.Resource.NameOutputReference
                .AsProvisioningParameter(infra, "privateContainerAppsEnvironmentName");
            app.EnvironmentId = new BicepValue<ResourceIdentifier>(
                (BicepExpression)BicepFunction.Interpolate(
                    $"resourceId('Microsoft.App/managedEnvironments', {privateEnvironmentName})"));
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

if (!useLocalInfrastructure)
{
    server.WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataSender);
    functions.WithRoleAssignments(messaging, ServiceBusBuiltInRole.AzureServiceBusDataReceiver);
}

if (useLocalInfrastructure)
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
