using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server;
using TalentSuite.Server.Users.Services;
using TalentSuite.Shared.Messaging;

namespace TalentSuite.SliceTests.Infrastructure;

public sealed class AuthenticatedTestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _originalAuthEnabled = Environment.GetEnvironmentVariable("AUTHENTICATION_ENABLED");
    private readonly string? _originalUseInMemory = Environment.GetEnvironmentVariable("USE_IN_MEMORY_DATA");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("AUTHENTICATION_ENABLED", "true");
        Environment.SetEnvironmentVariable("USE_IN_MEMORY_DATA", "true");

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var testSettings = new Dictionary<string, string?>
            {
                ["AUTHENTICATION_ENABLED"] = "true",
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

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = HeaderTestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = HeaderTestAuthenticationHandler.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, HeaderTestAuthenticationHandler>(
                    HeaderTestAuthenticationHandler.Scheme,
                    _ => { });
        });
    }

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable("AUTHENTICATION_ENABLED", _originalAuthEnabled);
        Environment.SetEnvironmentVariable("USE_IN_MEMORY_DATA", _originalUseInMemory);
        base.Dispose(disposing);
    }
}
