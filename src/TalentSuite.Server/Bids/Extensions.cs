using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
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
        services.AddSingleton<TalentSuiteMetrics>();
        services.AddScoped<IBidService, BidService>();
        services.AddSingleton<IDocumentIngestionJobService, DocumentIngestionJobService>();

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
            // Azure SDK clients are thread-safe and pool connections; singletons avoid
            // rebuilding client pipelines on every request.
            services.AddSingleton(_ => new DocumentIntelligenceClient(
                new Uri(configuration![DocumentIntelligenceEndpointKey]!),
                new AzureKeyCredential(configuration[DocumentIntelligenceApiKeyKey]!)));
            services.AddSingleton(_ => new AzureOpenAIClient(
                new Uri(configuration![AzureOpenAiEndpointKey]!),
                new AzureKeyCredential(configuration[AzureOpenAiApiKeyKey]!)));
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

        // Singleton so the Foundry agents client, credential handshake, and the
        // file-name cache survive across requests instead of being rebuilt per scope.
        services.AddSingleton<IAzureOpenAiChatService, AzureOpenAiChatService>();
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
