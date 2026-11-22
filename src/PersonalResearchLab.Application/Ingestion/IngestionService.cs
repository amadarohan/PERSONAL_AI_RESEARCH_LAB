using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Application.Text;
using PersonalResearchLab.Domain.Common;
using PersonalResearchLab.Domain.Documents;
using PersonalResearchLab.Domain.Sources;
using PersonalResearchLab.Domain.System;

namespace PersonalResearchLab.Application.Ingestion;

/// <summary>
/// Orchestrates the document ingestion pipeline. Each stage is recorded as a
/// <see cref="ProcessingJobStep"/> so progress reflects real work, and a failure in a later
/// stage never discards the output of an earlier successful one.
/// </summary>
public sealed class IngestionService
{
    private static readonly string[] StageNames =
    {
        "اعتبارسنجی فایل",
        "درهم‌سازی و تشخیص تکرار",
        "استخراج متن",
        "تشخیص زبان",
        "قطعه‌بندی متن",
        "ساخت نمایه واژگانی",
        "تولید تعبیه برداری",
        "پایان"
    };

    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IEnumerable<ITextExtractor> _extractors;
    private readonly ITextTokenizer _tokenizer;
    private readonly ILanguageDetector _languageDetector;
    private readonly IEmbeddingCoordinator _embeddings;
    private readonly IJobNotifier _notifier;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        IAppDbContext db,
        IFileStorage storage,
        IEnumerable<ITextExtractor> extractors,
        ITextTokenizer tokenizer,
        ILanguageDetector languageDetector,
        IEmbeddingCoordinator embeddings,
        IJobNotifier notifier,
        ILogger<IngestionService> logger)
    {
        _db = db;
        _storage = storage;
        _extractors = extractors;
        _tokenizer = tokenizer;
        _languageDetector = languageDetector;
        _embeddings = embeddings;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// Runs the full pipeline for a source version. Safe to re-run: existing chunks are replaced
    /// and unchanged chunk content is not re-embedded.
    /// </summary>
    public async Task<IngestionOutcome> ProcessAsync(
        Guid sourceVersionId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var version = await _db.SourceVersions
            .Include(v => v.Source)
            .FirstOrDefaultAsync(v => v.Id == sourceVersionId, cancellationToken);

        if (version?.Source is null)
        {
            return IngestionOutcome.Failed("نسخه منبع یافت نشد.");
        }

        var job = await _db.ProcessingJobs
            .Include(j => j.Steps)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            return IngestionOutcome.Failed("کار پردازش یافت نشد.");
        }

        var workspaceId = version.Source.WorkspaceId;
        var steps = job.Steps.OrderBy(s => s.Order).ToList();

        job.Status = ProcessingStatus.Running;
        job.StartedAtUtc = DateTime.UtcNow;
        version.Status = ProcessingStatus.Running;
        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.JobChangedAsync(job.Id, cancellationToken);

        try
        {
            // Stage 1: validate
            await BeginStepAsync(steps, 0, job, cancellationToken);
            if (version.SizeBytes <= 0)
            {
                throw new InvalidOperationException("فایل خالی است.");
            }

            await CompleteStepAsync(steps, 0, job, $"{PersianFormat.Number(version.SizeBytes)} بایت", cancellationToken);

            // Stage 2: hash / duplicate detection
            await BeginStepAsync(steps, 1, job, cancellationToken);
            var duplicateCount = await _db.SourceVersions
                .CountAsync(v => v.ContentHash == version.ContentHash && v.Id != version.Id, cancellationToken);
            await CompleteStepAsync(steps, 1, job,
                duplicateCount > 0 ? $"{PersianFormat.Number(duplicateCount)} نسخه یکسان از قبل ذخیره شده است" : "نسخه تکراری یافت نشد",
                cancellationToken);

            // Stage 3: extract text
            await BeginStepAsync(steps, 2, job, cancellationToken);
            var extractor = _extractors.FirstOrDefault(e => e.CanHandle(version.Source.Kind, version.Source.OriginalFileName))
                ?? throw new InvalidOperationException(
                    $"هیچ استخراج‌کننده متنی برای نوع {PersianFormat.SourceKind(version.Source.Kind)} ثبت نشده است.");

            ExtractionResult extraction;
            await using (var stream = await _storage.OpenReadAsync(version.StoragePath, cancellationToken))
            {
                extraction = await extractor.ExtractAsync(
                    stream, version.Source.Kind, version.Source.OriginalFileName, cancellationToken);
            }

            var totalChars = extraction.Pages.Sum(p => p.Text.Length);
            if (totalChars == 0)
            {
                throw new InvalidOperationException(
                    "هیچ متنی استخراج نشد. ممکن است فایل یک تصویر اسکن‌شده بدون OCR باشد.");
            }

            await CompleteStepAsync(steps, 2, job,
                $"{PersianFormat.Number(extraction.Pages.Count)} صفحه، {PersianFormat.Number(totalChars)} نویسه" +
                (extraction.UsedOcr ? " (OCR اعمال شد)" : string.Empty),
                cancellationToken);

            // Stage 4: language
            await BeginStepAsync(steps, 3, job, cancellationToken);
            var combined = string.Join('\n', extraction.Pages.Select(p => p.Text));
            var language = extraction.DetectedLanguage ?? _languageDetector.Detect(combined);
            version.Source.Language ??= language;
            await CompleteStepAsync(steps, 3, job, $"تشخیص‌داده‌شده: {PersianFormat.Language(language)}", cancellationToken);

            // Stage 5: chunk
            await BeginStepAsync(steps, 4, job, cancellationToken);
            var document = await _db.Documents
                .Include(d => d.Pages)
                .Include(d => d.Chunks)
                .FirstOrDefaultAsync(d => d.SourceVersionId == version.Id, cancellationToken);

            if (document is null)
            {
                document = new Document
                {
                    SourceVersionId = version.Id,
                    WorkspaceId = workspaceId,
                    Title = version.Source.Title
                };
                _db.Documents.Add(document);
            }

            // Replace previous extraction output; embeddings for removed chunks are deleted with them.
            _db.DocumentPages.RemoveRange(document.Pages);
            _db.DocumentChunks.RemoveRange(document.Chunks);
            document.Pages.Clear();
            document.Chunks.Clear();

            document.Title = version.Source.Title;
            document.Language = language;
            document.PageCount = extraction.Pages.Count;
            document.CharacterCount = totalChars;
            document.UsedOcr = extraction.UsedOcr;

            var chunker = new TextChunker();
            var drafts = new List<(DocumentChunk Chunk, ChunkDraft Draft)>();
            foreach (var page in extraction.Pages)
            {
                document.Pages.Add(new DocumentPage
                {
                    DocumentId = document.Id,
                    PageNumber = page.PageNumber,
                    Text = page.Text,
                    OcrApplied = page.OcrApplied
                });

                foreach (var draft in chunker.Chunk(page.Text, page.PageNumber))
                {
                    var chunk = new DocumentChunk
                    {
                        DocumentId = document.Id,
                        WorkspaceId = workspaceId,
                        PageNumber = draft.PageNumber,
                        ChunkIndex = draft.ChunkIndex,
                        StartOffset = draft.StartOffset,
                        EndOffset = draft.EndOffset,
                        Text = draft.Text,
                        TokenEstimate = draft.TokenEstimate,
                        ContentHash = TextChunker.HashContent(draft.Text)
                    };
                    document.Chunks.Add(chunk);
                    drafts.Add((chunk, draft));
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await CompleteStepAsync(steps, 4, job, $"{PersianFormat.Number(drafts.Count)} قطعه", cancellationToken);

            // Stage 6: lexical index
            await BeginStepAsync(steps, 5, job, cancellationToken);
            var termRows = new List<ChunkTerm>();
            foreach (var (chunk, _) in drafts)
            {
                foreach (var (term, frequency) in _tokenizer.CountTerms(chunk.Text))
                {
                    termRows.Add(new ChunkTerm
                    {
                        DocumentChunkId = chunk.Id,
                        WorkspaceId = workspaceId,
                        Term = term,
                        Frequency = frequency
                    });
                }
            }

            _db.ChunkTerms.AddRange(termRows);
            await _db.SaveChangesAsync(cancellationToken);
            await CompleteStepAsync(steps, 5, job, $"{PersianFormat.Number(termRows.Count)} ورودی نمایه", cancellationToken);

            // Stage 7: embeddings
            await BeginStepAsync(steps, 6, job, cancellationToken);
            var embeddingResult = await _embeddings.EmbedChunksAsync(
                workspaceId,
                drafts.Select(d => (d.Chunk.Id, d.Chunk.Text, d.Chunk.ContentHash)).ToList(),
                cancellationToken);

            foreach (var (chunk, _) in drafts)
            {
                chunk.IsEmbedded = embeddingResult.EmbeddedChunkIds.Contains(chunk.Id);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await CompleteStepAsync(
                steps,
                6,
                job,
                embeddingResult.Summary,
                cancellationToken,
                embeddingResult.Status);

            // Stage 8: complete
            await BeginStepAsync(steps, 7, job, cancellationToken);
            version.Status = ProcessingStatus.Succeeded;
            version.ExtractionQuality = ComputeExtractionQuality(totalChars, extraction.Pages.Count);
            version.ProcessingError = null;
            job.Status = ProcessingStatus.Succeeded;
            job.ProgressPercent = 100;
            job.CurrentStage = "پایان";
            job.CompletedAtUtc = DateTime.UtcNow;
            await CompleteStepAsync(steps, 7, job, "پردازش کامل شد", cancellationToken);

            _logger.LogInformation(
                "Ingested source version {SourceVersionId}: {ChunkCount} chunks, {TermCount} terms, embeddings={EmbeddingSummary}",
                version.Id, drafts.Count, termRows.Count, embeddingResult.Summary);

            return IngestionOutcome.Succeeded(drafts.Count, termRows.Count, embeddingResult.Summary);
        }
        catch (OperationCanceledException)
        {
            version.Status = ProcessingStatus.Cancelled;
            job.Status = ProcessingStatus.Cancelled;
            job.CurrentStage = "لغو شد";
            job.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await _notifier.JobChangedAsync(job.Id, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for source version {SourceVersionId}", sourceVersionId);

            version.Status = ProcessingStatus.Failed;
            version.ProcessingError = ex.Message;
            job.Status = ProcessingStatus.Failed;
            job.FailureReason = ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;

            var running = steps.FirstOrDefault(s => s.Status == ProcessingStatus.Running);
            if (running is not null)
            {
                running.Status = ProcessingStatus.Failed;
                running.Detail = ex.Message;
                running.CompletedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(CancellationToken.None);
            await _notifier.JobChangedAsync(job.Id, CancellationToken.None);
            return IngestionOutcome.Failed(ex.Message);
        }
    }

    /// <summary>Creates the job and its stage rows so the monitor can show the full plan up front.</summary>
    public async Task<ProcessingJob> CreateJobAsync(
        Guid workspaceId,
        Guid sourceVersionId,
        string title,
        CancellationToken cancellationToken = default)
    {
        var job = new ProcessingJob
        {
            WorkspaceId = workspaceId,
            JobType = "DocumentIngestion",
            Title = title,
            Status = ProcessingStatus.Pending,
            RelatedEntityId = sourceVersionId,
            CurrentStage = StageNames[0]
        };

        for (var i = 0; i < StageNames.Length; i++)
        {
            job.Steps.Add(new ProcessingJobStep
            {
                ProcessingJobId = job.Id,
                Order = i,
                Name = StageNames[i],
                Status = ProcessingStatus.Pending
            });
        }

        _db.ProcessingJobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);
        return job;
    }

    private async Task BeginStepAsync(
        List<ProcessingJobStep> steps,
        int order,
        ProcessingJob job,
        CancellationToken cancellationToken)
    {
        if (order >= steps.Count)
        {
            return;
        }

        var step = steps[order];
        step.Status = ProcessingStatus.Running;
        step.StartedAtUtc = DateTime.UtcNow;
        job.CurrentStage = step.Name;
        job.ProgressPercent = (int)Math.Round(order * 100.0 / steps.Count);
        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.JobChangedAsync(job.Id, cancellationToken);
    }

    private async Task CompleteStepAsync(
        List<ProcessingJobStep> steps,
        int order,
        ProcessingJob job,
        string detail,
        CancellationToken cancellationToken,
        ProcessingStatus status = ProcessingStatus.Succeeded)
    {
        if (order >= steps.Count)
        {
            return;
        }

        var step = steps[order];
        step.Status = status;
        step.CompletedAtUtc = DateTime.UtcNow;
        step.Detail = detail;
        job.ProgressPercent = (int)Math.Round((order + 1) * 100.0 / steps.Count);
        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.JobChangedAsync(job.Id, cancellationToken);
    }

    private static IEnumerable<KeyValuePair<string, int>> CountGeneric(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in text.Split(
                     new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '"', '\'' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 2)
            {
                continue;
            }

            counts[token] = counts.TryGetValue(token, out var existing) ? existing + 1 : 1;
        }

        return counts;
    }

    /// <summary>
    /// Coarse extraction quality: characters per page relative to a healthy baseline.
    /// This is a heuristic signal for the user, not a claim about document correctness.
    /// </summary>
    private static int ComputeExtractionQuality(int totalChars, int pageCount)
    {
        if (pageCount <= 0)
        {
            return 0;
        }

        var charsPerPage = (double)totalChars / pageCount;
        var score = (int)Math.Round(Math.Min(100, charsPerPage / 18.0));
        return Math.Clamp(score, 0, 100);
    }
}

public sealed record IngestionOutcome(
    bool Success,
    int ChunkCount,
    int TermCount,
    string EmbeddingSummary,
    string? Error)
{
    public static IngestionOutcome Succeeded(int chunks, int terms, string embeddingSummary) =>
        new(true, chunks, terms, embeddingSummary, null);

    public static IngestionOutcome Failed(string error) => new(false, 0, 0, "انجام نشد", error);
}

/// <summary>Embedding step abstraction so ingestion does not depend on a concrete provider.</summary>
public interface IEmbeddingCoordinator
{
    Task<EmbeddingBatchResult> EmbedChunksAsync(
        Guid workspaceId,
        IReadOnlyList<(Guid ChunkId, string Text, string ContentHash)> chunks,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of the embedding stage. <see cref="Status"/> is carried explicitly because the
/// stage can legitimately end without embedding anything: no provider is configured, or the
/// provider failed. Reporting those as <c>Succeeded</c> would put a green check next to a
/// stage that did no work, which is exactly the kind of false signal this product must avoid.
/// </summary>
public sealed record EmbeddingBatchResult(
    IReadOnlySet<Guid> EmbeddedChunkIds,
    string Summary,
    ProcessingStatus Status = ProcessingStatus.Succeeded)
{
    public static EmbeddingBatchResult Skipped(string summary) =>
        new(new HashSet<Guid>(), summary, ProcessingStatus.Skipped);

    public static EmbeddingBatchResult Failed(IReadOnlySet<Guid> embedded, string summary) =>
        new(embedded, summary, ProcessingStatus.Failed);
}
