using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.System;

/// <summary>
/// A unit of background work. Progress is reported from real stage transitions, never invented.
/// </summary>
public class ProcessingJob : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string JobType { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    /// <summary>Completed stages divided by total stages, expressed 0-100 from real stage counts.</summary>
    public int ProgressPercent { get; set; }

    public string? CurrentStage { get; set; }

    public int RetryCount { get; set; }

    public string? FailureReason { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Set when the user requested cancellation; the worker observes it cooperatively.</summary>
    public bool CancellationRequested { get; set; }

    public Guid? RelatedEntityId { get; set; }

    public ICollection<ProcessingJobStep> Steps { get; set; } = new List<ProcessingJobStep>();
}

public class ProcessingJobStep : Entity
{
    public Guid ProcessingJobId { get; set; }

    public int Order { get; set; }

    public string Name { get; set; } = string.Empty;

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? Detail { get; set; }

    public ProcessingJob? ProcessingJob { get; set; }
}

public class AppSetting : Entity
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public class AuditLog : Entity
{
    public string Category { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? Detail { get; set; }

    public Guid? WorkspaceId { get; set; }
}
