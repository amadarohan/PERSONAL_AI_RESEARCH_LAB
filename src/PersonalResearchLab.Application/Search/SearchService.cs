using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Application.Search;

/// <summary>
/// Search facade over hybrid retrieval. It exists so the UI never talks to the retrieval
/// implementation directly and so search history can be recorded in one place.
/// </summary>
public sealed class SearchService
{
    private readonly IRetrievalService _retrieval;
    private readonly IAppDbContext _db;

    public SearchService(IRetrievalService retrieval, IAppDbContext db)
    {
        _retrieval = retrieval;
        _db = db;
    }

    public async Task<SearchResponse> SearchAsync(
        Guid workspaceId,
        string query,
        int maxResults = 10,
        Guid? sourceIdFilter = null,
        bool includeCrossWorkspace = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchResponse(query, Array.Empty<SearchHit>(), "عبارت جست‌وجو خالی بود.");
        }

        var passages = await _retrieval.SearchAsync(
            new RetrievalQuery(workspaceId, query, maxResults, includeCrossWorkspace, sourceIdFilter),
            cancellationToken);

        var hits = passages
            .Select(p => new SearchHit(
                p.ChunkId,
                p.DocumentId,
                p.SourceId,
                p.SourceTitle,
                p.DocumentTitle,
                p.PageNumber,
                p.ChunkIndex,
                p.Text,
                p.LexicalScore,
                p.VectorScore,
                p.FusedScore,
                p.RetrievalReason))
            .ToList();

        var explanation = hits.Count == 0
            ? "هیچ قطعه‌ای مطابقت نداشت. عبارت دیگری را امتحان کنید یا بررسی کنید که پردازش منبع کامل شده باشد."
            : $"{hits.Count} قطعه بازیابی شد. هر نتیجه دلیل تطابق خود را نشان می‌دهد.";

        return new SearchResponse(query, hits, explanation);
    }

    /// <summary>Full document text for the reader, loaded page by page to avoid pulling everything at once.</summary>
    public async Task<DocumentReaderModel?> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var pages = await _db.DocumentPages
            .AsNoTracking()
            .Where(p => p.DocumentId == documentId)
            .OrderBy(p => p.PageNumber)
            .Select(p => new DocumentPageModel(p.PageNumber, p.Text, p.OcrApplied))
            .ToListAsync(cancellationToken);

        var source = await (
                from version in _db.SourceVersions
                join s in _db.Sources on version.SourceId equals s.Id
                where version.Id == document.SourceVersionId
                select new { s.Id, s.Title, s.Author, s.Url, s.Kind })
            .FirstOrDefaultAsync(cancellationToken);

        return new DocumentReaderModel(
            document.Id,
            document.WorkspaceId,
            document.Title,
            document.Language,
            document.PageCount,
            document.CharacterCount,
            document.UsedOcr,
            source?.Id ?? Guid.Empty,
            source?.Author,
            source?.Url,
            pages);
    }

    /// <summary>Finds the page containing a chunk so a citation can jump to the exact location.</summary>
    public async Task<int?> GetChunkPageAsync(Guid chunkId, CancellationToken cancellationToken = default) =>
        await _db.DocumentChunks
            .Where(c => c.Id == chunkId)
            .Select(c => (int?)c.PageNumber)
            .FirstOrDefaultAsync(cancellationToken);
}

public sealed record SearchResponse(string Query, IReadOnlyList<SearchHit> Hits, string Explanation);

public sealed record SearchHit(
    Guid ChunkId,
    Guid DocumentId,
    Guid SourceId,
    string SourceTitle,
    string DocumentTitle,
    int PageNumber,
    int ChunkIndex,
    string Text,
    double LexicalScore,
    double VectorScore,
    double FusedScore,
    string RetrievalReason);

public sealed record DocumentReaderModel(
    Guid DocumentId,
    Guid WorkspaceId,
    string Title,
    string? Language,
    int PageCount,
    int CharacterCount,
    bool UsedOcr,
    Guid SourceId,
    string? Author,
    string? Url,
    IReadOnlyList<DocumentPageModel> Pages);

public sealed record DocumentPageModel(int PageNumber, string Text, bool OcrApplied);
