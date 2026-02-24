using TalentSuite.Server.Bids.Data;
using TalentSuite.Server.Bids.Mappers;
using TalentSuite.Server.Bids.Services;

namespace TalentSuite.Server.Bids;

public static class Extensions
{
    private const string UseInMemoryDataKey = "USE_IN_MEMORY_DATA";
    private const string DocumentIntelligenceEndpointKey = "DocumentIntelligence:Endpoint";
    private const string DocumentIntelligenceApiKeyKey = "DocumentIntelligence:ApiKey";
    private const string AzureOpenAiEndpointKey = "AzureOpenAI:Endpoint";
    private const string AzureOpenAiApiKeyKey = "AzureOpenAI:ApiKey";
    private const string AzureOpenAiChatDeploymentKey = "AzureOpenAI:ChatDeployment";

    public static void AddBidServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<IBidService, BidService>();

        var useInMemory = string.Equals(configuration?[UseInMemoryDataKey], "true", StringComparison.OrdinalIgnoreCase);
        var ingestionConfigured = IsConfigured(configuration, DocumentIntelligenceEndpointKey)
                                  && IsConfigured(configuration, DocumentIntelligenceApiKeyKey)
                                  && IsConfigured(configuration, AzureOpenAiEndpointKey)
                                  && IsConfigured(configuration, AzureOpenAiApiKeyKey)
                                  && IsConfigured(configuration, AzureOpenAiChatDeploymentKey);
        if (useInMemory || !ingestionConfigured)
        {
            services.AddScoped<IDocumentIngestionservice, InMemoryDocumentIngestionService>();
        }
        else
        {
            services.AddScoped<IDocumentIngestionservice, DocumentIngestionService>();
        }

        var useSql = !useInMemory && !string.IsNullOrWhiteSpace(configuration?.GetConnectionString("talentconsultingdb"));
        if (useSql)
        {
            services.AddScoped<IManageBids, SqlServerBidRepository>();
        }
        else
        {
            services.AddSingleton<IManageBids, InMemoryBidRepository>();
        }

        services.AddScoped<IAzureOpenAiChatService, AzureOpenAiChatService>();
    }
    
    public static IServiceCollection AddBidMappings(this IServiceCollection services)
    {
        services.AddSingleton<BidMapper>();

        return services;
    }

    private static bool IsConfigured(IConfiguration? configuration, string key)
    {
        var value = configuration?[key];
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Placeholder values in launch settings should be treated as unset.
        if (value.StartsWith("__SET_", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
