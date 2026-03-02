using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Server.Bids.Services;

public interface IDocumentIngestionservice
{
    /// <summary>
    /// Extracts structured bid questions from a document (PDF/DOCX/XLSX) using:
    /// 1) Azure AI Document Intelligence to extract text
    /// 2) Azure OpenAI (chat completion) to structure questions
    /// </summary>
    Task<ParsedDocumentModel?> ExtractDocumentAsync(Stream documentStream,
        string filename,
        BidStage stage,
        CancellationToken ct = default);
}

public sealed class DocumentIngestionService : IDocumentIngestionservice
{
    private readonly DocumentIntelligenceClient _diClient;
    private readonly AzureOpenAIClient _aoaiClient;
    private readonly string _chatDeployment;
    
    public DocumentIngestionService(IConfiguration config)
    {
        // ---- Document Intelligence ----
        var diEndpoint = config["DocumentIntelligence:Endpoint"]
            ?? throw new InvalidOperationException("Missing config: DocumentIntelligence:Endpoint");

        var diKey = config["DocumentIntelligence:ApiKey"]
            ?? throw new InvalidOperationException("Missing config: DocumentIntelligence:ApiKey");

        _diClient = new DocumentIntelligenceClient(new Uri(diEndpoint), new AzureKeyCredential(diKey));

        // ---- Azure OpenAI ----
        var aoaiEndpoint = config["AzureOpenAI:Endpoint"]
            ?? throw new InvalidOperationException("Missing config: AzureOpenAI:Endpoint");

        var aoaiKey = config["AzureOpenAI:ApiKey"]
            ?? throw new InvalidOperationException("Missing config: AzureOpenAI:ApiKey");

        _chatDeployment = config["AzureOpenAI:ChatDeployment"]
            ?? throw new InvalidOperationException("Missing config: AzureOpenAI:ChatDeployment");

        _aoaiClient = new AzureOpenAIClient(new Uri(aoaiEndpoint), new AzureKeyCredential(aoaiKey));
    }

    public async Task<ParsedDocumentModel?> ExtractDocumentAsync(Stream documentStream,
        string fileName,
        BidStage stage,
        CancellationToken ct = default)
    {
        if (documentStream is null) throw new ArgumentNullException(nameof(documentStream));
        if (!documentStream.CanRead) throw new ArgumentException("Document stream must be readable.", nameof(documentStream));
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "document";

        // 1) Extract text with Document Intelligence (prebuilt-read)
        var extractedText = await ExtractTextWithDocumentIntelligenceAsync(documentStream, ct);

        if (string.IsNullOrWhiteSpace(extractedText))
            return new ParsedDocumentModel();

        // Optional clamp to avoid huge prompts
        extractedText = ClampText(extractedText, maxChars: 80_000);

        // 2) Ask Azure OpenAI to produce strict JSON array
        var json = await ExtractQuestionsJsonWithAzureOpenAiAsync(extractedText, fileName, stage, ct);

        // 3) Parse into strongly typed list
        var parsed = JsonSerializer.Deserialize<ParsedDocumentModel>(json, SerialiserOptions.JsonOptions);
        if (parsed?.Questions is { Count: > 0 })
        {
            for (var i = 0; i < parsed.Questions.Count; i++)
            {
                parsed.Questions[i].QuestionOrderIndex = i + 1;
            }
        }

        return parsed;
    }

    private async Task<string> ExtractTextWithDocumentIntelligenceAsync(Stream document, CancellationToken ct)
    {
        if (document.CanSeek) document.Position = 0;

        using var ms = new MemoryStream();
        await document.CopyToAsync(ms, ct);
        var binary = new BinaryData(ms.ToArray());

        Operation<AnalyzeResult> op =
            await _diClient.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-read", binary, cancellationToken: ct);

        var result = op.Value;
        var sb = new StringBuilder();

        // 1) Lines (most granular)
        if (result.Pages is not null)
        {
            foreach (var page in result.Pages)
            {
                if (page.Lines is null) continue;

                foreach (var line in page.Lines)
                {
                    if (!string.IsNullOrWhiteSpace(line.Content))
                        sb.AppendLine(line.Content);
                }

                if (sb.Length > 0)
                    sb.AppendLine();
            }
        }

        // 2) Paragraphs
        if (sb.Length == 0 && result.Paragraphs is not null)
        {
            foreach (var p in result.Paragraphs)
            {
                if (!string.IsNullOrWhiteSpace(p.Content))
                    sb.AppendLine(p.Content);
            }
        }

        // 3) Raw Content fallback
        if (sb.Length == 0 && !string.IsNullOrWhiteSpace(result.Content))
            sb.AppendLine(result.Content);

        return sb.ToString().Trim();
    }

    private async Task<string> ExtractQuestionsJsonWithAzureOpenAiAsync(
        string extractedText,
        string fileName,
        BidStage stage,
        CancellationToken ct)
    {
        var chatClient = _aoaiClient.GetChatClient(_chatDeployment);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are a strict information extraction engine. Return ONLY valid JSON. No markdown, no commentary."
            ),
            new UserChatMessage($"""
Extract bid questions (from {stage.ToString()} section of the document only) the document text below.

Return a JSON payload which contains an array of objects on a field called questions with EXACTLY these fields:
  - category (string)
  - number (string)
  - title (string)
  - description (string)
  - length (string)
  - weighting (int, 0-100)
  - required (bool)
  - niceToHave (bool)
  
and also extract relevant information at the root level of the json document about the document with the exact fields. 
If the length of the response is not found please set the default length to '750 chars (inc spaces)' 


- company (string), usually on the first page and is company plus department
- summary (string), which contains a summary of the work involved
- budget (string), which contains any information about the budget mentioned in the document
- deadlineForQualifying (string), which contains any information about the deadline for qualifying questions
- deadlineForSubmission (string, which contains any information about the deadline for submitting this bid
- lengthOfContract (string), which contains any information about the length of the contract mentioned in the document

Rules:
  - If a question is marked "mandatory", "must", "required" -> required=true, niceToHave=false.
  - If a question is marked "desirable", "optional", "nice to have" -> required=false, niceToHave=true.
  - If neither is clear -> required=false, niceToHave=false.
  - If weighting isn't present -> weighting=0.
  - If title is not explicit -> create a short title from the question text.
  - Each question will be under a specific heading and this is the category field
  - Keep description concise but informative (1-4 sentences max).
  - If length isn't present -> length="".
  - Do NOT include any fields other than the ones listed.

Document name: {fileName}
Bid stage: {stage}

DOCUMENT TEXT:
{extractedText}
""")
        };

        // You can optionally set MaxTokens, Temperature etc via ChatCompletionOptions if desired.
        var response = await chatClient.CompleteChatAsync(messages, cancellationToken: ct);

        var sb = new StringBuilder();
        foreach (var part in response.Value.Content)
        {
            if (!string.IsNullOrWhiteSpace(part.Text))
                sb.Append(part.Text);
        }

        return sb.ToString().Trim();
    }

    private static string ClampText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.Length <= maxChars) return text;
        return text.Substring(0, maxChars);
    }
}
