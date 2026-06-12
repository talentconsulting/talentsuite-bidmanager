using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TalentSuite.Shared;
using TalentSuite.Shared.Messaging;

namespace TalentSuite.Server.Messaging;

public sealed class AzureServiceBusClient : IAzureServiceBusClient, IAsyncDisposable
{
    private readonly AzureServiceBusOptions _options;
    private readonly ILogger<AzureServiceBusClient> _logger;
    private readonly JsonSerializerOptions _serializerOptions = SerialiserOptions.JsonOptions;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<ServiceBusClient> _client;

    public AzureServiceBusClient(IOptions<AzureServiceBusOptions> options, ILogger<AzureServiceBusClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new Lazy<ServiceBusClient>(() => CreateClient(), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task PublishAsync<T>(string entityName, T payload, CancellationToken ct = default)
        => PublishInternalAsync(entityName, payload, typeof(T), ct);

    public Task PublishAsync(string entityName, object payload, CancellationToken ct = default)
        => PublishInternalAsync(entityName, payload, payload.GetType(), ct);

    private async Task PublishInternalAsync(string entityName, object? payload, Type payloadType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityName))
            throw new ArgumentException("Entity name is required.", nameof(entityName));

        if (payload is null)
            throw new ArgumentNullException(nameof(payload));

        var client = GetOrCreateClient();
        var sender = _senders.GetOrAdd(entityName, client.CreateSender);

        var body = JsonSerializer.SerializeToUtf8Bytes(payload, payloadType, _serializerOptions);
        var message = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            Subject = payloadType.Name,
            MessageId = Guid.NewGuid().ToString("N")
        };

        message.ApplicationProperties["messageType"] = payloadType.FullName ?? payloadType.Name;
        message.ApplicationProperties["messageKind"] = ResolveMessageKind(payloadType);

        _logger.LogInformation(
            "Publishing {MessageType} to queue {QueueName}",
            payloadType.Name,
            entityName);

        await sender.SendMessageAsync(message, ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
        {
            await sender.DisposeAsync();
        }

        if (_client.IsValueCreated)
        {
            await _client.Value.DisposeAsync();
        }
    }

    private ServiceBusClient GetOrCreateClient() => _client.Value;

    private ServiceBusClient CreateClient()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
            return new ServiceBusClient(_options.ConnectionString);

        var fullyQualifiedNamespace = _options.FullyQualifiedNamespace;
        if (!string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
            return new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());

        throw new InvalidOperationException(
            $"Azure Service Bus is not configured. Set '{AzureServiceBusOptions.SectionName}:ConnectionString', '{AzureServiceBusOptions.SectionName}:FullyQualifiedNamespace', or the related environment variables.");
    }

    private static string ResolveMessageKind(Type payloadType)
    {
        if (payloadType.Name.EndsWith("Command", StringComparison.OrdinalIgnoreCase))
            return "command";

        if (payloadType.Name.EndsWith("Event", StringComparison.OrdinalIgnoreCase))
            return "event";

        return "message";
    }
}
