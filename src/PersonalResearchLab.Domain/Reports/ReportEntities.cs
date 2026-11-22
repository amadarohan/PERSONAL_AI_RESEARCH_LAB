using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Reports;

public class ResearchReport : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Template { get; set; } = "technical";

    public Guid? ResearchRunId { get; set; }

    public int CurrentVersionNumber { get; set; } = 1;

    public ICollection<ResearchReportVersion> Versions { get; set; } = new List<ResearchReportVersion>();
}

/// <summary>
/// Immutable snapshot of a report. Reports are reproducible because each version stores the
/// exact markdown plus the citation set it was generated from.
/// </summary>
public class ResearchReportVersion : Entity
{
    public Guid ResearchReportId { get; set; }

    public int VersionNumber { get; set; }

    public string Markdown { get; set; } = string.Empty;

    /// <summary>JSON array of citations: sourceId, chunkId, pageNumber, label.</summary>
    public string CitationsJson { get; set; } = "[]";

    public string? ChangeNote { get; set; }

    public ResearchReport? ResearchReport { get; set; }
}
