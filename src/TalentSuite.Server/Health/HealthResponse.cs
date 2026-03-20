namespace TalentSuite.Server.Health;

public sealed record HealthResponse(
    bool Success,
    IReadOnlyList<HealthCheckResult> Checks);
