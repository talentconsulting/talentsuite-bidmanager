using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using TalentSuite.Functions;
using TalentSuite.Functions.CommentEmail;
using TalentSuite.Functions.GoogleDriveSync;
using TalentSuite.Functions.InviteEmail;
using TalentSuite.Functions.StoringBids.BidLibrary;
using TalentSuite.Functions.StoringBids.Storage;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureLogging(logging =>
    {
        logging.AddOpenTelemetry(otelLogging =>
        {
            otelLogging.IncludeFormattedMessage = true;
            otelLogging.IncludeScopes = true;
        });
    })
    .ConfigureServices((context, services) =>
    {
        var useOtlp = !string.IsNullOrEmpty(context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource("TalentSuite.Functions")
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            activity.SetTag("http.method", request.Method.ToString());
                            activity.SetTag("http.url", request.RequestUri?.ToString());
                        };
                        options.EnrichWithHttpResponseMessage = (activity, response) =>
                        {
                            activity.SetTag("http.status_code", (int)response.StatusCode);
                        };
                    });

                if (useOtlp)
                    tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("TalentSuite.Functions");

                if (useOtlp)
                    metrics.AddOtlpExporter();
            });

        services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

        if (!string.IsNullOrEmpty(context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        {
            services.AddOpenTelemetry().UseAzureMonitorExporter(options =>
            {
                options.ConnectionString = context.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
            });
        }

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
