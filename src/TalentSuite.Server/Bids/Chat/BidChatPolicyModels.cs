namespace TalentSuite.Server.Bids.Chat;

public sealed class BidChatPolicyManifest
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public BidChatAgentSettings Agent { get; set; } = new();
    public BidChatPolicySettings Policy { get; set; } = new();
    public BidChatPolicyFiles Files { get; set; } = new();
}

public sealed class BidChatAgentSettings
{
    public string Object { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public BidChatReasoningSettings Reasoning { get; set; } = new();
}

public sealed class BidChatReasoningSettings
{
    public string Effort { get; set; } = string.Empty;
}

public sealed class BidChatPolicySettings
{
    public BidChatQuestionContextSettings QuestionContext { get; set; } = new();
    public BidChatOutputContractSettings OutputContract { get; set; } = new();
    public BidChatValidationSettings Validations { get; set; } = new();
}

public sealed class BidChatOutputContractSettings
{
    public string ResponseFormat { get; set; } = "json";
    public string RootProperty { get; set; } = "answerText";
}

public sealed class BidChatPolicyFiles
{
    public string Instructions { get; set; } = string.Empty;
    public string Tools { get; set; } = string.Empty;
    public string Guardrails { get; set; } = string.Empty;
    public string Evaluations { get; set; } = string.Empty;
}

public sealed class BidChatQuestionContextSettings
{
    public string Placement { get; set; } = "append_to_system_prompt";
    public string Heading { get; set; } = "Question Context";
}

public sealed class BidChatValidationSettings
{
    public bool RequireUserPrompt { get; set; }
    public bool RequireBidId { get; set; }
    public bool RequireQuestionId { get; set; }
    public bool RequireQuestionDescription { get; set; }
    public bool RequireCitations { get; set; }
    public int MinimumCitationCount { get; set; }
    public bool EnforceBidLibraryOnly { get; set; }
    public bool DisallowCrossProjectMerging { get; set; }
    public bool DisallowLowScoringExamples { get; set; }
    public int MaxResponseCharacters { get; set; }
    public string EmptyResponseMessage { get; set; } = "The assistant returned an empty answer. Please try again.";
    public string MissingCitationsMessage { get; set; } = "The assistant response did not include supporting citations from the bid library.";
    public string MissingQuestionMessage { get; set; } = "The selected question is missing the context required to answer it.";
}

public sealed class BidChatToolsDefinition
{
    public List<string> Tools { get; set; } = [];
    public Dictionary<string, BidChatToolUsageDefinition> ToolUsage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BidChatToolUsageDefinition
{
    public string Purpose { get; set; } = string.Empty;
}

public sealed class BidChatPolicyDefinition
{
    public required BidChatPolicyManifest Manifest { get; init; }
    public required BidChatToolsDefinition Tools { get; init; }
    public required string Instructions { get; init; }
    public required string GuardrailsMarkdown { get; init; }
    public required string EvaluationsMarkdown { get; init; }
}

public sealed class BidChatStructuredResponse
{
    public string AnswerText { get; set; } = string.Empty;
}
