using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Evidence;

/// <summary>
/// A structured, atomic assertion extracted from a source (or authored by the user).
/// A source making a claim never makes the claim true; status is tracked separately.
/// </summary>
public class Claim : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Statement { get; set; } = string.Empty;

    public ClaimType ClaimType { get; set; } = ClaimType.Factual;

    public ClaimStatus Status { get; set; } = ClaimStatus.Unreviewed;

    public EpistemicStatus EpistemicStatus { get; set; } = EpistemicStatus.SourceReported;

    /// <summary>Source the claim was extracted from. Null for user-authored claims.</summary>
    public Guid? SourceId { get; set; }

    public Guid? DocumentChunkId { get; set; }

    public int? PageNumber { get; set; }

    /// <summary>Verbatim passage the claim was extracted from, for auditability.</summary>
    public string? SourceExcerpt { get; set; }

    /// <summary>Free-text temporal context, e.g. "as of 2024" or "Q3 2025".</summary>
    public string? TemporalContext { get; set; }

    public bool IsAiExtracted { get; set; }

    public string? UserAnnotation { get; set; }

    public DateTime? VerifiedAtUtc { get; set; }

    public ICollection<ClaimEvidence> Evidence { get; set; } = new List<ClaimEvidence>();
}

/// <summary>
/// A link between a claim and a specific passage that bears on it.
/// </summary>
public class ClaimEvidence : Entity
{
    public Guid ClaimId { get; set; }

    public Guid DocumentChunkId { get; set; }

    public Guid SourceId { get; set; }

    public int PageNumber { get; set; }

    public string Excerpt { get; set; } = string.Empty;

    public EvidenceStance Stance { get; set; } = EvidenceStance.Neutral;

    /// <summary>Why this passage was judged relevant. Kept as text, not a fabricated score.</summary>
    public string? Rationale { get; set; }

    public bool IsAiGenerated { get; set; }

    public Claim? Claim { get; set; }
}

/// <summary>
/// A detected conflict between two claims. Stored explicitly so contradictions are first-class
/// research objects rather than something the user has to remember.
/// </summary>
public class ClaimConflict : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public Guid ClaimAId { get; set; }

    public Guid ClaimBId { get; set; }

    public string Explanation { get; set; } = string.Empty;

    public bool IsResolved { get; set; }

    public string? Resolution { get; set; }
}
