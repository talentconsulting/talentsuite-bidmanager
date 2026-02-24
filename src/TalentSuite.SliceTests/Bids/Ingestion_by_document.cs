using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TalentSuite.Shared.Bids;
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
}
