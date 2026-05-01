using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using TalentSuite.Server.Users.Services.DataModels;

namespace TalentSuite.Server.Users.Data;

public sealed class SqlServerUserRepository : IManageUsers
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static bool _schemaInitialized;
    private static readonly SemaphoreSlim SchemaLock = new(1, 1);

    private readonly string _connectionString;

    public SqlServerUserRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("talentconsultingdb")
                            ?? throw new InvalidOperationException(
                                "Connection string 'talentconsultingdb' was not found.");
    }

    public async Task<List<UserDataModel>> GetUsers(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var payloads = await connection.QueryAsync<string>(
            new CommandDefinition("SELECT Payload FROM dbo.Users", cancellationToken: ct));
        return payloads.Select(Deserialize<UserDataModel>).ToList();
    }

    public async Task<UserDataModel?> GetUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var payload = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT Payload FROM dbo.Users WHERE Id = @Id",
                new { Id = userId },
                cancellationToken: ct));
        return payload is null ? null : Deserialize<UserDataModel>(payload);
    }

    public async Task<UserDataModel> AddUser(UserDataModel user, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var id = Guid.NewGuid().ToString();
        var stored = new UserDataModel(id)
        {
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            HasAcceptedRegistration = false,
            InvitationToken = GenerateInvitationToken(),
            InvitationExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO dbo.Users (Id, Payload)
            VALUES (@Id, @Payload);
            """,
            new { Id = id, Payload = Serialize(stored) },
            cancellationToken: ct));
        return stored;
    }

    public async Task<bool> UpdateUser(string userId, UserDataModel updatedUser, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || updatedUser is null)
            return false;

        await EnsureSchemaAsync(ct);
        var existing = await GetUser(userId, ct);
        if (existing is null)
            return false;

        var toStore = new UserDataModel(userId)
        {
            Name = updatedUser.Name,
            Email = updatedUser.Email,
            Role = updatedUser.Role,
            HasAcceptedRegistration = updatedUser.HasAcceptedRegistration,
            IdentityProvider = existing.IdentityProvider,
            IdentitySubject = existing.IdentitySubject,
            IdentityUsername = existing.IdentityUsername,
            InvitationToken = existing.InvitationToken,
            InvitationExpiresAtUtc = existing.InvitationExpiresAtUtc
        };

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Users
            SET Payload = @Payload
            WHERE Id = @Id;
            """,
            new { Id = userId, Payload = Serialize(toStore) },
            cancellationToken: ct));
        return affected > 0;
    }

    public async Task<bool> DeleteUser(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        await EnsureSchemaAsync(ct);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM dbo.Users
            WHERE Id = @Id;
            """,
            new { Id = userId },
            cancellationToken: ct));

        return affected > 0;
    }

    public async Task<UserDataModel?> ResendInvite(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var user = await GetUser(userId, ct);
        if (user is null)
            return null;

        if (user.HasAcceptedRegistration)
            return null;

        user.InvitationToken = GenerateInvitationToken();
        user.InvitationExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Users
            SET Payload = @Payload
            WHERE Id = @Id;
            """,
            new { Id = userId, Payload = Serialize(user) },
            cancellationToken: ct));
        return affected > 0 ? user : null;
    }

    public async Task<UserDataModel?> AcceptInvite(
        string invitationToken,
        string identityProvider,
        string identitySubject,
        string identityUsername,
        string email,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invitationToken) || string.IsNullOrWhiteSpace(identitySubject))
            return null;

        var users = await GetUsers(ct);
        var matched = users.FirstOrDefault(u =>
            string.Equals(u.InvitationToken, invitationToken, StringComparison.Ordinal) &&
            u.InvitationExpiresAtUtc.HasValue &&
            u.InvitationExpiresAtUtc.Value >= DateTimeOffset.UtcNow);
        if (matched is null)
            return null;

        var existingBySubject = users.FirstOrDefault(u =>
            !string.IsNullOrWhiteSpace(u.IdentitySubject) &&
            string.Equals(u.IdentitySubject, identitySubject, StringComparison.Ordinal));
        if (existingBySubject is not null && !string.Equals(existingBySubject.Id, matched.Id, StringComparison.Ordinal))
            return null;

        matched.IdentityProvider = string.IsNullOrWhiteSpace(identityProvider) ? "keycloak" : identityProvider;
        matched.IdentitySubject = identitySubject;
        matched.IdentityUsername = identityUsername ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(email))
            matched.Email = email;
        matched.HasAcceptedRegistration = true;
        matched.InvitationToken = string.Empty;
        matched.InvitationExpiresAtUtc = null;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE dbo.Users
            SET Payload = @Payload
            WHERE Id = @Id;
            """,
            new { Id = matched.Id, Payload = Serialize(matched) },
            cancellationToken: ct));
        return affected > 0 ? matched : null;
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        if (_schemaInitialized)
            return;

        await SchemaLock.WaitAsync(ct);
        try
        {
            if (_schemaInitialized)
                return;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Users
                    (
                        Id NVARCHAR(100) NOT NULL PRIMARY KEY,
                        Payload NVARCHAR(MAX) NOT NULL,
                        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL
                            CONSTRAINT DF_Users_CreatedAtUtc DEFAULT SYSUTCDATETIME()
                    );
                END;
                """,
                cancellationToken: ct));
            _schemaInitialized = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    private static T Deserialize<T>(string payload)
    {
        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
               ?? throw new InvalidOperationException("Failed to deserialize SQL payload.");
    }

    private static string Serialize<T>(T model) => JsonSerializer.Serialize(model, JsonOptions);

    private static string GenerateInvitationToken() => Guid.NewGuid().ToString("N");
}
