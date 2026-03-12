using Projects;
using Azure.Provisioning.Sql;

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
var keycloakContainerAdminPassword = useLocalInfrastructure
    ? keycloakPassword
    : keycloakPasswordPlaceholder;
var keycloakContainerDbPassword = useLocalInfrastructure
    ? keycloakDbPassword
    : keycloakDbPasswordPlaceholder;

var keycloak = builder.AddKeycloak(
            "keycloak",
            adminPassword: keycloakContainerAdminPassword,
            port: 8080)
    .WithEnvironment("KC_DB", "mssql")
    .WithRealmImport("./keycloak/realms");
if (!useLocalInfrastructure)
{
    keycloak
        .WithEndpoint("http", endpoint => endpoint.IsExternal = true, createIfNotExists: false)
        .WithArgs("--http-enabled=true")
        .WithArgs("--proxy-headers=xforwarded")
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

var bidStorage = storage.AddBlobs("bidstorage");
IResourceBuilder<ProjectResource> server;
if (useLocalInfrastructure)
{
    var sql = builder.AddSqlServer("sql", password: sqlPassword, port: 14330)
        .WithDataVolume("talentsuite-sql-data", isReadOnly: false);
    var appDb = sql.AddDatabase("talentconsultingdb");
    var keycloakDb = sql.AddDatabase("keycloakdb");

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
        .WithReference(bidStorage)
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
    var sql = builder.AddAzureSqlServer("sql")
        .ConfigureInfrastructure(infra =>
        {
            foreach (var server in infra.GetProvisionableResources()
                         .OfType<SqlServer>())
            {
                server.AdministratorLogin = "rgparkins";
                server.AdministratorLoginPassword = sqlPassword.AsProvisioningParameter(infra);
            }

            foreach (var adOnly in infra.GetProvisionableResources()
                         .OfType<SqlServerAzureADOnlyAuthentication>())
            {
                adOnly.IsAzureADOnlyAuthenticationEnabled = false;
            }
        });
    
    var appDb = sql.AddDatabase("talentconsultingdb");
    var keycloakDb = sql.AddDatabase("keycloakdb");

    keycloak
        .WithEnvironment("KC_DB_URL", keycloakDb.Resource.JdbcConnectionString)
        .WithEnvironment("KC_DB_USERNAME", keycloakDbUsername)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakContainerAdminPassword)
        .WithEnvironment("KC_DB_PASSWORD", keycloakContainerDbPassword)
        .WaitFor(keycloakDb);
    
    server = builder.AddProject<TalentSuite_Server>("talentserver")
        .WithReference(appDb)
        .WithReference(keycloak)
        .WithReference(messaging)
        .WithReference(bidStorage)
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

builder.AddProject<TalentSuite_Functions>("talentfunctions")
    .WithReference(messaging)
    .WithReference(server)
    .WithReference(bidStorage)
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

if (useLocalInfrastructure)
{
    builder.AddProject<TalentSuite_FrontEnd>("talentfrontend")
        .WithEnvironment("AUTHENTICATION_ENABLED", authenticationEnabled)
        .WithEnvironment("USE_IN_MEMORY_DATA", useInMemoryData)
        .WithReference(keycloak)
        .WithReference(server)
        .WaitFor(keycloak)
        .WaitFor(server);
}

builder.Build().Run();
