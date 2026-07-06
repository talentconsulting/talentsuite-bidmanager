using TalentSuite.Shared.Messaging;

namespace TalentSuite.Server.Messaging;

public static class Extensions
{
    public static IHostApplicationBuilder AddAzureServiceBusMessaging(this IHostApplicationBuilder builder)
    {
        // The Aspire integration binds ConnectionStrings:messaging (injected by the
        // AppHost via WithReference) and handles both full connection strings and bare
        // namespaces with DefaultAzureCredential — the sniffing this project used to do.
        builder.AddAzureServiceBusClient("messaging", configureSettings: settings =>
        {
            // Legacy configuration fallbacks kept for environments that predate the
            // ConnectionStrings:messaging convention.
            if (string.IsNullOrWhiteSpace(settings.ConnectionString)
                && string.IsNullOrWhiteSpace(settings.FullyQualifiedNamespace))
            {
                var legacy = builder.Configuration["AzureServiceBus:ConnectionString"]
                             ?? Environment.GetEnvironmentVariable("AZURESERVICEBUS__CONNECTIONSTRING");
                if (!string.IsNullOrWhiteSpace(legacy))
                {
                    settings.ConnectionString = legacy;
                }
                else
                {
                    settings.FullyQualifiedNamespace =
                        builder.Configuration["AzureServiceBus:FullyQualifiedNamespace"]
                        ?? Environment.GetEnvironmentVariable("AZURESERVICEBUS__FULLYQUALIFIEDNAMESPACE");
                }
            }

            // Health checks stay off (they only activate when HealthCheckQueueName /
            // HealthCheckTopicName is set): tests run without any Service Bus
            // configuration, and the messaging readiness signal comes from publishes.
        });

        builder.Services.AddSingleton<IAzureServiceBusClient, AzureServiceBusClient>();
        return builder;
    }
}
