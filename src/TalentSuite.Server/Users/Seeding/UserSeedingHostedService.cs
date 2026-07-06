namespace TalentSuite.Server.Users.Seeding;

// Seeding runs off the startup path so a transient SQL outage delays seeding instead of
// crash-looping the app, and startup probes are served immediately.
public sealed class UserSeedingHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<UserSeedingHostedService> logger) : BackgroundService
{
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var seeder = scope.ServiceProvider.GetRequiredService<UserRepositorySeeder>();
                await seeder.SeedAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(
                    ex,
                    "User seeding attempt {Attempt}/{MaxAttempts} failed; retrying.",
                    attempt,
                    MaxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "User seeding failed after {MaxAttempts} attempts. Seed users may be missing until the next restart.",
                    MaxAttempts);
                return;
            }
        }
    }
}
