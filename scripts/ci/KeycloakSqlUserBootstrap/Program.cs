using Microsoft.Data.SqlClient;

var arguments = ParseArgs(args);

var server = Required(arguments, "--server");
var adminUser = Required(arguments, "--admin-user");
var adminPassword = Required(arguments, "--admin-password");
var appUser = Required(arguments, "--app-user");
var appPassword = Required(arguments, "--app-password");
var database = Required(arguments, "--database");

await EnsureLoginAsync(server, adminUser, adminPassword, appUser, appPassword);
await EnsureDatabaseUserAsync(server, adminUser, adminPassword, database, appUser);

Console.WriteLine($"Ensured SQL login '{appUser}' and database user in '{database}'.");

static Dictionary<string, string> ParseArgs(string[] args)
{
    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for argument '{args[i]}'.");
        }

        parsed[args[i]] = args[i + 1];
    }

    return parsed;
}

static string Required(IReadOnlyDictionary<string, string> arguments, string name)
{
    if (arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    throw new ArgumentException($"Missing required argument '{name}'.");
}

static string BuildConnectionString(string server, string database, string user, string password)
{
    var builder = new SqlConnectionStringBuilder
    {
        DataSource = server,
        InitialCatalog = database,
        UserID = user,
        Password = password,
        Encrypt = true,
        TrustServerCertificate = false,
        ConnectTimeout = 30
    };

    return builder.ConnectionString;
}

static async Task EnsureLoginAsync(
    string server,
    string adminUser,
    string adminPassword,
    string appUser,
    string appPassword)
{
    await using var connection = new SqlConnection(BuildConnectionString(server, "master", adminUser, adminPassword));
    await connection.OpenAsync();

    const string sql = """
        DECLARE @loginName sysname = @appUser;
        DECLARE @loginPassword nvarchar(256) = @appPassword;
        DECLARE @escapedPassword nvarchar(514) = REPLACE(@loginPassword, '''', '''''');

        IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = @loginName)
        BEGIN
            DECLARE @createSql nvarchar(max) =
                N'CREATE LOGIN ' + QUOTENAME(@loginName) + N' WITH PASSWORD = '''
                + @escapedPassword + N''';';
            EXEC (@createSql);
        END
        ELSE
        BEGIN
            DECLARE @alterSql nvarchar(max) =
                N'ALTER LOGIN ' + QUOTENAME(@loginName) + N' WITH PASSWORD = '''
                + @escapedPassword + N''';';
            EXEC (@alterSql);
        END
        """;

    await using var command = new SqlCommand(sql, connection);
    command.Parameters.AddWithValue("@appUser", appUser);
    command.Parameters.AddWithValue("@appPassword", appPassword);
    await command.ExecuteNonQueryAsync();
}

static async Task EnsureDatabaseUserAsync(
    string server,
    string adminUser,
    string adminPassword,
    string database,
    string appUser)
{
    await using var connection = new SqlConnection(BuildConnectionString(server, database, adminUser, adminPassword));
    await connection.OpenAsync();

    const string sql = """
        DECLARE @userName sysname = @appUser;

        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @userName)
        BEGIN
            DECLARE @createUserSql nvarchar(max) =
                N'CREATE USER ' + QUOTENAME(@userName) + N' FOR LOGIN ' + QUOTENAME(@userName) + N';';
            EXEC (@createUserSql);
        END

        IF NOT EXISTS (
            SELECT 1
            FROM sys.database_role_members drm
            JOIN sys.database_principals rolePrincipal
                ON rolePrincipal.principal_id = drm.role_principal_id
            JOIN sys.database_principals memberPrincipal
                ON memberPrincipal.principal_id = drm.member_principal_id
            WHERE rolePrincipal.name = N'db_owner'
              AND memberPrincipal.name = @userName)
        BEGIN
            DECLARE @grantRoleSql nvarchar(max) =
                N'ALTER ROLE [db_owner] ADD MEMBER ' + QUOTENAME(@userName) + N';';
            EXEC (@grantRoleSql);
        END
        """;

    await using var command = new SqlCommand(sql, connection);
    command.Parameters.AddWithValue("@appUser", appUser);
    await command.ExecuteNonQueryAsync();
}
