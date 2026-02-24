using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server;
using TalentSuite.Shared.Messaging;
using TalentSuite.Server.Users.Services;

namespace TalentSuite.SliceTests.Infrastructure;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _originalAuthEnabled = Environment.GetEnvironmentVariable("AUTHENTICATION_ENABLED");
    private readonly string? _originalUseInMemory = Environment.GetEnvironmentVariable("USE_IN_MEMORY_DATA");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("AUTHENTICATION_ENABLED", "false");
        Environment.SetEnvironmentVariable("USE_IN_MEMORY_DATA", "true");

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var testSettings = new Dictionary<string, string?>
            {
                ["AUTHENTICATION_ENABLED"] = "false",
                ["USE_IN_MEMORY_DATA"] = "true",
                ["AzureServiceBus:InviteUserEntityName"] = "invite-user",
                ["AzureServiceBus:BidSubmittedEntityName"] = "bid-submitted",
                ["AzureServiceBus:CommentSavedWithMentionsEntityName"] = "comment-saved-with-mentions"
            };

            configBuilder.AddInMemoryCollection(testSettings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAzureServiceBusClient>();
            services.RemoveAll<IKeycloakAdminService>();
            services.RemoveAll<IAzureOpenAiChatService>();
            services.AddSingleton<IAzureServiceBusClient, InMemoryAzureServiceBusClient>();
            services.AddSingleton<IKeycloakAdminService, InMemoryKeycloakAdminService>();
            services.AddSingleton<IAzureOpenAiChatService, InMemoryAzureOpenAiChatService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("AUTHENTICATION_ENABLED", _originalAuthEnabled);
        Environment.SetEnvironmentVariable("USE_IN_MEMORY_DATA", _originalUseInMemory);
        base.Dispose(disposing);
    }
}

public sealed record PublishedMessage(string EntityName, object Payload);

public sealed class InMemoryAzureServiceBusClient : IAzureServiceBusClient
{
    private readonly List<PublishedMessage> _messages = [];
    public IReadOnlyList<PublishedMessage> Messages => _messages;

    public Task PublishAsync<T>(string entityName, T payload, CancellationToken ct = default)
    {
        _messages.Add(new PublishedMessage(entityName, payload!));
        return Task.CompletedTask;
    }

    public Task PublishAsync(string entityName, object payload, CancellationToken ct = default)
    {
        _messages.Add(new PublishedMessage(entityName, payload));
        return Task.CompletedTask;
    }
}

public sealed record CreatedIdentity(
    string Username,
    string Email,
    string Name,
    string Role,
    string Subject);

public sealed class InMemoryKeycloakAdminService : IKeycloakAdminService
{
    private readonly List<CreatedIdentity> _createdIdentities = [];
    private readonly List<string> _deletedSubjects = [];

    public IReadOnlyList<CreatedIdentity> CreatedIdentities => _createdIdentities;
    public IReadOnlyList<string> DeletedSubjects => _deletedSubjects;

    public Task<bool> DeleteUserAsync(string? userId, string? username, string? email, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(userId))
            _deletedSubjects.Add(userId);

        return Task.FromResult(true);
    }

    public Task<string?> CreateUserAsync(
        string username,
        string email,
        string? name,
        string password,
        string role,
        CancellationToken ct = default)
    {
        var subject = $"kc-{username}";
        _createdIdentities.Add(new CreatedIdentity(
            username,
            email,
            name ?? string.Empty,
            role,
            subject));

        return Task.FromResult<string?>(subject);
    }
}

public sealed class InMemoryAzureOpenAiChatService : IAzureOpenAiChatService
{
    private int _threadCounter;

    public Task<ChatAnswerResult> AskAsync(
        string userPrompt,
        string? systemPrompt = null,
        string? threadId = null,
        CancellationToken ct = default)
    {
        var prompt = userPrompt ?? string.Empty;
        var resolvedThreadId = string.IsNullOrWhiteSpace(threadId)
            ? $"thread-{Interlocked.Increment(ref _threadCounter)}"
            : threadId;

        return Task.FromResult(new ChatAnswerResult
        {
            Response = $"[stubbed-chat] {prompt}",
            ThreadId = resolvedThreadId
        });
    }
}
