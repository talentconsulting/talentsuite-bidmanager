namespace TalentSuite.Server.Users.Seeding;

public static class UserSeedingExtensions
{
    public static async Task SeedUsersAsync(this WebApplication app, CancellationToken ct = default)
    {
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<UserRepositorySeeder>();
        await seeder.SeedAsync(ct);
    }
}
