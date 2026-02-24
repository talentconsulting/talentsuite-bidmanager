namespace TalentSuite.Shared.Messaging;

public interface IAzureServiceBusClient
{
    Task PublishAsync<T>(string entityName, T payload, CancellationToken ct = default);
    Task PublishAsync(string entityName, object payload, CancellationToken ct = default);
}
