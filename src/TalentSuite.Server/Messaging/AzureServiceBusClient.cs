using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using TalentSuite.Shared;
using TalentSuite.Shared.Messaging;

namespace TalentSuite.Server.Messaging;

public sealed class AzureServiceBusClient : IAzureServiceBusClient, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<AzureServiceBusClient> _logger;
    private readonly JsonSerializerOptions _serializerOptions = SerialiserOptions.JsonOptions;
    // Lazy values so racing GetOrAdd calls cannot create senders that are never
    // stored (and therefore never disposed).
    private readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _senders = new(StringComparer.OrdinalIgnoreCase);

    public AzureServiceBusClient(ServiceBusClient client, ILogger<AzureServiceBusClient> logger)
    {
        _client = client;
        _logger = logger;
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

        var sender = _senders.GetOrAdd(
            entityName,
            static (name, c) => new Lazy<ServiceBusSender>(
                () => c.CreateSender(name),
                LazyThreadSafetyMode.ExecutionAndPublication),
            _client).Value;

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
        // The ServiceBusClient itself is owned and disposed by the DI container.
        foreach (var sender in _senders.Values)
        {
            if (sender.IsValueCreated)
                await sender.Value.DisposeAsync();
        }
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
