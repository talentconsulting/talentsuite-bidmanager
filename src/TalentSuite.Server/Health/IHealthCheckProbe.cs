namespace TalentSuite.Server.Health;

public interface IHealthCheckProbe
{
    string Name { get; }

    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
