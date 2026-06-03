using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalentSuite.Shared.Bids;
using TalentSuite.Shared;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Ingestion_by_document : SliceTestBase
{
    [Test]
    public async Task Ingest_WithFileAndStage_ReturnsParsedDocument()
    {
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 1, 2, 3, 4, 5 };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        content.Add(fileContent, "file", "sample.docx");
        content.Add(new StringContent(BidStage.Stage1.ToString()), "stage");

        var response = await Client.PostAsync("/api/document", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var parsed = await response.Content.ReadFromJsonAsync<ParsedDocumentResponse>();
        Assert.That(parsed, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(parsed!.Company, Is.Not.Null.And.Not.Empty);
            Assert.That(parsed.Summary, Is.Not.Null.And.Not.Empty);
            Assert.That(parsed.Questions, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Ingest_WithoutFile_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(BidStage.Stage1.ToString()), "stage");

        var response = await Client.PostAsync("/api/document", content);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Ingest_StreamedJob_ReturnsProgressEventsAndParsedDocument()
    {
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 1, 2, 3, 4, 5 };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        content.Add(fileContent, "file", "sample.docx");
        content.Add(new StringContent(BidStage.Stage1.ToString()), "stage");

        var createResponse = await Client.PostAsync("/api/document/jobs", content);

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));

        var created = await createResponse.Content.ReadFromJsonAsync<DocumentIngestionJobCreatedResponse>();
        Assert.That(created?.JobId, Is.Not.Null.And.Not.Empty);

        using var streamResponse = await Client.GetAsync(
            $"/api/document/jobs/{created!.JobId}/stream",
            HttpCompletionOption.ResponseHeadersRead);

        Assert.That(streamResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(streamResponse.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/x-ndjson"));

        await using var responseStream = await streamResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(responseStream);

        var events = new List<DocumentIngestionJobEventResponse>();
        while (events.All(e => !e.IsComplete))
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var update = JsonSerializer.Deserialize<DocumentIngestionJobEventResponse>(
                line,
                SerialiserOptions.JsonOptions);

            Assert.That(update, Is.Not.Null);
            events.Add(update!);
        }

        Assert.Multiple(() =>
        {
            Assert.That(events.Select(x => x.Status), Does.Contain("queued"));
            Assert.That(events.Last().Status, Is.EqualTo("completed"));
            Assert.That(events.Last().Result, Is.Not.Null);
            Assert.That(events.Last().Result!.Questions, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Ingest_JobsListAndDetail_ReturnCreatedJobForCurrentUser()
    {
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 1, 2, 3, 4, 5 };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        content.Add(fileContent, "file", "sample.docx");
        content.Add(new StringContent(BidStage.Stage2.ToString()), "stage");

        var createResponse = await Client.PostAsync("/api/document/jobs", content);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentIngestionJobCreatedResponse>();

        Assert.That(created?.JobId, Is.Not.Null.And.Not.Empty);

        var listResponse = await Client.GetAsync("/api/document/jobs");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var jobs = await listResponse.Content.ReadFromJsonAsync<List<DocumentIngestionJobStatusResponse>>();
        var listedJob = jobs?.SingleOrDefault(x => x.JobId == created!.JobId);

        Assert.That(listedJob, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(listedJob!.FileName, Is.EqualTo("sample.docx"));
            Assert.That(listedJob.Stage, Is.EqualTo(BidStage.Stage2));
            Assert.That(listedJob.Status, Is.Not.Empty);
        });

        DocumentIngestionJobStatusResponse? detailedJob = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var detailResponse = await Client.GetAsync($"/api/document/jobs/{created!.JobId}");
            Assert.That(detailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            detailedJob = await detailResponse.Content.ReadFromJsonAsync<DocumentIngestionJobStatusResponse>();
            if (detailedJob?.IsComplete == true)
                break;

            await Task.Delay(50);
        }

        Assert.That(detailedJob, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(detailedJob!.IsComplete, Is.True);
            Assert.That(detailedJob.Result, Is.Not.Null);
            Assert.That(detailedJob.Result!.Questions, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task Ingest_JobTimeout_MarksJobAsFailed()
    {
        await using var factory = new TimeoutIngestionWebApplicationFactory();
        using var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 1, 2, 3, 4, 5 };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        content.Add(fileContent, "file", "sample.docx");
        content.Add(new StringContent(BidStage.Stage1.ToString()), "stage");

        var createResponse = await client.PostAsync("/api/document/jobs", content);
        var created = await createResponse.Content.ReadFromJsonAsync<DocumentIngestionJobCreatedResponse>();

        Assert.That(created?.JobId, Is.Not.Null.And.Not.Empty);

        DocumentIngestionJobStatusResponse? detailedJob = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var detailResponse = await client.GetAsync($"/api/document/jobs/{created!.JobId}");
            Assert.That(detailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            detailedJob = await detailResponse.Content.ReadFromJsonAsync<DocumentIngestionJobStatusResponse>();
            if (detailedJob?.IsError == true)
                break;

            await Task.Delay(100);
        }

        Assert.That(detailedJob, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(detailedJob!.IsError, Is.True);
            Assert.That(detailedJob.IsComplete, Is.False);
            Assert.That(detailedJob.Message, Does.Contain("time limit"));
        });
    }

    private sealed class TimeoutIngestionWebApplicationFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DocumentIngestion:JobTimeout"] = "00:00:01"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDocumentIngestionservice>();
                services.AddScoped<IDocumentIngestionservice, HangingDocumentIngestionService>();
            });
        }
    }

    private sealed class HangingDocumentIngestionService : IDocumentIngestionservice
    {
        public async Task<ParsedDocumentModel?> ExtractDocumentAsync(
            Stream documentStream,
            string filename,
            BidStage stage,
            IProgress<DocumentIngestionProgressUpdate>? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report(new DocumentIngestionProgressUpdate
            {
                Status = "extracting_text",
                Message = "Extracting text from the source document."
            });

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new ParsedDocumentModel { Questions = [] };
        }
    }
}
