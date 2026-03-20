namespace TalentSuite.Server.Messaging;

public sealed class AzureServiceBusOptions
{
    public const string SectionName = "AzureServiceBus";
    public string? ConnectionString { get; set; }
    public string? FullyQualifiedNamespace { get; set; }
}
