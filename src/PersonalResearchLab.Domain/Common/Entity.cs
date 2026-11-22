namespace PersonalResearchLab.Domain.Common;

/// <summary>
/// Base type for all persisted aggregates and entities.
/// Timestamps are always stored in UTC; presentation converts to the configured timezone.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Optimistic concurrency token, mapped to SQL Server <c>rowversion</c>.</summary>
    public byte[]? RowVersion { get; set; }

    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

/// <summary>Marker for entities that belong to a research workspace and must be scoped by it.</summary>
public interface IWorkspaceScoped
{
    Guid WorkspaceId { get; set; }
}
