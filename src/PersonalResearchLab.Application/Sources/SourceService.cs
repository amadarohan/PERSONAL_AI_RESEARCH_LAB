using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Application.Ingestion;
using PersonalResearchLab.Application.Text;
using PersonalResearchLab.Domain.Common;
using PersonalResearchLab.Domain.Research;
using PersonalResearchLab.Domain.Sources;
using PersonalResearchLab.Domain.System;

namespace PersonalResearchLab.Application.Sources;

/// <summary>
/// Source intake. Uploading a file stores the bytes, records a version, and queues ingestion.
/// The source is never reported as "ready" until ingestion actually succeeds, because a source
/// that looks imported but has no text is worse than one that visibly failed.
/// </summary>
public sealed class SourceService
{
    private readonly IAppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IngestionService _ingestion;
    private readonly IBackgroundJobQueue _queue;
    private readonly ILogger<SourceService> _logger;

    public SourceService(
        IAppDbContext db,
        IFileStorage storage,
        IngestionService ingestion,
        IBackgroundJobQueue queue,
        ILogger<SourceService> logger)
    {
        _db = db;
        _storage = storage;
        _ingestion = ingestion;
        _queue = queue;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SourceListItem>> ListAsync(
        Guid workspaceId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Sources.AsNoTracking().Where(s => s.WorkspaceId == workspaceId);
        if (!includeArchived)
        {
            query = query.Where(s => !s.IsArchived);
        }

        return await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new SourceListItem(
                s.Id,
                s.Title,
                s.Kind,
                s.Author,
                s.Language,
                s.SizeBytes,
                s.IsArchived,
                s.ReliabilityRating,
                s.CreatedAtUtc,
                s.Versions
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new SourceVersionInfo(
                        v.Id,
                        v.VersionNumber,
                        v.Status,
                        v.ExtractionQuality,
                        v.ProcessingError,
                        v.Document != null ? v.Document.PageCount : 0,
                        v.Document != null ? v.Document.CharacterCount : 0))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }

    public async Task<SourceDetail?> GetDetailAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await _db.Sources
            .AsNoTracking()
            .Include(s => s.Versions)
            .FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);

        if (source is null)
        {
            return null;
        }

        var latest = source.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var document = latest is null
            ? null
            : await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.SourceVersionId == latest.Id, cancellationToken);

        return new SourceDetail(
            source.Id,
            source.WorkspaceId,
            source.Title,
            source.Kind,
            source.Author,
            source.Publisher,
            source.Url,
            source.Language,
            source.SizeBytes,
            source.OriginalFileName,
            source.Tags,
            source.ReliabilityRating,
            source.ReliabilityRationale,
            source.CreatedAtUtc,
            latest?.Id,
            latest?.Status ?? ProcessingStatus.Pending,
            latest?.ProcessingError,
            document?.Id,
            document?.PageCount ?? 0,
            document?.CharacterCount ?? 0,
            document?.UsedOcr ?? false);
    }

    /// <summary>
    /// Stores an uploaded file and queues ingestion. Returns the created source and job so the
    /// UI can navigate straight to the live processing monitor.
    /// </summary>
    public async Task<UploadSourceResult> UploadAsync(
        Guid workspaceId,
        Stream content,
        string fileName,
        string? title,
        SourceKind? kind,
        CancellationToken cancellationToken = default)
    {
        var workspaceExists = await _db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken);
        if (!workspaceExists)
        {
            throw new InvalidOperationException("فضای کاری یافت نشد.");
        }

        // Hash before storing so duplicate detection never depends on the file name.
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var hash = Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
        buffer.Position = 0;

        var resolvedKind = kind ?? InferKind(fileName);
        var storagePath = await _storage.SaveAsync(buffer, fileName, cancellationToken);
        var sizeBytes = buffer.Length;

        var existing = await _db.Sources
            .Include(s => s.Versions)
            .FirstOrDefaultAsync(
                s => s.WorkspaceId == workspaceId && s.ContentHash == hash,
                cancellationToken);

        Source source;
        SourceVersion version;

        if (existing is not null)
        {
            // Same bytes already collected: add a version instead of a duplicate source.
            source = existing;
            version = new SourceVersion
            {
                SourceId = source.Id,
                VersionNumber = source.Versions.Count == 0 ? 1 : source.Versions.Max(v => v.VersionNumber) + 1,
                ContentHash = hash,
                StoragePath = storagePath,
                SizeBytes = sizeBytes,
                Status = ProcessingStatus.Pending
            };
            _db.SourceVersions.Add(version);
            _logger.LogInformation(
                "Re-imported identical content into source {SourceId} as version {Version}",
                source.Id, version.VersionNumber);
        }
        else
        {
            source = new Source
            {
                WorkspaceId = workspaceId,
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim(),
                Kind = resolvedKind,
                ContentHash = hash,
                SizeBytes = sizeBytes,
                OriginalFileName = fileName,
                RetrievedAtUtc = DateTime.UtcNow
            };

            version = new SourceVersion
            {
                SourceId = source.Id,
                VersionNumber = 1,
                ContentHash = hash,
                StoragePath = storagePath,
                SizeBytes = sizeBytes,
                Status = ProcessingStatus.Pending
            };

            source.Versions.Add(version);
            _db.Sources.Add(source);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var job = await _ingestion.CreateJobAsync(workspaceId, version.Id, $"پردازش: {source.Title}", cancellationToken);

        _db.TimelineEvents.Add(new ResearchTimelineEvent
        {
            WorkspaceId = workspaceId,
            EventType = "source.added",
            Title = $"منبع افزوده شد: {source.Title}",
            Detail = $"{PersianFormat.SourceKind(resolvedKind)}، {PersianFormat.Number(sizeBytes)} بایت",
            RelatedEntityId = source.Id
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _queue.EnqueueIngestionAsync(version.Id, job.Id, cancellationToken);

        return new UploadSourceResult(source.Id, version.Id, job.Id, existing is not null);
    }

    /// <summary>Creates a source from typed text, which needs no file extraction.</summary>
    public async Task<UploadSourceResult> CreateNoteSourceAsync(
        Guid workspaceId,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("متن یادداشت الزامی است.", nameof(body));
        }

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        return await UploadAsync(
            workspaceId,
            stream,
            $"{SanitizeFileName(title)}.md",
            title,
            SourceKind.ManualNote,
            cancellationToken);
    }

    public async Task SetArchivedAsync(Guid sourceId, bool archived, CancellationToken cancellationToken = default)
    {
        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
            ?? throw new InvalidOperationException("منبع یافت نشد.");

        source.IsArchived = archived;
        source.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetReliabilityAsync(
        Guid sourceId,
        int? rating,
        string? rationale,
        CancellationToken cancellationToken = default)
    {
        if (rating is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "اعتبار باید بین ۰ و ۱۰۰ باشد.");
        }

        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
            ?? throw new InvalidOperationException("منبع یافت نشد.");

        source.ReliabilityRating = rating;
        source.ReliabilityRationale = rationale;
        source.LastVerifiedAtUtc = rating.HasValue ? DateTime.UtcNow : null;
        source.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Re-runs ingestion for the latest version, reusing stored bytes.</summary>
    public async Task<Guid> ReprocessAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await _db.Sources
            .Include(s => s.Versions)
            .FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken)
            ?? throw new InvalidOperationException("منبع یافت نشد.");

        var latest = source.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault()
            ?? throw new InvalidOperationException("این منبع هیچ نسخه ذخیره‌شده‌ای ندارد.");

        latest.Status = ProcessingStatus.Pending;
        latest.ProcessingError = null;
        await _db.SaveChangesAsync(cancellationToken);

        var job = await _ingestion.CreateJobAsync(
            source.WorkspaceId, latest.Id, $"پردازش دوباره: {source.Title}", cancellationToken);

        await _queue.EnqueueIngestionAsync(latest.Id, job.Id, cancellationToken);
        return job.Id;
    }

    public static SourceKind InferKind(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => SourceKind.Pdf,
            ".docx" => SourceKind.Docx,
            ".md" or ".markdown" => SourceKind.Markdown,
            ".html" or ".htm" => SourceKind.Html,
            ".csv" => SourceKind.Csv,
            ".json" => SourceKind.Json,
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".tif" or ".tiff" => SourceKind.Image,
            _ => SourceKind.Text
        };

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "note" : cleaned;
    }
}

public sealed record SourceListItem(
    Guid Id,
    string Title,
    SourceKind Kind,
    string? Author,
    string? Language,
    long SizeBytes,
    bool IsArchived,
    int? ReliabilityRating,
    DateTime CreatedAtUtc,
    SourceVersionInfo? LatestVersion);

public sealed record SourceVersionInfo(
    Guid Id,
    int VersionNumber,
    ProcessingStatus Status,
    int? ExtractionQuality,
    string? ProcessingError,
    int PageCount,
    int CharacterCount);

public sealed record SourceDetail(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    SourceKind Kind,
    string? Author,
    string? Publisher,
    string? Url,
    string? Language,
    long SizeBytes,
    string? OriginalFileName,
    string? Tags,
    int? ReliabilityRating,
    string? ReliabilityRationale,
    DateTime CreatedAtUtc,
    Guid? LatestVersionId,
    ProcessingStatus Status,
    string? ProcessingError,
    Guid? DocumentId,
    int PageCount,
    int CharacterCount,
    bool UsedOcr);

public sealed record UploadSourceResult(Guid SourceId, Guid SourceVersionId, Guid JobId, bool WasDuplicate);
