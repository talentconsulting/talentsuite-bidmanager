using Microsoft.Data.SqlClient;

namespace TalentSuite.Server.Health;

public sealed class SqlDatabaseHealthCheckProbe : IHealthCheckProbe
{
    private readonly string _connectionString;
    private readonly ILogger<SqlDatabaseHealthCheckProbe> _logger;

    public SqlDatabaseHealthCheckProbe(IConfiguration configuration, ILogger<SqlDatabaseHealthCheckProbe> logger)
    {
        _connectionString = configuration.GetConnectionString("talentconsultingdb")
                            ?? throw new InvalidOperationException(
                                "Connection string 'talentconsultingdb' was not found.");
        _logger = logger;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // An aborted probe request is not a database outage.
            throw;
        }
        catch (Exception ex)
        {
            // The health endpoint is anonymous, so exception detail stays in the logs.
            _logger.LogWarning(ex, "Database health check failed.");
            return new HealthCheckResult(Name, false, "Database connection failed.");
        }
    }
}
