using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Common;
using PersonalResearchLab.Domain.Evidence;

namespace PersonalResearchLab.Application.Claims;

/// <summary>
/// Claim and evidence review. A claim's status is a human judgement recorded here; the service
/// never infers support from the mere existence of evidence rows.
/// </summary>
public sealed class ClaimsService
{
    private readonly IAppDbContext _db;

    public ClaimsService(IAppDbContext db) => _db = db;

    public async Task<ClaimsOverview> GetOverviewAsync(
        Guid workspaceId,
        ClaimStatus? statusFilter = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Claims.AsNoTracking().Where(c => c.WorkspaceId == workspaceId);
        if (statusFilter is not null)
        {
            query = query.Where(c => c.Status == statusFilter);
        }

        var claims = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new ClaimListItem(
                c.Id,
                c.Statement,
                c.ClaimType,
                c.Status,
                c.EpistemicStatus,
                c.SourceId,
                c.PageNumber,
                c.IsAiExtracted,
                c.TemporalContext,
                c.Evidence.Count,
                c.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var counts = new ClaimCounts(
            Total: await _db.Claims.CountAsync(c => c.WorkspaceId == workspaceId, cancellationToken),
            Unreviewed: await _db.Claims.CountAsync(
                c => c.WorkspaceId == workspaceId && c.Status == ClaimStatus.Unreviewed, cancellationToken),
            Supported: await _db.Claims.CountAsync(
                c => c.WorkspaceId == workspaceId && c.Status == ClaimStatus.Supported, cancellationToken),
            Contested: await _db.Claims.CountAsync(
                c => c.WorkspaceId == workspaceId && c.Status == ClaimStatus.Contested, cancellationToken),
            Contradicted: await _db.Claims.CountAsync(
                c => c.WorkspaceId == workspaceId && c.Status == ClaimStatus.Contradicted, cancellationToken),
            OpenConflicts: await _db.ClaimConflicts.CountAsync(
                c => c.WorkspaceId == workspaceId && !c.IsResolved, cancellationToken));

        var conflicts = await _db.ClaimConflicts
            .AsNoTracking()
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderBy(c => c.IsResolved)
            .ThenByDescending(c => c.CreatedAtUtc)
            .Take(50)
            .Select(c => new ClaimConflictView(
                c.Id,
                c.ClaimAId,
                c.ClaimBId,
                c.Explanation,
                c.IsResolved,
                c.Resolution,
                c.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new ClaimsOverview(counts, claims, conflicts);
    }

    public async Task<ClaimDetail?> GetAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        var claim = await _db.Claims
            .AsNoTracking()
            .Include(c => c.Evidence)
            .FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken);

        if (claim is null)
        {
            return null;
        }

        string? sourceTitle = null;
        if (claim.SourceId is not null)
        {
            sourceTitle = await _db.Sources
                .Where(s => s.Id == claim.SourceId)
                .Select(s => s.Title)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new ClaimDetail(
            claim.Id,
            claim.WorkspaceId,
            claim.Statement,
            claim.ClaimType,
            claim.Status,
            claim.EpistemicStatus,
            claim.SourceId,
            sourceTitle,
            claim.DocumentChunkId,
            claim.PageNumber,
            claim.SourceExcerpt,
            claim.TemporalContext,
            claim.IsAiExtracted,
            claim.UserAnnotation,
            claim.VerifiedAtUtc,
            claim.CreatedAtUtc,
            claim.Evidence
                .OrderBy(e => e.Stance)
                .Select(e => new ClaimEvidenceView(
                    e.Id,
                    e.DocumentChunkId,
                    e.SourceId,
                    e.PageNumber,
                    e.Excerpt,
                    e.Stance,
                    e.Rationale,
                    e.IsAiGenerated))
                .ToList());
    }

    /// <summary>Records a human review decision. This is the only path that changes claim status.</summary>
    public async Task SetStatusAsync(
        Guid claimId,
        ClaimStatus status,
        string? annotation,
        CancellationToken cancellationToken = default)
    {
        var claim = await _db.Claims.FirstOrDefaultAsync(c => c.Id == claimId, cancellationToken)
            ?? throw new InvalidOperationException("ادعا یافت نشد.");

        claim.Status = status;
        claim.UserAnnotation = annotation;

        // Only an explicit human review can promote a claim to Verified.
        if (status == ClaimStatus.Supported)
        {
            claim.EpistemicStatus = EpistemicStatus.Verified;
            claim.VerifiedAtUtc = DateTime.UtcNow;
        }
        else if (status == ClaimStatus.Unreviewed)
        {
            claim.VerifiedAtUtc = null;
        }

        claim.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> CreateAsync(
        Guid workspaceId,
        string statement,
        ClaimType claimType,
        string? temporalContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(statement))
        {
            throw new ArgumentException("متن ادعا الزامی است.", nameof(statement));
        }

        var claim = new Claim
        {
            WorkspaceId = workspaceId,
            Statement = statement.Trim(),
            ClaimType = claimType,
            Status = ClaimStatus.Unreviewed,
            EpistemicStatus = EpistemicStatus.Hypothesis,
            TemporalContext = temporalContext?.Trim(),
            IsAiExtracted = false
        };

        _db.Claims.Add(claim);
        await _db.SaveChangesAsync(cancellationToken);
        return claim.Id;
    }

    public async Task ResolveConflictAsync(
        Guid conflictId,
        string resolution,
        CancellationToken cancellationToken = default)
    {
        var conflict = await _db.ClaimConflicts.FirstOrDefaultAsync(c => c.Id == conflictId, cancellationToken)
            ?? throw new InvalidOperationException("تناقض یافت نشد.");

        conflict.IsResolved = true;
        conflict.Resolution = resolution;
        conflict.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ClaimsOverview(
    ClaimCounts Counts,
    IReadOnlyList<ClaimListItem> Claims,
    IReadOnlyList<ClaimConflictView> Conflicts);

public sealed record ClaimCounts(
    int Total,
    int Unreviewed,
    int Supported,
    int Contested,
    int Contradicted,
    int OpenConflicts);

public sealed record ClaimListItem(
    Guid Id,
    string Statement,
    ClaimType ClaimType,
    ClaimStatus Status,
    EpistemicStatus EpistemicStatus,
    Guid? SourceId,
    int? PageNumber,
    bool IsAiExtracted,
    string? TemporalContext,
    int EvidenceCount,
    DateTime CreatedAtUtc);

public sealed record ClaimDetail(
    Guid Id,
    Guid WorkspaceId,
    string Statement,
    ClaimType ClaimType,
    ClaimStatus Status,
    EpistemicStatus EpistemicStatus,
    Guid? SourceId,
    string? SourceTitle,
    Guid? DocumentChunkId,
    int? PageNumber,
    string? SourceExcerpt,
    string? TemporalContext,
    bool IsAiExtracted,
    string? UserAnnotation,
    DateTime? VerifiedAtUtc,
    DateTime CreatedAtUtc,
    IReadOnlyList<ClaimEvidenceView> Evidence);

public sealed record ClaimEvidenceView(
    Guid Id,
    Guid DocumentChunkId,
    Guid SourceId,
    int PageNumber,
    string Excerpt,
    EvidenceStance Stance,
    string? Rationale,
    bool IsAiGenerated);

public sealed record ClaimConflictView(
    Guid Id,
    Guid ClaimAId,
    Guid ClaimBId,
    string Explanation,
    bool IsResolved,
    string? Resolution,
    DateTime CreatedAtUtc);
