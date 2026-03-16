using TalentSuite.Shared.Messaging;

namespace TalentSuite.Server.Messaging;

public static class Extensions
{
    public static IServiceCollection AddAzureServiceBusMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AzureServiceBusOptions>(options =>
        {
            var configuredConnection = configuration[$"{AzureServiceBusOptions.SectionName}:ConnectionString"]
                                       ?? configuration.GetConnectionString("messaging")
                                       ?? Environment.GetEnvironmentVariable("AZURESERVICEBUS__CONNECTIONSTRING");

            if (LooksLikeConnectionString(configuredConnection))
            {
                options.ConnectionString = configuredConnection;
            }
            else
            {
                options.FullyQualifiedNamespace = ExtractFullyQualifiedNamespace(configuredConnection);
            }

            options.FullyQualifiedNamespace ??= configuration[$"{AzureServiceBusOptions.SectionName}:FullyQualifiedNamespace"];
            options.FullyQualifiedNamespace ??= Environment.GetEnvironmentVariable("AZURESERVICEBUS__FULLYQUALIFIEDNAMESPACE");
        });

        services.AddSingleton<IAzureServiceBusClient, AzureServiceBusClient>();
        return services;
    }

    private static bool LooksLikeConnectionString(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractFullyQualifiedNamespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.Host;

        if (!value.Contains("://", StringComparison.OrdinalIgnoreCase)
            && value.Contains('.', StringComparison.Ordinal))
        {
            return value.Trim();
        }

        return null;
    }
}
