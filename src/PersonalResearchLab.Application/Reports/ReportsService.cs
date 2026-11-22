using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Reports;

namespace PersonalResearchLab.Application.Reports;

/// <summary>
/// Report library. Versions are immutable snapshots, so a report can always be reproduced from
/// the exact markdown and citation set it was generated with.
/// </summary>
public sealed class ReportsService
{
    private readonly IAppDbContext _db;

    public ReportsService(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReportListItem>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        await _db.ResearchReports
            .AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId)
            .OrderByDescending(r => r.UpdatedAtUtc)
            .Select(r => new ReportListItem(
                r.Id,
                r.Title,
                r.Template,
                r.CurrentVersionNumber,
                r.Versions.Count,
                r.ResearchRunId,
                r.CreatedAtUtc,
                r.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

    public async Task<ReportDetail?> GetAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.ResearchReports
            .AsNoTracking()
            .Include(r => r.Versions)
            .FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken);

        if (report is null)
        {
            return null;
        }

        var versions = report.Versions
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new ReportVersionView(
                v.Id,
                v.VersionNumber,
                v.Markdown,
                v.CitationsJson,
                v.ChangeNote,
                v.CreatedAtUtc))
            .ToList();

        return new ReportDetail(
            report.Id,
            report.WorkspaceId,
            report.Title,
            report.Template,
            report.CurrentVersionNumber,
            report.ResearchRunId,
            report.CreatedAtUtc,
            report.UpdatedAtUtc,
            versions);
    }

    /// <summary>
    /// Appends a new immutable version. Editing a report never mutates history, which is what
    /// makes a report citable later.
    /// </summary>
    public async Task<int> AddVersionAsync(
        Guid reportId,
        string markdown,
        string citationsJson,
        string? changeNote,
        CancellationToken cancellationToken = default)
    {
        var report = await _db.ResearchReports
            .Include(r => r.Versions)
            .FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken)
            ?? throw new InvalidOperationException("گزارش یافت نشد.");

        var next = report.Versions.Count == 0 ? 1 : report.Versions.Max(v => v.VersionNumber) + 1;

        _db.ResearchReportVersions.Add(new ResearchReportVersion
        {
            ResearchReportId = report.Id,
            VersionNumber = next,
            Markdown = markdown,
            CitationsJson = citationsJson,
            ChangeNote = changeNote
        });

        report.CurrentVersionNumber = next;
        report.Touch();
        await _db.SaveChangesAsync(cancellationToken);
        return next;
    }

    public async Task<Guid> CreateAsync(
        Guid workspaceId,
        string title,
        string template,
        string markdown,
        string citationsJson,
        Guid? researchRunId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("عنوان گزارش الزامی است.", nameof(title));
        }

        var report = new ResearchReport
        {
            WorkspaceId = workspaceId,
            Title = title.Trim(),
            Template = string.IsNullOrWhiteSpace(template) ? "technical" : template,
            ResearchRunId = researchRunId,
            CurrentVersionNumber = 1
        };

        report.Versions.Add(new ResearchReportVersion
        {
            VersionNumber = 1,
            Markdown = markdown,
            CitationsJson = citationsJson,
            ChangeNote = "نسخه اولیه"
        });

        _db.ResearchReports.Add(report);
        await _db.SaveChangesAsync(cancellationToken);
        return report.Id;
    }

    public async Task DeleteAsync(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await _db.ResearchReports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken)
            ?? throw new InvalidOperationException("گزارش یافت نشد.");

        _db.ResearchReports.Remove(report);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record ReportListItem(
    Guid Id,
    string Title,
    string Template,
    int CurrentVersionNumber,
    int VersionCount,
    Guid? ResearchRunId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ReportDetail(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string Template,
    int CurrentVersionNumber,
    Guid? ResearchRunId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ReportVersionView> Versions);

public sealed record ReportVersionView(
    Guid Id,
    int VersionNumber,
    string Markdown,
    string CitationsJson,
    string? ChangeNote,
    DateTime CreatedAtUtc);
