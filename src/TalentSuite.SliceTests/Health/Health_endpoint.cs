using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalentSuite.Server;
using TalentSuite.Server.Health;
using TalentSuite.SliceTests.Infrastructure;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Users.Services;
using TalentSuite.Shared.Messaging;

namespace TalentSuite.SliceTests.Health;

public class Health_endpoint
{
    [Test]
    public async Task Get_returns_ok_when_all_checks_pass()
    {
        using var factory = new HealthTestWebApplicationFactory(
            new StubHealthCheckProbe("database", true, "Database connection succeeded."));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(payload, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(payload!.Success, Is.True);
            Assert.That(payload.Checks, Has.Count.EqualTo(1));
            Assert.That(payload.Checks[0], Is.EqualTo(new HealthCheckResult("database", true, "Database connection succeeded.")));
        });
    }

    [Test]
    public async Task Get_returns_service_unavailable_when_any_check_fails()
    {
        using var factory = new HealthTestWebApplicationFactory(
            new StubHealthCheckProbe("database", false, "Database connection failed."));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(payload, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(payload!.Success, Is.False);
            Assert.That(payload.Checks, Has.Count.EqualTo(1));
            Assert.That(payload.Checks[0], Is.EqualTo(new HealthCheckResult("database", false, "Database connection failed.")));
        });
    }

    private sealed class HealthTestWebApplicationFactory(StubHealthCheckProbe probe) : WebApplicationFactory<Program>
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
                services.RemoveAll<IHealthCheckProbe>();
                services.AddSingleton<IAzureServiceBusClient, InMemoryAzureServiceBusClient>();
                services.AddSingleton<IKeycloakAdminService, InMemoryKeycloakAdminService>();
                services.AddSingleton<IAzureOpenAiChatService, InMemoryAzureOpenAiChatService>();
                services.AddSingleton<IHealthCheckProbe>(probe);
            });
        }

        protected override void Dispose(bool disposing)
        {
            Environment.SetEnvironmentVariable("AUTHENTICATION_ENABLED", _originalAuthEnabled);
            Environment.SetEnvironmentVariable("USE_IN_MEMORY_DATA", _originalUseInMemory);
            base.Dispose(disposing);
        }
    }

    private sealed class StubHealthCheckProbe(string name, bool success, string description) : IHealthCheckProbe
    {
        public string Name => name;

        public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new HealthCheckResult(name, success, description));
    }
}
