using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TalentSuite.Shared.Bids;
using TalentSuite.SliceTests.Infrastructure;

namespace TalentSuite.SliceTests.Bids;

public class Ingestion_summary_question_removal : SliceTestBase
{
    [Test]
    public async Task IngestedQuestions_WhenOneIsRemovedBeforeSave_RemovedQuestionIsNotPersisted()
    {
        var parsed = await IngestAsync();
        Assert.That(parsed.Questions.Count, Is.GreaterThanOrEqualTo(3));

        var questionsToSave = parsed.Questions
            .Take(3)
            .Select(CloneQuestion)
            .ToList();

        var removedQuestion = questionsToSave[1];
        questionsToSave.RemoveAt(1);

        for (var i = 0; i < questionsToSave.Count; i++)
            questionsToSave[i].QuestionOrderIndex = i + 1;

        var createResponse = await Client.PostAsJsonAsync("/api/bids", new CreateBidRequest
        {
            UniqueReference = "REF-123",
            Company = parsed.Company ?? "Slice Ingestion Co",
            Summary = parsed.Summary ?? "Created from ingestion result",
            Budget = parsed.Budget,
            DeadlineForQualifying = parsed.DeadlineForQualifying,
            DeadlineForSubmission = parsed.DeadlineForSubmission,
            LengthOfContract = parsed.LengthOfContract,
            Stage = BidStage.Stage1,
            Questions = questionsToSave.Select(q => new CreateQuestionRequest
            {
                QuestionOrderIndex = q.QuestionOrderIndex,
                Category = q.Category,
                Number = q.Number,
                Title = q.Title,
                Description = q.Description,
                Length = q.Length,
                Weighting = q.Weighting,
                Required = q.Required,
                NiceToHave = q.NiceToHave
            }).ToList()
        });

        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var bidId = await ExtractCreatedIdAsync(createResponse);
        var getResponse = await Client.GetAsync($"/api/bids/{Uri.EscapeDataString(bidId)}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var created = await getResponse.Content.ReadFromJsonAsync<BidResponse>();
        Assert.That(created, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(created!.Questions, Has.Count.EqualTo(2));
            Assert.That(created.UniqueReference, Is.EqualTo("REF-123"));
            Assert.That(created.Questions.Any(q =>
                string.Equals(q.Title, removedQuestion.Title, StringComparison.Ordinal) &&
                string.Equals(q.Number, removedQuestion.Number, StringComparison.Ordinal)), Is.False);
            Assert.That(created.Questions.Select(q => q.QuestionOrderIndex), Is.EqualTo(new[] { 1, 2 }));
        });
    }

    private async Task<ParsedDocumentResponse> IngestAsync()
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
        return parsed!;
    }

    private static ParsedQuestionResponse CloneQuestion(ParsedQuestionResponse q)
    {
        return new ParsedQuestionResponse
        {
            QuestionOrderIndex = q.QuestionOrderIndex,
            Category = q.Category,
            Number = q.Number,
            Title = q.Title,
            Description = q.Description,
            Length = q.Length,
            Weighting = q.Weighting,
            Required = q.Required,
            NiceToHave = q.NiceToHave
        };
    }

    private static async Task<string> ExtractCreatedIdAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var bidId = doc.RootElement.GetProperty("result").GetString();
        Assert.That(string.IsNullOrWhiteSpace(bidId), Is.False);
        return bidId!;
    }
}
