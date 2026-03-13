namespace TalentSuite.Server.Health;

public sealed record HealthCheckResult(
    string Name,
    bool Success,
    string Description);
