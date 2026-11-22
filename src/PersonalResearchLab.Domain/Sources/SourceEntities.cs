using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Sources;

/// <summary>
/// A collected information source. A source is a container for one or more versions;
/// re-importing the same file creates a new version rather than a duplicate source.
/// </summary>
public class Source : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public SourceKind Kind { get; set; }

    public string? Author { get; set; }

    public string? Publisher { get; set; }

    public string? Url { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    public DateTime RetrievedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>BCP-47 language tag detected or supplied for the source content.</summary>
    public string? Language { get; set; }

    /// <summary>SHA-256 of the original bytes, used for duplicate detection.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string? OriginalFileName { get; set; }

    public string? Tags { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>
    /// User-assigned reliability rating, 0-100, or null when unrated.
    /// This is a human judgement and is never presented as a computed probability.
    /// </summary>
    public int? ReliabilityRating { get; set; }

    public string? ReliabilityRationale { get; set; }

    public DateTime? LastVerifiedAtUtc { get; set; }

    public ICollection<SourceVersion> Versions { get; set; } = new List<SourceVersion>();
}

/// <summary>
/// One immutable snapshot of a source's bytes plus the processing state for that snapshot.
/// </summary>
public class SourceVersion : Entity
{
    public Guid SourceId { get; set; }

    public int VersionNumber { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Relative path inside the managed storage root. Never an absolute path.</summary>
    public string StoragePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public ProcessingStatus Status { get; set; } = ProcessingStatus.Pending;

    /// <summary>Coarse extraction quality signal, 0-100, derived from extracted character count.</summary>
    public int? ExtractionQuality { get; set; }

    public string? ProcessingError { get; set; }

    public Source? Source { get; set; }

    public Documents.Document? Document { get; set; }
}
