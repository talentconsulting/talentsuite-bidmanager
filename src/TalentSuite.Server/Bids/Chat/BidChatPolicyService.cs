using System.Text;
using System.Text.Json;
using TalentSuite.Server.Bids.Services;
using TalentSuite.Server.Bids.Services.Models;
using TalentSuite.Shared.Bids.Ai;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Text.RegularExpressions;

namespace TalentSuite.Server.Bids.Chat;

public interface IBidChatPolicyService
{
    Task<BidChatPolicyDefinition> GetBidQuestionAnsweringPolicyAsync(CancellationToken ct = default);
    string BuildSystemPrompt(BidChatPolicyDefinition policy, CreateQuestionModel question);
    string ExtractAnswerText(BidChatPolicyDefinition policy, string rawResponse);
    void ValidateRequest(BidChatPolicyDefinition policy, string bidId, string questionId, string userPrompt, CreateQuestionModel question);
    void ValidateResponse(BidChatPolicyDefinition policy, string response, IReadOnlyCollection<ChatSourceReferenceResponse> sources);
}

public sealed class BidChatPolicyService : IBidChatPolicyService
{
    private const string ManifestPath = "Configuration/Chat/manifest.yaml";
    private static readonly Regex InlineCitationPattern = new(
        @"\[[^\]]+\]|\u3010[^\u3011]+\u3011",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<BidChatPolicyService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly IDeserializer _yamlDeserializer;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private BidChatPolicyDefinition? _cachedPolicy;

    public BidChatPolicyService(IWebHostEnvironment environment, ILogger<BidChatPolicyService> logger)
    {
        _environment = environment;
        _logger = logger;
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public async Task<BidChatPolicyDefinition> GetBidQuestionAnsweringPolicyAsync(CancellationToken ct = default)
    {
        if (_cachedPolicy is not null)
            return _cachedPolicy;

        await _loadLock.WaitAsync(ct);
        try
        {
            if (_cachedPolicy is not null)
                return _cachedPolicy;

            var manifest = await ReadYamlFileAsync<BidChatPolicyManifest>(ManifestPath, ct)
                           ?? throw new InvalidOperationException("Bid chat manifest could not be loaded.");

            var instructions = await ReadTextFileAsync(ToChatPath(manifest.Files.Instructions), ct);
            var guardrailsMarkdown = await ReadTextFileAsync(ToChatPath(manifest.Files.Guardrails), ct);
            var evaluationsMarkdown = await ReadTextFileAsync(ToChatPath(manifest.Files.Evaluations), ct);
            var tools = await ReadYamlFileAsync<BidChatToolsDefinition>(ToChatPath(manifest.Files.Tools), ct)
                        ?? new BidChatToolsDefinition();

            _cachedPolicy = new BidChatPolicyDefinition
            {
                Manifest = manifest,
                Tools = tools,
                Instructions = instructions.Trim(),
                GuardrailsMarkdown = guardrailsMarkdown.Trim(),
                EvaluationsMarkdown = evaluationsMarkdown.Trim()
            };

            _logger.LogInformation(
                "Loaded bid chat policy {PolicyName} version {Version}.",
                manifest.Name,
                manifest.Version);

            return _cachedPolicy;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public string BuildSystemPrompt(BidChatPolicyDefinition policy, CreateQuestionModel question)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(question);

        var prompt = new StringBuilder();
        prompt.AppendLine(policy.Instructions);
        prompt.AppendLine();
        prompt.AppendLine(policy.GuardrailsMarkdown);
        prompt.AppendLine();
        prompt.AppendLine($"{policy.Manifest.Policy.QuestionContext.Heading}:");
        prompt.AppendLine($"- Number: {ValueOrFallback(question.Number, "(not provided)")}");
        prompt.AppendLine($"- Title: {ValueOrFallback(question.Title, "(not provided)")}");
        prompt.AppendLine($"- Category: {ValueOrFallback(question.Category, "(not provided)")}");
        prompt.AppendLine($"- Description: {ValueOrFallback(question.Description, "(not provided)")}");
        prompt.AppendLine($"- Length guidance: {ValueOrFallback(question.Length, "(not provided)")}");
        prompt.AppendLine($"- Weighting: {question.Weighting}");
        prompt.AppendLine($"- Required: {question.Required}");
        prompt.AppendLine($"- Nice to have: {question.NiceToHave}");
        prompt.AppendLine();
        prompt.AppendLine("Final Markdown formatting requirements:");
        prompt.AppendLine("- Use ## for the answer title and ### for section headings; never use bold text as a heading.");
        prompt.AppendLine("- Put a blank line after every heading before its content.");
        prompt.AppendLine("- A heading line must contain only the heading title; never concatenate paragraph text onto it.");
        prompt.AppendLine("- Never bold an entire paragraph.");
        prompt.AppendLine("- Correct: ### Introduction\\n\\nOur organisation has a proven track record...");
        prompt.AppendLine("- Invalid: **IntroductionOur organisation has a proven track record...**");

        return prompt.ToString().Trim();
    }

    public string ExtractAnswerText(BidChatPolicyDefinition policy, string rawResponse)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(rawResponse))
            return string.Empty;

        var rootProperty = policy.Manifest.Policy.OutputContract.RootProperty;
        if (string.IsNullOrWhiteSpace(rootProperty))
            return CleanAssistantText(rawResponse);

        var candidateJson = ExtractJsonObject(rawResponse);
        if (string.IsNullOrWhiteSpace(candidateJson))
            return CleanAssistantText(rawResponse);

        try
        {
            using var document = JsonDocument.Parse(candidateJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return CleanAssistantText(rawResponse);

            if (!TryGetPropertyIgnoreCase(document.RootElement, rootProperty, out var answerElement))
                return CleanAssistantText(rawResponse);

            var extractedText = answerElement.ValueKind == JsonValueKind.String
                ? answerElement.GetString() ?? string.Empty
                : answerElement.ToString();

            return string.IsNullOrWhiteSpace(extractedText)
                ? CleanAssistantText(rawResponse)
                : CleanAssistantText(extractedText);
        }
        catch (JsonException)
        {
            return CleanAssistantText(rawResponse);
        }
    }

    public void ValidateRequest(
        BidChatPolicyDefinition policy,
        string bidId,
        string questionId,
        string userPrompt,
        CreateQuestionModel question)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(question);

        var validations = policy.Manifest.Policy.Validations;

        if (validations.RequireBidId && string.IsNullOrWhiteSpace(bidId))
            throw new ChatServiceUserException("A bid id is required for chat.", StatusCodes.Status400BadRequest);

        if (validations.RequireQuestionId && string.IsNullOrWhiteSpace(questionId))
            throw new ChatServiceUserException("A question id is required for chat.", StatusCodes.Status400BadRequest);

        if (validations.RequireUserPrompt && string.IsNullOrWhiteSpace(userPrompt))
            throw new ChatServiceUserException("Please enter a question before starting chat.", StatusCodes.Status400BadRequest);

        if (validations.RequireQuestionDescription && string.IsNullOrWhiteSpace(question.Description))
            throw new ChatServiceUserException(validations.MissingQuestionMessage, StatusCodes.Status400BadRequest);
    }

    public void ValidateResponse(BidChatPolicyDefinition policy, string response, IReadOnlyCollection<ChatSourceReferenceResponse> sources)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var validations = policy.Manifest.Policy.Validations;

        if (string.IsNullOrWhiteSpace(response))
            throw new ChatServiceUserException(validations.EmptyResponseMessage, StatusCodes.Status502BadGateway);

        if (validations.MaxResponseCharacters > 0 && response.Length > validations.MaxResponseCharacters)
            throw new ChatServiceUserException("The assistant response exceeded the allowed response size.", StatusCodes.Status502BadGateway);

        var minimumCitationCount = Math.Max(1, validations.MinimumCitationCount);
        var sourceCount = sources?.Count ?? 0;
        var inlineCitationCount = CountInlineCitations(response);

        if (validations.RequireCitations
            && sourceCount < minimumCitationCount
            && inlineCitationCount < minimumCitationCount)
        {
            _logger.LogWarning(
                "Bid chat response did not satisfy citation policy. Structured sources: {SourceCount}. Inline citations: {InlineCitationCount}.",
                sourceCount,
                inlineCitationCount);
        }
    }

    private async Task<T?> ReadYamlFileAsync<T>(string relativePath, CancellationToken ct)
    {
        var contents = await ReadTextFileAsync(relativePath, ct);
        return _yamlDeserializer.Deserialize<T>(contents);
    }

    private async Task<string> ReadTextFileAsync(string relativePath, CancellationToken ct)
    {
        var absolutePath = ResolvePath(relativePath);
        using var reader = File.OpenText(absolutePath);
        using var registration = ct.Register(reader.Dispose);
        return await reader.ReadToEndAsync(ct);
    }

    private string ResolvePath(string relativePath)
    {
        var absolutePath = Path.Combine(_environment.ContentRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
            throw new FileNotFoundException($"Bid chat policy asset was not found: {relativePath}", absolutePath);

        return absolutePath;
    }

    private static string ToChatPath(string fileName)
        => string.IsNullOrWhiteSpace(fileName) ? string.Empty : $"Configuration/Chat/{fileName}";

    private static string ValueOrFallback(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string ExtractJsonObject(string rawResponse)
    {
        var normalized = StripWrappingCodeFence(rawResponse);

        var firstBrace = normalized.IndexOf('{');
        var lastBrace = normalized.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace < firstBrace)
            return string.Empty;

        return normalized[firstBrace..(lastBrace + 1)];
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int CountInlineCitations(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return 0;

        return InlineCitationPattern.Matches(response).Count;
    }

    private static string StripWrappingCodeFence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim();
        if (!normalized.StartsWith("```", StringComparison.Ordinal))
            return normalized;

        var firstNewLine = normalized.IndexOf('\n');
        if (firstNewLine < 0)
            return normalized;

        var trailingFenceIndex = normalized.LastIndexOf("```", StringComparison.Ordinal);
        if (trailingFenceIndex <= firstNewLine)
            return normalized;

        var innerContent = normalized[(firstNewLine + 1)..trailingFenceIndex].Trim();
        return string.IsNullOrWhiteSpace(innerContent) ? normalized : innerContent;
    }

    private static string CleanAssistantText(string value)
    {
        var withoutFence = StripWrappingCodeFence(value);
        if (string.IsNullOrWhiteSpace(withoutFence))
            return string.Empty;

        var withoutInlineCitations = InlineCitationPattern.Replace(withoutFence, string.Empty);
        return Regex.Replace(withoutInlineCitations, @"[ \t]+\n", "\n").Trim();
    }
}
