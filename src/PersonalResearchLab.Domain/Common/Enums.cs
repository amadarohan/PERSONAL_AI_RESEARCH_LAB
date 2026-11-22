namespace PersonalResearchLab.Domain.Common;

/// <summary>
/// Epistemic classification of a statement. The product must never blur these categories,
/// because the whole value of the lab depends on the user knowing what kind of knowledge they hold.
/// </summary>
public enum EpistemicStatus
{
    /// <summary>Directly observed or verified by the user against a primary source.</summary>
    Verified = 0,

    /// <summary>Reported by a source; the source is the authority, not the lab.</summary>
    SourceReported = 1,

    /// <summary>Produced by an AI model as an interpretation of retrieved material.</summary>
    AiInterpretation = 2,

    /// <summary>Derived by explicit logical inference from stated premises.</summary>
    LogicalInference = 3,

    /// <summary>Proposed explanation that is not yet supported by evidence.</summary>
    Hypothesis = 4,

    /// <summary>Known to be unknown, or insufficiently supported to state.</summary>
    Unknown = 5
}

public enum ClaimType
{
    Factual = 0,
    Statistical = 1,
    Causal = 2,
    Predictive = 3,
    Interpretive = 4,
    Normative = 5,
    Hypothetical = 6
}

public enum ClaimStatus
{
    Unreviewed = 0,
    Supported = 1,
    PartiallySupported = 2,
    Contested = 3,
    Contradicted = 4,
    Outdated = 5,
    Unverifiable = 6
}

/// <summary>
/// How strongly a piece of evidence bears on a claim. Deliberately ordinal and coarse:
/// the product must not present subjective judgement as mathematical precision.
/// </summary>
public enum EvidenceStance
{
    Supports = 0,
    PartiallySupports = 1,
    Neutral = 2,
    PartiallyContradicts = 3,
    Contradicts = 4
}

public enum SourceKind
{
    Pdf = 0,
    Docx = 1,
    Text = 2,
    Markdown = 3,
    Html = 4,
    Csv = 5,
    Json = 6,
    Image = 7,
    WebPage = 8,
    ManualNote = 9,
    StructuredImport = 10
}

public enum ProcessingStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Skipped = 5
}

public enum KnowledgeNodeKind
{
    Concept = 0,
    Person = 1,
    Organization = 2,
    Technology = 3,
    Event = 4,
    Document = 5,
    Claim = 6,
    ResearchQuestion = 7,
    Topic = 8
}

public enum KnowledgeRelationKind
{
    Supports = 0,
    Contradicts = 1,
    DependsOn = 2,
    RelatedTo = 3,
    DerivedFrom = 4,
    Mentions = 5,
    Explains = 6,
    Causes = 7
}

public enum ResearchPlanStepKind
{
    ClarifyObjective = 0,
    DecomposeQuestions = 1,
    InventoryKnowledge = 2,
    RetrieveEvidence = 3,
    EvaluateEvidence = 4,
    DetectContradictions = 5,
    FollowUpQuestions = 6,
    Synthesize = 7,
    DraftReport = 8
}

public enum ResearchRunStatus
{
    Draft = 0,
    AwaitingApproval = 1,
    Running = 2,
    Paused = 3,
    Completed = 4,
    Cancelled = 5,
    Failed = 6
}

public enum AiProviderKind
{
    OpenAiCompatible = 0,
    Ollama = 1,
    AzureOpenAi = 2,
    DeterministicFake = 3
}

public enum AiCapability
{
    Chat = 0,
    Embedding = 1,
    Reranking = 2,
    Ocr = 3,
    Transcription = 4
}

/// <summary>Where a given AI call is allowed to run. Surfaced in the UI so routing is never silent.</summary>
public enum DataRoute
{
    LocalOnly = 0,
    CloudAllowed = 1
}
