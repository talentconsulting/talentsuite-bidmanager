using TalentSuite.Server.Users.Data;
using TalentSuite.Server.Users.Mappers;
using TalentSuite.Server.Users.Seeding;
using TalentSuite.Server.Users.Services;

namespace TalentSuite.Server.Users;

public static class Extensions
{
    private const string UseInMemoryDataKey = "USE_IN_MEMORY_DATA";

    public static IServiceCollection AddUserMappings(this IServiceCollection services)
    {
        services.AddSingleton<UserMapper>();

        return services;
    }

    public static IServiceCollection AddUserServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        var useInMemory = string.Equals(configuration?[UseInMemoryDataKey], "true", StringComparison.OrdinalIgnoreCase);
        var useSql = !useInMemory && !string.IsNullOrWhiteSpace(configuration?.GetConnectionString("talentconsultingdb"));
        if (useSql)
        {
            services.AddScoped<IManageUsers, SqlServerUserRepository>();
        }
        else
        {
            services.AddSingleton<IManageUsers, InMemoryUserRepository>();
        }

        services.AddScoped<IUserService, UserService>();
        services.AddHttpClient();
        services.AddScoped<IKeycloakAdminService, KeycloakAdminService>();
        services.AddScoped<UserRepositorySeeder>();

        return services;
    }
}
