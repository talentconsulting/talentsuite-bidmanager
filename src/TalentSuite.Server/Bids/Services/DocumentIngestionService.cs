using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.AI.OpenAI;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
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
        IProgress<DocumentIngestionProgressUpdate>? progress = null,
        CancellationToken ct = default);
}

public sealed class DocumentIngestionService : IDocumentIngestionservice
{
    private const int DocumentChunkChars = 60_000;
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
        IProgress<DocumentIngestionProgressUpdate>? progress = null,
        CancellationToken ct = default)
    {
        if (documentStream is null) throw new ArgumentNullException(nameof(documentStream));
        if (!documentStream.CanRead) throw new ArgumentException("Document stream must be readable.", nameof(documentStream));
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "document";

        progress?.Report(new DocumentIngestionProgressUpdate
        {
            Status = "extracting_text",
            Message = "Extracting text from the source document."
        });

        if (IsSpreadsheetFile(fileName))
        {
            var spreadsheetChunks = await ExtractTextChunksFromSpreadsheetAsync(documentStream, ct);
            if (spreadsheetChunks.Count == 0)
                return new ParsedDocumentModel { Questions = [] };

            return ApplyQuestionOrderIndices(await ExtractAndMergeChunksAsync(
                spreadsheetChunks,
                fileName,
                stage,
                progress,
                progressMessagePrefix: "Structuring workbook content into bid questions",
                ct));
        }

        // 1) Extract text with Document Intelligence (prebuilt-read)
        var extractedText = await ExtractTextWithDocumentIntelligenceAsync(documentStream, ct);

        if (string.IsNullOrWhiteSpace(extractedText))
            return new ParsedDocumentModel { Questions = [] };

        var textChunks = SplitTextIntoChunks(extractedText, DocumentChunkChars);
        return ApplyQuestionOrderIndices(await ExtractAndMergeChunksAsync(
            textChunks,
            fileName,
            stage,
            progress,
            progressMessagePrefix: "Structuring the extracted content into bid questions",
            ct));
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
        CancellationToken ct,
        int? chunkIndex = null,
        int? chunkCount = null)
    {
        var chatClient = _aoaiClient.GetChatClient(_chatDeployment);
        var chunkContext = chunkIndex.HasValue && chunkCount.HasValue
            ? $"""

This is chunk {chunkIndex.Value + 1} of {chunkCount.Value} from the source document.
Only extract questions and metadata explicitly present in this chunk.
If a root-level metadata field is not present in this chunk, return it as an empty string.
Do not invent missing metadata from earlier or later chunks.
"""
            : string.Empty;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(
                "You are a strict information extraction engine. Return ONLY valid JSON. No markdown, no commentary."
            ),
            new UserChatMessage($"""
Extract bid questions (from {stage.ToString()} section of the document only), without changing the text on the question, the document text below.

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
- uniqueReference (string), usually on the first page and is the bid reference
- summary (string), which contains a summary of the work involved
- budget (string), which contains any information about the budget mentioned in the document
- deadlineForQualifying (string), which contains any information about the deadline for qualifying questions
- deadlineForSubmission (string, which contains any information about the deadline for submitting this bid
- lengthOfContract (string), which contains any information about the length of the contract mentioned in the document
- keyInformation (string), which contains any key information that could help us choose an example from the bid library, for example the problem to be solved and whjy its being done

Rules:
  - Do not change the text on a question, just take the complete question in the document
  - If a question is marked "mandatory", "must", "required" -> required=true, niceToHave=false.
  - If a question is marked "desirable", "optional", "nice to have" -> required=false, niceToHave=true.
  - If neither is clear -> required=false, niceToHave=false.
  - If weighting isn't present -> weighting=0.
  - If title is not explicit -> create a short title from the question text.
  - Each question will be under a specific heading and this is the category field
  - Keep description concise but informative (1-4 sentences max).
  - If length isn't present -> length="".
  - Do NOT include any fields other than the ones listed.
{chunkContext}

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

    private static bool IsSpreadsheetFile(string fileName)
    {
        return fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<string>> ExtractTextChunksFromSpreadsheetAsync(Stream document, CancellationToken ct)
    {
        if (document.CanSeek)
            document.Position = 0;

        using var memory = new MemoryStream();
        await document.CopyToAsync(memory, ct);
        memory.Position = 0;

        using var spreadsheet = SpreadsheetDocument.Open(memory, false);
        var workbookPart = spreadsheet.WorkbookPart
                           ?? throw new InvalidOperationException("Spreadsheet workbook part was missing.");

        var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
        var chunks = new List<string>();
        var current = new StringBuilder();

        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? Enumerable.Empty<Sheet>();
        foreach (var sheet in sheets)
        {
            var worksheetPart = workbookPart.GetPartById(sheet.Id!) as WorksheetPart;
            if (worksheetPart?.Worksheet is null)
                continue;

            AppendChunkLine(current, chunks, $"=== SHEET: {sheet.Name} ===");

            var rows = worksheetPart.Worksheet.Descendants<Row>();
            foreach (var row in rows)
            {
                var values = row.Elements<Cell>()
                    .Select(cell => GetCellText(cell, sharedStringTable))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();

                if (values.Count == 0)
                    continue;

                AppendChunkLine(current, chunks, string.Join(" | ", values));
            }
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks;
    }

    private static void AppendChunkLine(StringBuilder current, List<string> chunks, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var normalizedLine = line.Trim();
        if (current.Length > 0 && current.Length + normalizedLine.Length + Environment.NewLine.Length > DocumentChunkChars)
        {
            chunks.Add(current.ToString().Trim());
            current.Clear();
        }

        current.AppendLine(normalizedLine);
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStringTable)
    {
        var raw = cell.CellValue?.InnerText;
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(raw, out var sharedIndex)
            && sharedStringTable?.ElementAtOrDefault(sharedIndex) is SharedStringItem item)
        {
            return item.InnerText.Trim();
        }

        return raw.Trim();
    }

    private async Task<ParsedDocumentModel> ExtractAndMergeChunksAsync(
        List<string> chunks,
        string fileName,
        BidStage stage,
        IProgress<DocumentIngestionProgressUpdate>? progress,
        string progressMessagePrefix,
        CancellationToken ct)
    {
        var parsedChunks = new List<ParsedDocumentModel>();

        for (var i = 0; i < chunks.Count; i++)
        {
            var message = chunks.Count == 1
                ? $"{progressMessagePrefix}."
                : $"{progressMessagePrefix} ({i + 1}/{chunks.Count}).";

            progress?.Report(new DocumentIngestionProgressUpdate
            {
                Status = "structuring_questions",
                Message = message
            });

            var chunkJson = await ExtractQuestionsJsonWithAzureOpenAiAsync(
                chunks[i],
                fileName,
                stage,
                ct,
                chunkIndex: i,
                chunkCount: chunks.Count);

            var parsedChunk = JsonSerializer.Deserialize<ParsedDocumentModel>(chunkJson, SerialiserOptions.JsonOptions);
            if (parsedChunk is not null)
                parsedChunks.Add(parsedChunk);
        }

        return MergeParsedDocuments(parsedChunks);
    }

    private static List<string> SplitTextIntoChunks(string text, int maxChars)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);

        foreach (var rawLine in lines)
        {
            AppendChunkLine(current, chunks, rawLine);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        if (chunks.Count == 0 && !string.IsNullOrWhiteSpace(text))
            chunks.Add(text.Trim());

        return chunks;
    }

    private static ParsedDocumentModel MergeParsedDocuments(List<ParsedDocumentModel> parsedChunks)
    {
        var merged = new ParsedDocumentModel
        {
            Questions = []
        };

        foreach (var chunk in parsedChunks)
        {
            merged.UniqueReference ??= FirstNonEmpty(chunk.UniqueReference);
            merged.Company ??= FirstNonEmpty(chunk.Company);
            merged.Summary ??= FirstNonEmpty(chunk.Summary);
            merged.KeyInformation ??= FirstNonEmpty(chunk.KeyInformation);
            merged.Budget ??= FirstNonEmpty(chunk.Budget);
            merged.DeadlineForQualifying ??= FirstNonEmpty(chunk.DeadlineForQualifying);
            merged.DeadlineForSubmission ??= FirstNonEmpty(chunk.DeadlineForSubmission);
            merged.LengthOfContract ??= FirstNonEmpty(chunk.LengthOfContract);

            foreach (var question in chunk.Questions ?? [])
            {
                if (merged.Questions.Any(existing =>
                        string.Equals(existing.Number, question.Number, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.Title, question.Title, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.Description, question.Description, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                merged.Questions.Add(question);
            }
        }

        return merged;
    }

    private static string? FirstNonEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static ParsedDocumentModel? ApplyQuestionOrderIndices(ParsedDocumentModel? parsed)
    {
        if (parsed is not null && parsed.Questions is null)
            parsed.Questions = [];

        if (parsed?.Questions is { Count: > 0 })
        {
            for (var i = 0; i < parsed.Questions.Count; i++)
            {
                parsed.Questions[i].QuestionOrderIndex = i + 1;
            }
        }

        return parsed;
    }
}
