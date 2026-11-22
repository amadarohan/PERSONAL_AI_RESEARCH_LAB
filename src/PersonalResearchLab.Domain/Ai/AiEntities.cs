using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Ai;

/// <summary>
/// A configured AI endpoint. Secrets are never stored here; only a reference to a protected
/// secret entry is kept so the database can be backed up without leaking credentials.
/// </summary>
public class AiProvider : Entity
{
    public string Name { get; set; } = string.Empty;

    public AiProviderKind Kind { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Name of the secret entry in the protected secret store. Never the key itself.</summary>
    public string? ApiKeySecretName { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether this provider may receive workspace content.</summary>
    public DataRoute DataRoute { get; set; } = DataRoute.LocalOnly;

    public DateTime? LastValidatedAtUtc { get; set; }

    public string? LastValidationResult { get; set; }
}

public class AiModelConfiguration : Entity
{
    public Guid AiProviderId { get; set; }

    public AiCapability Capability { get; set; }

    public string ModelName { get; set; } = string.Empty;

    public double Temperature { get; set; } = 0.2;

    public int MaxOutputTokens { get; set; } = 2048;

    public int ContextWindow { get; set; } = 8192;

    public int TimeoutSeconds { get; set; } = 120;

    public int MaxRetries { get; set; } = 2;

    /// <summary>Embedding vector width; 0 for non-embedding capabilities.</summary>
    public int Dimensions { get; set; }

    public bool IsDefault { get; set; }

    public AiProvider? AiProvider { get; set; }
}

public class AiConversation : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Optional context anchor, e.g. a document or graph node the conversation is about.</summary>
    public string? ContextKind { get; set; }

    public Guid? ContextId { get; set; }

    public ICollection<AiMessage> Messages { get; set; } = new List<AiMessage>();
}

public class AiMessage : Entity
{
    public Guid AiConversationId { get; set; }

    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    /// <summary>JSON array of citation objects: chunkId, sourceId, pageNumber, excerpt.</summary>
    public string? CitationsJson { get; set; }

    /// <summary>True when the assistant explicitly stated it lacked sufficient information.</summary>
    public bool IsInsufficientEvidence { get; set; }

    public string? ModelName { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public AiConversation? AiConversation { get; set; }
}

/// <summary>Audit record for every AI call. Required for transparency and cost tracking.</summary>
public class AiExecution : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public DataRoute DataRoute { get; set; }

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public long DurationMs { get; set; }

    public string? Error { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }
}

/// <summary>
/// A proposed persistent mutation produced by AI. Nothing is written until the user approves,
/// which is the core guardrail against autonomous data corruption.
/// </summary>
public class AiProposal : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string ProposalType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public bool IsApproved { get; set; }

    public bool IsRejected { get; set; }

    public DateTime? DecidedAtUtc { get; set; }
}
