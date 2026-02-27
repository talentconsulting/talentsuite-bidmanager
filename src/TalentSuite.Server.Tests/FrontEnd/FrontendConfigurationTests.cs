using Microsoft.Extensions.Configuration;
using TalentSuite.FrontEnd.Configuration;

namespace TalentSuite.Server.Tests.FrontEnd;

public class FrontendConfigurationTests
{
    [Test]
    public void ResolveApiBaseAddress_UsesConfiguredHttpsValue()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TALENTSERVER_HTTPS"] = "https://localhost:5001"
        });

        var result = FrontendConfiguration.ResolveApiBaseAddress(config, strictConfiguration: false);

        Assert.That(result, Is.EqualTo("https://localhost:5001"));
    }

    [Test]
    public void ResolveApiBaseAddress_UsesDevFallback_WhenMissingAndNonStrict()
    {
        var config = BuildConfig([]);

        var result = FrontendConfiguration.ResolveApiBaseAddress(config, strictConfiguration: false);

        Assert.That(result, Is.EqualTo("https://localhost:5001"));
    }

    [Test]
    public void ResolveApiBaseAddress_Throws_WhenMissingAndStrict()
    {
        var config = BuildConfig([]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => FrontendConfiguration.ResolveApiBaseAddress(config, strictConfiguration: true));

        Assert.That(ex!.Message, Does.Contain("Missing TALENTSERVER_HTTPS/TALENTSERVER_HTTP"));
    }

    [Test]
    public void ResolveAuthorizedApiUrl_UsesDevFallback_WhenMissingAndNonStrict()
    {
        var config = BuildConfig([]);

        var result = FrontendConfiguration.ResolveAuthorizedApiUrl(config);

        Assert.That(result, Is.EqualTo("https://localhost:5001"));
    }

    [Test]
    public void ResolveAuthorizedApiUrl_Throws_WhenStrictAndMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["STRICT_CONFIGURATION"] = "true"
        });

        var ex = Assert.Throws<InvalidOperationException>(
            () => FrontendConfiguration.ResolveAuthorizedApiUrl(config));

        Assert.That(ex!.Message, Does.Contain("authorized API URL"));
    }

    [Test]
    public void ResolveKeycloakAuthority_BuildsFromEndpoint_WhenAuthorityMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["KEYCLOAK_HTTP"] = "http://localhost:8080"
        });

        var result = FrontendConfiguration.ResolveKeycloakAuthority(config, strictConfiguration: false);

        Assert.That(result, Is.EqualTo("http://localhost:8080/realms/TalentConsulting"));
    }

    [Test]
    public void ResolveKeycloakClientId_UsesFallback_WhenMissingAndNonStrict()
    {
        var config = BuildConfig([]);

        var result = FrontendConfiguration.ResolveKeycloakClientId(config, strictConfiguration: false);

        Assert.That(result, Is.EqualTo("talentsuite-frontend"));
    }

    [Test]
    public void IsAuthenticationEnabled_Throws_WhenMissingAndStrict()
    {
        var config = BuildConfig([]);

        Assert.Throws<InvalidOperationException>(
            () => FrontendConfiguration.IsAuthenticationEnabled(config, strictConfiguration: true));
    }

    private static IConfiguration BuildConfig(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
