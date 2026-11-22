namespace PersonalResearchLab.Application.Abstractions;

/// <summary>
/// A retrieved passage with full provenance. Retrieval returns evidence, not anonymous text,
/// so every answer can be traced back to a page.
/// </summary>
public sealed record RetrievedPassage(
    Guid ChunkId,
    Guid DocumentId,
    Guid SourceId,
    Guid WorkspaceId,
    string SourceTitle,
    string DocumentTitle,
    int PageNumber,
    int ChunkIndex,
    string Text,
    double LexicalScore,
    double VectorScore,
    double FusedScore,
    string RetrievalReason);

public sealed record RetrievalQuery(
    Guid WorkspaceId,
    string QueryText,
    int MaxResults = 8,
    bool IncludeCrossWorkspace = false,
    Guid? SourceIdFilter = null,
    Guid? DocumentIdFilter = null);

public interface IRetrievalService
{
    Task<IReadOnlyList<RetrievedPassage>> SearchAsync(RetrievalQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Vector index abstraction. The default implementation stores vectors in SQL Server.</summary>
public interface IVectorIndex
{
    Task UpsertAsync(Guid chunkId, Guid workspaceId, float[] vector, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Guid ChunkId, double Similarity)>> QueryAsync(
        Guid workspaceId,
        float[] vector,
        int topK,
        CancellationToken cancellationToken = default);

    Task DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
