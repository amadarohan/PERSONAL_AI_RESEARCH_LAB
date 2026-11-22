using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Research;

/// <summary>
/// An independent research environment. Everything the user collects lives inside a workspace,
/// which is the unit of scoping for search, AI context, and cross-workspace permissions.
/// </summary>
public class ResearchWorkspace : Entity
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>The single sentence the workspace exists to answer.</summary>
    public string? Objective { get; set; }

    public string AccentColor { get; set; } = "#22D3EE";

    public bool IsArchived { get; set; }

    /// <summary>Free-form instructions injected into every AI call scoped to this workspace.</summary>
    public string? AiInstructions { get; set; }

    /// <summary>BCP-47 tag used for extraction and answer language, e.g. <c>fa-IR</c>.</summary>
    public string PreferredLanguage { get; set; } = "fa-IR";

    /// <summary>When false, this workspace's content is never sent to a cloud provider.</summary>
    public DataRoute DataRoute { get; set; } = DataRoute.LocalOnly;

    /// <summary>When true, other workspaces may retrieve this workspace's knowledge.</summary>
    public bool AllowCrossWorkspaceAccess { get; set; }

    public ICollection<ResearchQuestion> Questions { get; set; } = new List<ResearchQuestion>();

    public ICollection<ResearchRun> Runs { get; set; } = new List<ResearchRun>();

    public ICollection<ResearchNote> Notes { get; set; } = new List<ResearchNote>();
}

public class ResearchQuestion : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Text { get; set; } = string.Empty;

    public string? Rationale { get; set; }

    public bool IsAnswered { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Set when the question was produced by the planner rather than typed by the user.</summary>
    public bool IsAiGenerated { get; set; }

    public ResearchWorkspace? Workspace { get; set; }
}

/// <summary>
/// A single observable execution of the deep research engine. The plan is stored separately
/// so the user can inspect and edit it before approving execution.
/// </summary>
public class ResearchRun : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Objective { get; set; } = string.Empty;

    public ResearchRunStatus Status { get; set; } = ResearchRunStatus.Draft;

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>True only when a real web search provider executed during this run.</summary>
    public bool UsedWebSearch { get; set; }

    public string? WebSearchProvider { get; set; }

    public ResearchWorkspace? Workspace { get; set; }

    public ICollection<ResearchPlanStep> PlanSteps { get; set; } = new List<ResearchPlanStep>();

    public ICollection<ResearchFinding> Findings { get; set; } = new List<ResearchFinding>();
}

public class ResearchPlanStep : Entity
{
    public Guid ResearchRunId { get; set; }

    public int Order { get; set; }

    public ResearchPlanStepKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? OutputSummary { get; set; }

    public string? FailureReason { get; set; }

    public ResearchRun? ResearchRun { get; set; }
}

public class ResearchFinding : Entity
{
    public Guid ResearchRunId { get; set; }

    public string Statement { get; set; } = string.Empty;

    public EpistemicStatus EpistemicStatus { get; set; } = EpistemicStatus.AiInterpretation;

    /// <summary>Chunk ids backing this finding. Empty means the finding is unsupported and must be labelled so.</summary>
    public string EvidenceChunkIds { get; set; } = string.Empty;

    public string? Caveat { get; set; }

    public ResearchRun? ResearchRun { get; set; }
}

public class ResearchNote : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public string? Tags { get; set; }

    public ResearchWorkspace? Workspace { get; set; }
}

/// <summary>Append-only timeline entry used by the research timeline view.</summary>
public class ResearchTimelineEvent : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public Guid? RelatedEntityId { get; set; }
}
