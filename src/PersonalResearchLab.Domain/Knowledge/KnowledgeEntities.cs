using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Knowledge;

/// <summary>
/// A node in the knowledge graph. Nodes are workspace-scoped by default; cross-workspace
/// exploration is an explicit permission on the workspace, not an implicit behaviour.
/// </summary>
public class KnowledgeNode : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public KnowledgeNodeKind Kind { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Normalized lowercase name used for candidate matching. Never used to auto-merge.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Number of distinct sources that mention this node.</summary>
    public int SourceCount { get; set; }

    public bool IsUserConfirmed { get; set; }

    /// <summary>Set when the user merged another node into this one, for auditability.</summary>
    public Guid? MergedIntoNodeId { get; set; }
}

public class KnowledgeRelationship : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public Guid FromNodeId { get; set; }

    public Guid ToNodeId { get; set; }

    public KnowledgeRelationKind Kind { get; set; }

    public string? Description { get; set; }

    /// <summary>True only when the user explicitly confirmed the relationship.</summary>
    public bool IsUserConfirmed { get; set; }

    public bool IsAiSuggested { get; set; }

    public ICollection<KnowledgeRelationshipEvidence> Evidence { get; set; } =
        new List<KnowledgeRelationshipEvidence>();
}

/// <summary>Provenance for a relationship. A relationship without evidence is a hypothesis.</summary>
public class KnowledgeRelationshipEvidence : Entity
{
    public Guid KnowledgeRelationshipId { get; set; }

    public Guid DocumentChunkId { get; set; }

    public Guid SourceId { get; set; }

    public string Excerpt { get; set; } = string.Empty;

    public KnowledgeRelationship? KnowledgeRelationship { get; set; }
}
