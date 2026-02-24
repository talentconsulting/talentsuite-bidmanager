using TalentSuite.Functions.CommentEmail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TalentSuite.Functions;
using TalentSuite.Functions.InviteEmail;
using TalentSuite.Functions.StoringBids.BidLibrary;
using TalentSuite.Functions.StoringBids.Storage;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddEmailConfiguration(context.Configuration);
        services.AddInviteEmail();
        services.AddCommentEmail();
        services.AddHttpClient();
        services.AddSingleton<IAzureBlobStorageService, AzureBlobStorageService>();
        services.AddSingleton<IBidLibraryApiClient, BidLibraryApiClient>();
        services.AddSingleton<IBidLibraryWriter, BidLibraryWriter>();
    })
    .Build();

host.Run();
