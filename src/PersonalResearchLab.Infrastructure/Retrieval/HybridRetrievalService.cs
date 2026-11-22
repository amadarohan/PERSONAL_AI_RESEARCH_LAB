using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Documents;

namespace PersonalResearchLab.Infrastructure.Retrieval;

/// <summary>
/// Hybrid retrieval combining a maintained inverted index (lexical) with dense vectors.
/// <para>
/// Scores are fused with reciprocal-rank fusion rather than raw score addition, because lexical
/// and cosine scores live on incomparable scales. Every returned passage carries the reason it
/// was retrieved so the UI can explain results instead of presenting a black box.
/// </para>
/// </summary>
public sealed class HybridRetrievalService : IRetrievalService
{
    private const int LexicalCandidateLimit = 60;
    private const int VectorCandidateLimit = 60;
    private const double RrfK = 60.0;

    private readonly IAppDbContext _db;
    private readonly ITextTokenizer _tokenizer;
    private readonly IAiProviderResolver _resolver;
    private readonly ILogger<HybridRetrievalService> _logger;

    public HybridRetrievalService(
        IAppDbContext db,
        ITextTokenizer tokenizer,
        IAiProviderResolver resolver,
        ILogger<HybridRetrievalService> logger)
    {
        _db = db;
        _tokenizer = tokenizer;
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedPassage>> SearchAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.QueryText))
        {
            return Array.Empty<RetrievedPassage>();
        }

        var workspaceIds = new List<Guid> { query.WorkspaceId };
        if (query.IncludeCrossWorkspace)
        {
            var permitted = await _db.Workspaces
                .Where(w => w.Id != query.WorkspaceId && w.AllowCrossWorkspaceAccess && !w.IsArchived)
                .Select(w => w.Id)
                .ToListAsync(cancellationToken);
            workspaceIds.AddRange(permitted);
        }

        var lexical = await LexicalSearchAsync(query, workspaceIds, cancellationToken);
        var vector = await VectorSearchAsync(query, workspaceIds, cancellationToken);

        var fused = Fuse(lexical, vector, query.MaxResults);
        _logger.LogDebug(
            "Retrieval for workspace {WorkspaceId}: {Lexical} lexical, {Vector} vector, {Fused} fused",
            query.WorkspaceId, lexical.Count, vector.Count, fused.Count);

        return fused;
    }

    private async Task<List<LexicalHit>> LexicalSearchAsync(
        RetrievalQuery query,
        List<Guid> workspaceIds,
        CancellationToken cancellationToken)
    {
        var terms = _tokenizer.Tokenize(query.QueryText).Distinct().Take(24).ToList();
        if (terms.Count == 0)
        {
            return new List<LexicalHit>();
        }

        var postings = await _db.ChunkTerms
            .Where(t => workspaceIds.Contains(t.WorkspaceId) && terms.Contains(t.Term))
            .Select(t => new { t.DocumentChunkId, t.Term, t.Frequency })
            .ToListAsync(cancellationToken);

        if (postings.Count == 0)
        {
            return new List<LexicalHit>();
        }

        // Weight rarer terms higher: a term matching 2 chunks is more informative than one matching 200.
        var documentFrequency = postings
            .GroupBy(p => p.Term)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DocumentChunkId).Distinct().Count());

        var totalChunks = Math.Max(1, await _db.DocumentChunks
            .CountAsync(c => workspaceIds.Contains(c.WorkspaceId), cancellationToken));

        var scores = new Dictionary<Guid, double>();
        foreach (var posting in postings)
        {
            var df = documentFrequency[posting.Term];
            var idf = Math.Log(1.0 + ((double)totalChunks / df));
            var contribution = (1.0 + Math.Log(posting.Frequency)) * idf;

            scores[posting.DocumentChunkId] = scores.TryGetValue(posting.DocumentChunkId, out var existing)
                ? existing + contribution
                : contribution;
        }

        var top = scores
            .OrderByDescending(kv => kv.Value)
            .Take(LexicalCandidateLimit)
            .ToList();

        var chunkIds = top.Select(t => t.Key).ToList();
        var chunks = await LoadChunksAsync(chunkIds, query, cancellationToken);

        return top
            .Where(t => chunks.ContainsKey(t.Key))
            .Select(t => new LexicalHit(t.Key, t.Value, chunks[t.Key]))
            .ToList();
    }

    private async Task<List<VectorHit>> VectorSearchAsync(
        RetrievalQuery query,
        List<Guid> workspaceIds,
        CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveEmbeddingAsync(query.WorkspaceId, cancellationToken);
        if (resolved is null)
        {
            return new List<VectorHit>();
        }

        float[] queryVector;
        try
        {
            var vectors = await resolved.Provider.EmbedAsync(new[] { query.QueryText }, cancellationToken);
            if (vectors.Count == 0)
            {
                return new List<VectorHit>();
            }

            queryVector = vectors[0];
        }
        catch (Exception ex)
        {
            // Vector search is an enhancement; lexical results must still be returned.
            _logger.LogWarning(ex, "Embedding the query failed; continuing with lexical results only");
            return new List<VectorHit>();
        }

        var embeddings = await _db.ChunkEmbeddings
            .Where(e => workspaceIds.Contains(e.WorkspaceId))
            .Select(e => new { e.DocumentChunkId, e.Vector, e.Dimensions })
            .ToListAsync(cancellationToken);

        if (embeddings.Count == 0)
        {
            return new List<VectorHit>();
        }

        var scored = new List<(Guid ChunkId, double Similarity)>();
        foreach (var embedding in embeddings)
        {
            if (embedding.Dimensions != queryVector.Length)
            {
                continue;
            }

            var vector = ChunkEmbedding.Unpack(embedding.Vector);
            scored.Add((embedding.DocumentChunkId, CosineSimilarity(queryVector, vector)));
        }

        var top = scored
            .Where(s => s.Similarity > 0.01)
            .OrderByDescending(s => s.Similarity)
            .Take(VectorCandidateLimit)
            .ToList();

        var chunkIds = top.Select(t => t.ChunkId).ToList();
        var chunks = await LoadChunksAsync(chunkIds, query, cancellationToken);

        return top
            .Where(t => chunks.ContainsKey(t.ChunkId))
            .Select(t => new VectorHit(t.ChunkId, t.Similarity, chunks[t.ChunkId]))
            .ToList();
    }

    private async Task<Dictionary<Guid, ChunkContext>> LoadChunksAsync(
        List<Guid> chunkIds,
        RetrievalQuery query,
        CancellationToken cancellationToken)
    {
        if (chunkIds.Count == 0)
        {
            return new Dictionary<Guid, ChunkContext>();
        }

        var rows = await (
                from chunk in _db.DocumentChunks
                join document in _db.Documents on chunk.DocumentId equals document.Id
                join version in _db.SourceVersions on document.SourceVersionId equals version.Id
                join source in _db.Sources on version.SourceId equals source.Id
                where chunkIds.Contains(chunk.Id)
                      && (query.SourceIdFilter == null || source.Id == query.SourceIdFilter)
                      && (query.DocumentIdFilter == null || document.Id == query.DocumentIdFilter)
                select new ChunkContext(
                    chunk.Id,
                    document.Id,
                    source.Id,
                    chunk.WorkspaceId,
                    source.Title,
                    document.Title,
                    chunk.PageNumber,
                    chunk.ChunkIndex,
                    chunk.Text))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.ChunkId);
    }

    private static List<RetrievedPassage> Fuse(
        List<LexicalHit> lexical,
        List<VectorHit> vector,
        int maxResults)
    {
        var lexicalRanks = lexical
            .Select((hit, index) => (hit.ChunkId, Rank: index + 1, hit.Score, hit.Context))
            .ToDictionary(x => x.ChunkId);

        var vectorRanks = vector
            .Select((hit, index) => (hit.ChunkId, Rank: index + 1, hit.Similarity, hit.Context))
            .ToDictionary(x => x.ChunkId);

        var allIds = lexicalRanks.Keys.Union(vectorRanks.Keys).ToList();
        var results = new List<RetrievedPassage>(allIds.Count);

        foreach (var id in allIds)
        {
            var hasLexical = lexicalRanks.TryGetValue(id, out var lex);
            var hasVector = vectorRanks.TryGetValue(id, out var vec);

            var fused = 0.0;
            var reasons = new List<string>();

            if (hasLexical)
            {
                fused += 1.0 / (RrfK + lex.Rank);
                reasons.Add("lexical match");
            }

            if (hasVector)
            {
                fused += 1.0 / (RrfK + vec.Rank);
                reasons.Add("semantic similarity");
            }

            var context = hasLexical ? lex.Context : vec.Context;

            results.Add(new RetrievedPassage(
                context.ChunkId,
                context.DocumentId,
                context.SourceId,
                context.WorkspaceId,
                context.SourceTitle,
                context.DocumentTitle,
                context.PageNumber,
                context.ChunkIndex,
                context.Text,
                hasLexical ? lex.Score : 0,
                hasVector ? vec.Similarity : 0,
                fused,
                string.Join(" + ", reasons)));
        }

        return results
            .OrderByDescending(r => r.FusedScore)
            .Take(maxResults)
            .ToList();
    }

    private static double CosineSimilarity(float[] a, float[] b)
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

        if (normA <= 0 || normB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private sealed record LexicalHit(Guid ChunkId, double Score, ChunkContext Context);

    private sealed record VectorHit(Guid ChunkId, double Similarity, ChunkContext Context);

    private sealed record ChunkContext(
        Guid ChunkId,
        Guid DocumentId,
        Guid SourceId,
        Guid WorkspaceId,
        string SourceTitle,
        string DocumentTitle,
        int PageNumber,
        int ChunkIndex,
        string Text);
}
