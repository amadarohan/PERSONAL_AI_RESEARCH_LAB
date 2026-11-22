using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Documents;

namespace PersonalResearchLab.Infrastructure.Retrieval;

/// <summary>
/// Vector index backed by the same SQL Server database.
/// <para>
/// SQL Server 2019 has no native vector type, so vectors are stored as packed float32 blobs and
/// scored in-process. This is a deliberate trade-off: it keeps the deployment to a single
/// database with no extra service, and it is fast enough for a personal corpus. It does not
/// scale to millions of chunks, which is documented in <c>docs/VECTOR_STORAGE.md</c>.
/// </para>
/// </summary>
public sealed class SqlVectorIndex : IVectorIndex
{
    private readonly IAppDbContext _db;

    public SqlVectorIndex(IAppDbContext db) => _db = db;

    public async Task UpsertAsync(Guid chunkId, Guid workspaceId, float[] vector, CancellationToken cancellationToken = default)
    {
        var existing = await _db.ChunkEmbeddings
            .FirstOrDefaultAsync(e => e.DocumentChunkId == chunkId, cancellationToken);

        if (existing is null)
        {
            _db.ChunkEmbeddings.Add(new ChunkEmbedding
            {
                DocumentChunkId = chunkId,
                WorkspaceId = workspaceId,
                Dimensions = vector.Length,
                Vector = ChunkEmbedding.Pack(vector)
            });
        }
        else
        {
            existing.Dimensions = vector.Length;
            existing.Vector = ChunkEmbedding.Pack(vector);
            existing.Touch();
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(Guid ChunkId, double Similarity)>> QueryAsync(
        Guid workspaceId,
        float[] vector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.ChunkEmbeddings
            .Where(e => e.WorkspaceId == workspaceId && e.Dimensions == vector.Length)
            .Select(e => new { e.DocumentChunkId, e.Vector })
            .ToListAsync(cancellationToken);

        var scored = new List<(Guid ChunkId, double Similarity)>(rows.Count);
        foreach (var row in rows)
        {
            scored.Add((row.DocumentChunkId, Cosine(vector, ChunkEmbedding.Unpack(row.Vector))));
        }

        return scored
            .OrderByDescending(s => s.Similarity)
            .Take(topK)
            .ToList();
    }

    public async Task DeleteForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var chunkIds = await _db.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (chunkIds.Count == 0)
        {
            return;
        }

        var embeddings = await _db.ChunkEmbeddings
            .Where(e => chunkIds.Contains(e.DocumentChunkId))
            .ToListAsync(cancellationToken);

        _db.ChunkEmbeddings.RemoveRange(embeddings);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return 0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA <= 0 || normB <= 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
