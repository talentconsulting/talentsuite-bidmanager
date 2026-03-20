using Microsoft.Extensions.Logging;
using TalentSuite.Functions.StoringBids.Storage;
using TalentSuite.Shared.Bids;

namespace TalentSuite.Functions.StoringBids.BidLibrary;

public sealed class BidLibraryWriter(
    IAzureBlobStorageService blobStorageService,
    ILogger<BidLibraryWriter> logger) : IBidLibraryWriter
{
    public async Task WriteBidAsync(
        BidResponse bid,
        IReadOnlyDictionary<string, string> finalAnswerTextByQuestionId,
        CancellationToken ct = default)
    {
        if (bid is null)
            throw new ArgumentNullException(nameof(bid));
        if (finalAnswerTextByQuestionId is null)
            throw new ArgumentNullException(nameof(finalAnswerTextByQuestionId));

        var questions = bid.Questions ?? new List<QuestionResponse>();
        if (questions.Count == 0)
        {
            logger.LogInformation("Bid {BidId} has no questions. Nothing to write to bid library.", bid.Id);
            return;
        }

        var bidIdForLogs = bid.Id ?? string.Empty;
        var directoryName = SanitizePathSegment(string.IsNullOrWhiteSpace(bid.Company) ? bidIdForLogs : bid.Company!);
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var question in questions.Where(q => !string.IsNullOrWhiteSpace(q.Id)))
        {
            finalAnswerTextByQuestionId.TryGetValue(question.Id, out var answerText);
            answerText ??= string.Empty;

            var rawFileName = SanitizePathSegment(question.Title);
            if (string.IsNullOrWhiteSpace(rawFileName))
                rawFileName = $"question-{SanitizePathSegment(question.Number)}";
            if (string.IsNullOrWhiteSpace(rawFileName))
                rawFileName = $"question-{SanitizePathSegment(question.Id)}";

            var uniqueFileName = EnsureUniqueFileName(rawFileName, usedFileNames);
            var blobName = $"{directoryName}/{uniqueFileName}.txt";
            await blobStorageService.WriteTextAsync(
                "bidlibrary",
                blobName,
                answerText,
                ct);
        }

        logger.LogInformation(
            "Bid {BidId} pushed to blob container {Container}. Directory {Directory} with {Count} file(s).",
            bidIdForLogs,
            "bidlibrary",
            directoryName,
            usedFileNames.Count);
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars().Concat(['/', '\\']).Distinct().ToArray();
        var sanitized = value.Trim();
        foreach (var invalidChar in invalidChars)
            sanitized = sanitized.Replace(invalidChar, '-');

        while (sanitized.Contains("  ", StringComparison.Ordinal))
            sanitized = sanitized.Replace("  ", " ", StringComparison.Ordinal);

        return sanitized.Trim().Trim('.');
    }

    private static string EnsureUniqueFileName(string baseName, ISet<string> usedNames)
    {
        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            candidate = $"{baseName}-{suffix}";
            suffix++;
        }

        return candidate;
    }
}
