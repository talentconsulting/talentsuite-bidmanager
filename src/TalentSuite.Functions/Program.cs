using Microsoft.Azure.Functions.Worker;
using TalentSuite.Functions.CommentEmail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TalentSuite.Functions;
using TalentSuite.Functions.InviteEmail;
using TalentSuite.Functions.GoogleDriveSync;
using TalentSuite.Functions.StoringBids.BidLibrary;
using TalentSuite.Functions.StoringBids.Storage;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddEmailConfiguration(context.Configuration);
        services.AddGoogleDriveSyncConfiguration(context.Configuration);
        services.AddInviteEmail();
        services.AddCommentEmail();
        services.AddHttpClient();
        services.AddSingleton<IGoogleDriveSyncService, GoogleDriveSyncService>();
        services.AddSingleton<IAzureBlobStorageService, AzureBlobStorageService>();
        services.AddSingleton<IBidLibraryApiClient, BidLibraryApiClient>();
        services.AddSingleton<IBidLibraryWriter, BidLibraryWriter>();
    })
    .Build();

host.Run();
