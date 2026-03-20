using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Bid_files : SliceTestBase
{
    [Test]
    public async Task UploadAndListBidFiles_WorksForBid()
    {
        var bidId = await CreateBidAsync();

        using var multipart = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hello bid file"));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        multipart.Add(fileContent, "file", "notes.txt");

        var uploadResponse = await Client.PostAsync($"/api/bids/{Uri.EscapeDataString(bidId)}/files", multipart);
        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<BidFileResponse>();
        Assert.That(uploaded, Is.Not.Null);
        Assert.That(uploaded!.BidId, Is.EqualTo(bidId));
        Assert.That(uploaded.FileName, Is.EqualTo("notes.txt"));
        Assert.That(uploaded.SizeBytes, Is.GreaterThan(0));

        var listResponse = await Client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}/files");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var files = await listResponse.Content.ReadFromJsonAsync<List<BidFileResponse>>();
        Assert.That(files, Is.Not.Null);
        Assert.That(files!, Has.Count.EqualTo(1));
        Assert.That(files[0].FileName, Is.EqualTo("notes.txt"));
        Assert.That(files[0].BidId, Is.EqualTo(bidId));

        var downloadResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/files/{Uri.EscapeDataString(files[0].Id)}");
        Assert.That(downloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.That(Encoding.UTF8.GetString(downloadedBytes), Is.EqualTo("hello bid file"));

        var deleteResponse = await Client.DeleteAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/files/{Uri.EscapeDataString(files[0].Id)}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var listAfterDelete = await Client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}/files");
        Assert.That(listAfterDelete.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var filesAfterDelete = await listAfterDelete.Content.ReadFromJsonAsync<List<BidFileResponse>>();
        Assert.That(filesAfterDelete, Is.Not.Null);
        Assert.That(filesAfterDelete!, Is.Empty);
    }

    private async Task<string> CreateBidAsync()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice File Co",
            Summary = "File upload/list slice test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "File question",
                    Description = "File test question.",
                    Length = "200 words",
                    Weighting = 10,
                    Required = true,
                    NiceToHave = false
                }
            ]
        });
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var createJson = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createJson);
        var bidId = createDoc.RootElement.GetProperty("result").GetString();
        Assert.That(string.IsNullOrWhiteSpace(bidId), Is.False);
        return bidId!;
    }
}
