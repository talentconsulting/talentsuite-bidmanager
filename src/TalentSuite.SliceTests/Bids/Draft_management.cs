using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Draft_management : SliceTestBase
{
    [Test]
    public async Task Drafts_CanBeAddedListedAndUpdated_ForAQuestion()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var addResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts",
            new DraftRequest { Response = "Initial draft response." });
        Assert.That(addResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var addedDraft = await addResponse.Content.ReadFromJsonAsync<CreateAssetResponse>();
        Assert.That(addedDraft, Is.Not.Null);
        Assert.That(addedDraft!.Id, Is.Not.Empty);

        var listResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var drafts = await listResponse.Content.ReadFromJsonAsync<List<DraftResponse>>();
        Assert.That(drafts, Is.Not.Null);
        Assert.That(drafts!.Any(d => d.Id == addedDraft.Id && d.Response == "Initial draft response."), Is.True);

        var updateResponse = await Client.PutAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts/{Uri.EscapeDataString(addedDraft.Id)}",
            new UpdateDraftRequest
            {
                Id = addedDraft.Id,
                Response = "Updated draft response."
            });
        Assert.That(updateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updatedListResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts");
        Assert.That(updatedListResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var updatedDrafts = await updatedListResponse.Content.ReadFromJsonAsync<List<DraftResponse>>();
        Assert.That(updatedDrafts, Is.Not.Null);
        Assert.That(updatedDrafts!.Any(d => d.Id == addedDraft.Id && d.Response == "Updated draft response."), Is.True);
    }

    [Test]
    public async Task Drafts_CanBeDeleted_ForAQuestion()
    {
        var (bidId, questionId) = await CreateBidWithOneQuestionAsync();

        var addResponse = await Client.PostAsJsonAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts",
            new DraftRequest { Response = "Draft to delete." });
        Assert.That(addResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var addedDraft = await addResponse.Content.ReadFromJsonAsync<CreateAssetResponse>();
        Assert.That(addedDraft, Is.Not.Null);
        Assert.That(addedDraft!.Id, Is.Not.Empty);

        var deleteResponse = await Client.DeleteAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts?responseId={Uri.EscapeDataString(addedDraft.Id)}");
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var listResponse = await Client.GetAsync(
            $"/api/bids/{Uri.EscapeDataString(bidId)}/questions/{Uri.EscapeDataString(questionId)}/drafts");
        Assert.That(listResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var drafts = await listResponse.Content.ReadFromJsonAsync<List<DraftResponse>>();
        Assert.That(drafts, Is.Not.Null);
        Assert.That(drafts!.Any(d => d.Id == addedDraft.Id), Is.False);
    }

    private async Task<(string BidId, string QuestionId)> CreateBidWithOneQuestionAsync()
    {
        var createResponse = await Client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            Company = "Slice Draft Co",
            Summary = "Draft management slice test bid",
            Questions =
            [
                new CreateQuestionRequest
                {
                    Category = "General",
                    Number = "1",
                    Title = "Provide your draft response",
                    Description = "Describe your approach.",
                    Length = "300 words",
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

        var bidResponse = await Client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId!)}");
        Assert.That(bidResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var bid = await bidResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(bid, Is.Not.Null);
        Assert.That(bid!.Questions, Is.Not.Empty);

        var questionId = bid.Questions[0].Id;
        Assert.That(string.IsNullOrWhiteSpace(questionId), Is.False);
        return (bidId!, questionId);
    }
}
