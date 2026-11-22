using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Application.Ingestion;
using PersonalResearchLab.Application.Text;
using PersonalResearchLab.Domain.Documents;

namespace PersonalResearchLab.Infrastructure.Ai;

/// <summary>
/// Generates and persists chunk embeddings.
/// <para>
/// Two behaviours matter here. First, chunks whose content hash already has an embedding are
/// skipped, so re-processing a document never pays for unchanged text. Second, when no embedding
/// provider is resolvable the coordinator reports that plainly instead of writing zero vectors,
/// which would silently poison vector search with meaningless similarity scores.
/// </para>
/// </summary>
public sealed class EmbeddingCoordinator : IEmbeddingCoordinator
{
    private const int BatchSize = 32;

    private readonly IAppDbContext _db;
    private readonly IAiProviderResolver _resolver;
    private readonly ILogger<EmbeddingCoordinator> _logger;

    public EmbeddingCoordinator(
        IAppDbContext db,
        IAiProviderResolver resolver,
        ILogger<EmbeddingCoordinator> logger)
    {
        _db = db;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<EmbeddingBatchResult> EmbedChunksAsync(
        Guid workspaceId,
        IReadOnlyList<(Guid ChunkId, string Text, string ContentHash)> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return EmbeddingBatchResult.Skipped("قطعه‌ای برای تعبیه برداری وجود ندارد");
        }

        var resolved = await _resolver.ResolveEmbeddingAsync(workspaceId, cancellationToken);
        if (resolved is null)
        {
            _logger.LogInformation(
                "No embedding provider is configured for workspace {WorkspaceId}; lexical search remains available",
                workspaceId);
            return EmbeddingBatchResult.Skipped(
                "رد شد: ارائه‌دهنده تعبیه برداری تنظیم نشده است (جست‌وجوی واژگانی همچنان کار می‌کند)");
        }

        var chunkIds = chunks.Select(c => c.ChunkId).ToList();

        // Reuse embeddings for identical content already stored in this workspace.
        var existingByHash = await _db.ChunkEmbeddings
            .Where(e => e.WorkspaceId == workspaceId)
            .Join(
                _db.DocumentChunks.Where(c => chunkIds.Contains(c.Id)),
                embedding => embedding.DocumentChunkId,
                chunk => chunk.Id,
                (embedding, chunk) => new { chunk.ContentHash, embedding.Vector, embedding.Dimensions })
            .ToListAsync(cancellationToken);

        var reusable = existingByHash
            .GroupBy(e => e.ContentHash, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var embedded = new HashSet<Guid>();
        var toEmbed = new List<(Guid ChunkId, string Text, string ContentHash)>();

        foreach (var chunk in chunks)
        {
            if (reusable.TryGetValue(chunk.ContentHash, out var cached) &&
                cached.Dimensions == resolved.Provider.Dimensions)
            {
                _db.ChunkEmbeddings.Add(new ChunkEmbedding
                {
                    DocumentChunkId = chunk.ChunkId,
                    WorkspaceId = workspaceId,
                    Dimensions = cached.Dimensions,
                    Vector = cached.Vector,
                    ModelName = resolved.Provider.ModelName
                });
                embedded.Add(chunk.ChunkId);
            }
            else
            {
                toEmbed.Add(chunk);
            }
        }

        var reusedCount = embedded.Count;

        for (var offset = 0; offset < toEmbed.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = toEmbed.Skip(offset).Take(BatchSize).ToList();
            IReadOnlyList<float[]> vectors;
            try
            {
                vectors = await resolved.Provider.EmbedAsync(
                    batch.Select(b => b.Text).ToList(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Embedding failure must not destroy the successful extraction and lexical index.
                _logger.LogError(ex, "Embedding batch failed; keeping lexical index only");
                return EmbeddingBatchResult.Failed(
                    embedded,
                    $"ناقص: {PersianFormat.Number(embedded.Count)} تعبیه شد، {PersianFormat.Number(toEmbed.Count - offset)} ناموفق ({ex.Message})");
            }

            for (var i = 0; i < batch.Count && i < vectors.Count; i++)
            {
                _db.ChunkEmbeddings.Add(new ChunkEmbedding
                {
                    DocumentChunkId = batch[i].ChunkId,
                    WorkspaceId = workspaceId,
                    Dimensions = vectors[i].Length,
                    Vector = ChunkEmbedding.Pack(vectors[i]),
                    ModelName = resolved.Provider.ModelName
                });
                embedded.Add(batch[i].ChunkId);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var summary = reusedCount > 0
            ? $"{PersianFormat.Number(embedded.Count)} تعبیه شد ({PersianFormat.Number(reusedCount)} مورد از محتوای یکسان بازاستفاده شد) با {resolved.Description}"
            : $"{PersianFormat.Number(embedded.Count)} تعبیه شد با {resolved.Description}";

        return new EmbeddingBatchResult(embedded, summary);
    }
}
