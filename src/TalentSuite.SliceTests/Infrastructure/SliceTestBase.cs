using Microsoft.Extensions.DependencyInjection;

namespace TalentSuite.SliceTests.Infrastructure;

public abstract class SliceTestBase
{
    protected TestWebApplicationFactory Factory { get; private set; } = default!;
    protected HttpClient Client { get; private set; } = default!;

    [SetUp]
    public void SetUpBase()
    {
        Factory = new TestWebApplicationFactory();
        Client = Factory.CreateClient();
    }

    [TearDown]
    public void TearDownBase()
    {
        Client.Dispose();
        Factory.Dispose();
    }

    protected T GetRequiredService<T>() where T : notnull
        => Factory.Services.GetRequiredService<T>();
}
