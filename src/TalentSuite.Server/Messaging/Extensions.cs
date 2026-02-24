using TalentSuite.Shared.Messaging;

namespace TalentSuite.Server.Messaging;

public static class Extensions
{
    public static IServiceCollection AddAzureServiceBusMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureServiceBusOptions>(options =>
        {
            options.ConnectionString = configuration[$"{AzureServiceBusOptions.SectionName}:ConnectionString"];
            options.ConnectionString ??= configuration.GetConnectionString("messaging");
            options.ConnectionString ??= Environment.GetEnvironmentVariable("AZURESERVICEBUS__CONNECTIONSTRING");
        });

        services.AddSingleton<IAzureServiceBusClient, AzureServiceBusClient>();
        return services;
    }
}
