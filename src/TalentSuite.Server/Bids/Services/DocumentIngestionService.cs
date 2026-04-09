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
        CancellationToken ct = default);
}

public sealed class DocumentIngestionService : IDocumentIngestionservice
{
    private const int MaxPromptChars = 80_000;
    private const int ExcelChunkChars = 20_000;
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

        if (IsExcelWorkbook(fileName))
        {
            var excelResult = await ExtractExcelDocumentAsync(documentStream, fileName, stage, ct);
            if (excelResult?.Questions is { Count: > 0 } || HasDocumentMetadata(excelResult))
                return ApplyQuestionOrderIndices(excelResult);
        }

        // 1) Extract text with Document Intelligence (prebuilt-read)
        var extractedText = await ExtractTextWithDocumentIntelligenceAsync(documentStream, ct);

        if (string.IsNullOrWhiteSpace(extractedText))
            return new ParsedDocumentModel();

        // Optional clamp to avoid huge prompts
        extractedText = ClampText(extractedText, maxChars: MaxPromptChars);

        // 2) Ask Azure OpenAI to produce strict JSON array
        var json = await ExtractQuestionsJsonWithAzureOpenAiAsync(extractedText, fileName, stage, ct);

        // 3) Parse into strongly typed list
        var parsed = JsonSerializer.Deserialize<ParsedDocumentModel>(json, SerialiserOptions.JsonOptions);
        return ApplyQuestionOrderIndices(parsed);
    }

    private async Task<ParsedDocumentModel?> ExtractExcelDocumentAsync(
        Stream documentStream,
        string fileName,
        BidStage stage,
        CancellationToken ct)
    {
        if (documentStream.CanSeek)
            documentStream.Position = 0;

        using var workbookStream = new MemoryStream();
        await documentStream.CopyToAsync(workbookStream, ct);
        workbookStream.Position = 0;

        var sheetTexts = ExtractWorkbookSheets(workbookStream);
        if (sheetTexts.Count == 0)
            return null;

        var merged = new ParsedDocumentModel
        {
            Questions = new List<ParsedQuestionModel>()
        };

        foreach (var sheetText in sheetTexts)
        {
            foreach (var chunk in ChunkExcelSheetText(sheetText.Content, ExcelChunkChars))
            {
                var json = await ExtractQuestionsJsonWithAzureOpenAiAsync(
                    chunk,
                    $"{fileName} [{sheetText.Name}]",
                    stage,
                    ct);

                var parsedChunk = JsonSerializer.Deserialize<ParsedDocumentModel>(json, SerialiserOptions.JsonOptions);
                MergeParsedDocument(merged, parsedChunk);
            }
        }

        return merged;
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

    private static bool IsExcelWorkbook(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDocumentMetadata(ParsedDocumentModel? parsed)
    {
        if (parsed is null)
            return false;

        return !string.IsNullOrWhiteSpace(parsed.UniqueReference)
               || !string.IsNullOrWhiteSpace(parsed.Company)
               || !string.IsNullOrWhiteSpace(parsed.Summary)
               || !string.IsNullOrWhiteSpace(parsed.KeyInformation)
               || !string.IsNullOrWhiteSpace(parsed.Budget)
               || !string.IsNullOrWhiteSpace(parsed.DeadlineForQualifying)
               || !string.IsNullOrWhiteSpace(parsed.DeadlineForSubmission)
               || !string.IsNullOrWhiteSpace(parsed.LengthOfContract);
    }

    private static ParsedDocumentModel? ApplyQuestionOrderIndices(ParsedDocumentModel? parsed)
    {
        if (parsed?.Questions is { Count: > 0 })
        {
            for (var i = 0; i < parsed.Questions.Count; i++)
            {
                parsed.Questions[i].QuestionOrderIndex = i + 1;
            }
        }

        return parsed;
    }

    private static List<WorkbookSheetText> ExtractWorkbookSheets(Stream workbookStream)
    {
        using var document = SpreadsheetDocument.Open(workbookStream, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart?.Workbook?.Sheets is null)
            return new List<WorkbookSheetText>();

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var result = new List<WorkbookSheetText>();

        foreach (var sheet in workbookPart.Workbook.Sheets.Elements<Sheet>())
        {
            if (string.IsNullOrWhiteSpace(sheet.Id?.Value)
                || workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheetPart)
                continue;

            var text = ExtractWorksheetText(worksheetPart, sharedStrings);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            result.Add(new WorkbookSheetText(sheet.Name?.Value ?? "Sheet", text));
        }

        return result;
    }

    private static string ExtractWorksheetText(WorksheetPart worksheetPart, SharedStringTable? sharedStrings)
    {
        var rows = worksheetPart.Worksheet.Descendants<Row>();
        var lines = new List<string>();

        foreach (var row in rows)
        {
            var values = row.Elements<Cell>()
                .Select(cell => GetCellText(cell, sharedStrings))
                .ToList();

            TrimTrailingEmptyValues(values);
            if (values.Count == 0 || values.All(string.IsNullOrWhiteSpace))
                continue;

            lines.Add(string.Join(" | ", values));
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var rawValue = cell.CellValue?.InnerText ?? cell.InnerText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
            return string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(rawValue, out var sharedStringIndex)
            && sharedStrings?.ElementAtOrDefault(sharedStringIndex) is SharedStringItem sharedString)
        {
            return sharedString.InnerText?.Trim() ?? string.Empty;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText?.Trim() ?? string.Empty;

        return rawValue.Trim();
    }

    private static void TrimTrailingEmptyValues(List<string> values)
    {
        for (var i = values.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                break;

            values.RemoveAt(i);
        }
    }

    private static IEnumerable<string> ChunkExcelSheetText(string sheetText, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(sheetText))
            yield break;

        var builder = new StringBuilder();
        foreach (var line in sheetText.Split(Environment.NewLine))
        {
            var normalizedLine = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(normalizedLine))
                continue;

            if (builder.Length > 0 && builder.Length + normalizedLine.Length + Environment.NewLine.Length > maxChars)
            {
                yield return builder.ToString().Trim();
                builder.Clear();
            }

            if (normalizedLine.Length > maxChars)
            {
                yield return ClampText(normalizedLine, maxChars);
                continue;
            }

            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(normalizedLine);
        }

        if (builder.Length > 0)
            yield return builder.ToString().Trim();
    }

    private static void MergeParsedDocument(ParsedDocumentModel target, ParsedDocumentModel? source)
    {
        if (source is null)
            return;

        target.UniqueReference ??= NullIfWhiteSpace(source.UniqueReference);
        target.Company ??= NullIfWhiteSpace(source.Company);
        target.Summary ??= NullIfWhiteSpace(source.Summary);
        target.KeyInformation ??= NullIfWhiteSpace(source.KeyInformation);
        target.Budget ??= NullIfWhiteSpace(source.Budget);
        target.DeadlineForQualifying ??= NullIfWhiteSpace(source.DeadlineForQualifying);
        target.DeadlineForSubmission ??= NullIfWhiteSpace(source.DeadlineForSubmission);
        target.LengthOfContract ??= NullIfWhiteSpace(source.LengthOfContract);

        if (source.Questions is { Count: > 0 })
            target.Questions.AddRange(source.Questions);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record WorkbookSheetText(string Name, string Content);
}
