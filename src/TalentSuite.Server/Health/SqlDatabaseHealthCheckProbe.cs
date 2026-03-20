using Microsoft.Data.SqlClient;

namespace TalentSuite.Server.Health;

public sealed class SqlDatabaseHealthCheckProbe : IHealthCheckProbe
{
    private readonly string _connectionString;

    public SqlDatabaseHealthCheckProbe(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("talentconsultingdb")
                            ?? throw new InvalidOperationException(
                                "Connection string 'talentconsultingdb' was not found.");
    }

    public string Name => "database";

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return new HealthCheckResult(Name, true, "Database connection succeeded.");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(Name, false, $"Database connection failed: {ex.Message}");
        }
    }
}
