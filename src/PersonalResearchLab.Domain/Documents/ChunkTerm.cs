using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Documents;

/// <summary>
/// One row per (chunk, normalized term) pair, forming an inverted index.
/// <para>
/// Rationale: SQL Server full-text search on this machine only ships English and Arabic word
/// breakers, so Persian content tokenizes poorly. A maintained inverted index gives correct
/// Persian and English lexical search, is fully testable, and avoids a second database.
/// </para>
/// </summary>
public class ChunkTerm : Entity
{
    public Guid DocumentChunkId { get; set; }

    public Guid WorkspaceId { get; set; }

    /// <summary>Normalized term: lowercased, Persian characters unified, diacritics removed.</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>Occurrences of the term within the chunk.</summary>
    public int Frequency { get; set; }
}
