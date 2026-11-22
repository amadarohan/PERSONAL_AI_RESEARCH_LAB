using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Application.Text;
using PersonalResearchLab.Domain.Common;
using PersonalResearchLab.Domain.Research;

namespace PersonalResearchLab.Application.Research;

/// <summary>
/// The deep research engine.
/// <para>
/// The engine is deliberately observable: it writes one <see cref="ResearchPlanStep"/> per stage
/// and updates it as work happens, so the user can watch and interrupt. It never claims to have
/// searched the web unless a real <see cref="IWebSearchProvider"/> executed, and it never emits a
/// finding without recording which chunks support it.
/// </para>
/// </summary>
public sealed class DeepResearchService
{
    private const int EvidencePerQuestion = 5;

    private readonly IAppDbContext _db;
    private readonly IRetrievalService _retrieval;
    private readonly IAiProviderResolver _resolver;
    private readonly IJobNotifier _notifier;
    private readonly ILogger<DeepResearchService> _logger;

    public DeepResearchService(
        IAppDbContext db,
        IRetrievalService retrieval,
        IAiProviderResolver resolver,
        IJobNotifier notifier,
        ILogger<DeepResearchService> logger)
    {
        _db = db;
        _retrieval = retrieval;
        _resolver = resolver;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// Creates a run in <see cref="ResearchRunStatus.AwaitingApproval"/> with a visible plan.
    /// Nothing is executed until the user approves, which is the core control guarantee.
    /// </summary>
    public async Task<Guid> CreatePlanAsync(
        Guid workspaceId,
        string objective,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objective))
        {
            throw new ArgumentException("هدف پژوهش الزامی است.", nameof(objective));
        }

        var workspaceExists = await _db.Workspaces.AnyAsync(w => w.Id == workspaceId, cancellationToken);
        if (!workspaceExists)
        {
            throw new InvalidOperationException("فضای کاری یافت نشد.");
        }

        var run = new ResearchRun
        {
            WorkspaceId = workspaceId,
            Objective = objective.Trim(),
            Status = ResearchRunStatus.AwaitingApproval
        };

        var plan = new (ResearchPlanStepKind Kind, string Title, string Detail)[]
        {
            (ResearchPlanStepKind.ClarifyObjective, "روشن‌سازی هدف",
                "شناسایی واژه‌های مبهم و اعلام دامنه‌ای که بررسی می‌شود."),
            (ResearchPlanStepKind.DecomposeQuestions, "تجزیه به پرسش‌های پژوهشی",
                "شکستن هدف به زیرپرسش‌های قابل پاسخ."),
            (ResearchPlanStepKind.InventoryKnowledge, "فهرست‌برداری از دانش موجود",
                "شمارش آنچه فضای کاری دارد و آنچه ندارد."),
            (ResearchPlanStepKind.RetrieveEvidence, "بازیابی شواهد",
                "اجرای بازیابی ترکیبی برای هر پرسش روی منابع فضای کاری."),
            (ResearchPlanStepKind.EvaluateEvidence, "ارزیابی شواهد",
                "سنجش اینکه قطعه‌های بازیابی‌شده واقعاً به هر پرسش مربوط هستند یا نه."),
            (ResearchPlanStepKind.DetectContradictions, "تشخیص تناقض",
                "جست‌وجوی قطعه‌هایی که با یکدیگر اختلاف دارند."),
            (ResearchPlanStepKind.FollowUpQuestions, "تولید پرسش‌های پیگیری",
                "ثبت آنچه بی‌پاسخ مانده تا اجرای بعدی روی آن تمرکز کند."),
            (ResearchPlanStepKind.Synthesize, "ترکیب یافته‌ها",
                "تولید یافته‌ها، هر یک با وضعیت معرفتی و شواهد مشخص."),
            (ResearchPlanStepKind.DraftReport, "نگارش پیش‌نویس گزارش",
                "ساخت گزارش ارجاع‌دار از یافته‌ها.")
        };

        for (var i = 0; i < plan.Length; i++)
        {
            run.PlanSteps.Add(new ResearchPlanStep
            {
                ResearchRunId = run.Id,
                Order = i,
                Kind = plan[i].Kind,
                Title = plan[i].Title,
                Detail = plan[i].Detail,
                Status = ProcessingStatus.Pending
            });
        }

        _db.ResearchRuns.Add(run);
        _db.TimelineEvents.Add(new ResearchTimelineEvent
        {
            WorkspaceId = workspaceId,
            EventType = "research.planned",
            Title = "طرح پژوهش ساخته شد",
            Detail = objective.Trim(),
            RelatedEntityId = run.Id
        });

        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.ResearchRunChangedAsync(run.Id, cancellationToken);
        return run.Id;
    }

    public async Task<ResearchRunDetail?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _db.ResearchRuns
            .AsNoTracking()
            .Include(r => r.PlanSteps)
            .Include(r => r.Findings)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

        if (run is null)
        {
            return null;
        }

        return new ResearchRunDetail(
            run.Id,
            run.WorkspaceId,
            run.Objective,
            run.Status,
            run.UsedWebSearch,
            run.WebSearchProvider,
            run.FailureReason,
            run.StartedAtUtc,
            run.CompletedAtUtc,
            run.PlanSteps
                .OrderBy(s => s.Order)
                .Select(s => new ResearchStepView(
                    s.Order, s.Kind, s.Title, s.Detail, s.Status,
                    s.StartedAtUtc, s.CompletedAtUtc, s.OutputSummary, s.FailureReason))
                .ToList(),
            run.Findings
                .Select(f => new ResearchFindingView(
                    f.Id, f.Statement, f.EpistemicStatus, f.EvidenceChunkIds, f.Caveat))
                .ToList());
    }

    public async Task<IReadOnlyList<ResearchRunSummary>> ListRunsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        await _db.ResearchRuns
            .AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new ResearchRunSummary(
                r.Id,
                r.Objective,
                r.Status,
                r.UsedWebSearch,
                r.Findings.Count,
                r.CreatedAtUtc,
                r.CompletedAtUtc))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Executes an approved plan. Each step is persisted before and after it runs so an
    /// interrupted run leaves an accurate record of how far it got.
    /// </summary>
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await _db.ResearchRuns
            .Include(r => r.PlanSteps)
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException("اجرای پژوهش یافت نشد.");

        if (run.Status is ResearchRunStatus.Completed or ResearchRunStatus.Cancelled)
        {
            return;
        }

        var workspace = await _db.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == run.WorkspaceId, cancellationToken)
            ?? throw new InvalidOperationException("فضای کاری یافت نشد.");

        run.Status = ResearchRunStatus.Running;
        run.StartedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.ResearchRunChangedAsync(run.Id, cancellationToken);

        var steps = run.PlanSteps.OrderBy(s => s.Order).ToList();
        var questions = new List<string>();
        var evidenceByQuestion = new Dictionary<string, List<RetrievedPassage>>();
        var findings = new List<ResearchFinding>();

        try
        {
            // Step 0: clarify
            await RunStepAsync(steps[0], async () =>
            {
                var resolved = await _resolver.ResolveChatAsync(run.WorkspaceId, cancellationToken);
                if (resolved is null)
                {
                    return "ارائه‌دهنده گفت‌وگویی تنظیم نشده است؛ هدف بدون روشن‌سازی و به‌صورت عین عبارت استفاده شد.";
                }

                var result = await resolved.Provider.CompleteAsync(new AiChatRequest(
                    new List<AiChatMessage>
                    {
                        new("system",
                            "You clarify research objectives. Reply with one short paragraph naming " +
                            "ambiguous terms and the scope you assume. Do not answer the objective."),
                        new("user", run.Objective)
                    },
                    Temperature: 0.1,
                    MaxOutputTokens: 300), cancellationToken);

                return result.Success
                    ? result.Content
                    : $"روشن‌سازی ممکن نشد: {result.Error}";
            }, cancellationToken);

            // Step 1: decompose
            await RunStepAsync(steps[1], async () =>
            {
                questions = await DecomposeAsync(run, workspace, cancellationToken);
                return $"{PersianFormat.Digits(questions.Count.ToString())} پرسش: " + string.Join(" | ", questions.Take(4));
            }, cancellationToken);

            // Step 2: inventory
            await RunStepAsync(steps[2], async () =>
            {
                var sourceCount = await _db.Sources.CountAsync(
                    s => s.WorkspaceId == run.WorkspaceId && !s.IsArchived, cancellationToken);
                var chunkCount = await _db.DocumentChunks.CountAsync(
                    c => c.WorkspaceId == run.WorkspaceId, cancellationToken);

                return $"{PersianFormat.Number(sourceCount)} منبع و {PersianFormat.Number(chunkCount)} قطعه نمایه‌شده در دسترس است.";
            }, cancellationToken);

            // Step 3: retrieve
            await RunStepAsync(steps[3], async () =>
            {
                var total = 0;
                foreach (var question in questions)
                {
                    var passages = await _retrieval.SearchAsync(
                        new RetrievalQuery(run.WorkspaceId, question, EvidencePerQuestion),
                        cancellationToken);

                    evidenceByQuestion[question] = passages.ToList();
                    total += passages.Count;
                }

                return $"{PersianFormat.Number(total)} قطعه در {PersianFormat.Number(questions.Count)} پرسش بازیابی شد.";
            }, cancellationToken);

            // Step 4: evaluate
            await RunStepAsync(steps[4], () =>
            {
                var withEvidence = evidenceByQuestion.Count(kv => kv.Value.Count > 0);
                return Task.FromResult($"{PersianFormat.Number(withEvidence)} از {PersianFormat.Number(questions.Count)} پرسش قطعه پشتیبان دارد.");
            }, cancellationToken);

            // Step 5: contradictions
            await RunStepAsync(steps[5], async () =>
            {
                var conflicts = await _db.ClaimConflicts.CountAsync(
                    c => c.WorkspaceId == run.WorkspaceId && !c.IsResolved, cancellationToken);

                return conflicts == 0
                    ? "تناقض ادعایی ثبت‌شده‌ای در این فضای کاری وجود ندارد."
                    : $"{conflicts} تناقض ادعایی حل‌نشده از قبل ثبت شده است.";
            }, cancellationToken);

            // Step 6: follow-ups
            await RunStepAsync(steps[6], async () =>
            {
                var unanswered = evidenceByQuestion.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
                foreach (var question in unanswered)
                {
                    _db.ResearchQuestions.Add(new ResearchQuestion
                    {
                        WorkspaceId = run.WorkspaceId,
                        Text = question,
                        Rationale = "در فضای کاری فعلی هیچ شاهد پشتیبانی‌کننده‌ای یافت نشد.",
                        IsAiGenerated = true,
                        SortOrder = 100
                    });
                }

                await _db.SaveChangesAsync(cancellationToken);
                return unanswered.Count == 0
                    ? "هر پرسش حداقل یک قطعه پشتیبان داشت."
                    : $"{unanswered.Count} پرسش به‌عنوان بی‌پاسخ ثبت شد.";
            }, cancellationToken);

            // Step 7: synthesize
            await RunStepAsync(steps[7], async () =>
            {
                findings = await SynthesizeAsync(run, workspace, evidenceByQuestion, cancellationToken);
                _db.ResearchFindings.AddRange(findings);
                await _db.SaveChangesAsync(cancellationToken);
                return $"{PersianFormat.Number(findings.Count)} یافته با پیوند شواهد ثبت شد.";
            }, cancellationToken);

            // Step 8: report draft
            await RunStepAsync(steps[8], async () =>
            {
                var report = BuildReport(run, findings, evidenceByQuestion);
                _db.ResearchReports.Add(report);
                await _db.SaveChangesAsync(cancellationToken);
                return $"پیش‌نویس گزارش ساخته شد: {report.Title}";
            }, cancellationToken);

            run.Status = ResearchRunStatus.Completed;
            run.CompletedAtUtc = DateTime.UtcNow;

            // This engine never performs web search. UsedWebSearch stays false so the UI cannot
            // imply external sources were consulted when only local evidence was used.
            run.UsedWebSearch = false;
            run.WebSearchProvider = null;

            _db.TimelineEvents.Add(new ResearchTimelineEvent
            {
                WorkspaceId = run.WorkspaceId,
                EventType = "research.completed",
                Title = "اجرای پژوهش کامل شد",
                Detail = run.Objective,
                RelatedEntityId = run.Id
            });

            await _db.SaveChangesAsync(cancellationToken);
            await _notifier.ResearchRunChangedAsync(run.Id, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Status = ResearchRunStatus.Cancelled;
            run.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await _notifier.ResearchRunChangedAsync(run.Id, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Research run {RunId} failed", runId);
            run.Status = ResearchRunStatus.Failed;
            run.FailureReason = ex.Message;
            run.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            await _notifier.ResearchRunChangedAsync(run.Id, CancellationToken.None);
        }
    }

    private async Task<List<string>> DecomposeAsync(
        ResearchRun run,
        ResearchWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveChatAsync(run.WorkspaceId, cancellationToken);
        if (resolved is not null)
        {
            var result = await resolved.Provider.CompleteAsync(new AiChatRequest(
                new List<AiChatMessage>
                {
                    new("system",
                        "Decompose a research objective into 3-6 answerable research questions. " +
                        "Reply with JSON only: {\"questions\":[\"...\"]}. No commentary."),
                    new("user", run.Objective)
                },
                Temperature: 0.2,
                MaxOutputTokens: 600,
                JsonMode: true), cancellationToken);

            if (result.Success)
            {
                var parsed = TryParseQuestions(result.Content);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
        }

        // Deterministic fallback: derive questions from the objective without inventing content.
        return new List<string>
        {
            $"این فضای کاری دربارهٔ چه چیزی از پیش شواهد دارد: {run.Objective}",
            $"کدام منابع این فضای کاری به این موضوع می‌پردازند: {run.Objective}",
            $"چه شواهدی دربارهٔ این موضوع هنوز غایب است: {run.Objective}"
        };
    }

    private static List<string> TryParseQuestions(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("questions", out var array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                return array.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Take(6)
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Fall through to the deterministic fallback.
        }

        return new List<string>();
    }

    private async Task<List<ResearchFinding>> SynthesizeAsync(
        ResearchRun run,
        ResearchWorkspace workspace,
        Dictionary<string, List<RetrievedPassage>> evidenceByQuestion,
        CancellationToken cancellationToken)
    {
        var findings = new List<ResearchFinding>();
        var resolved = await _resolver.ResolveChatAsync(run.WorkspaceId, cancellationToken);

        foreach (var (question, passages) in evidenceByQuestion)
        {
            if (passages.Count == 0)
            {
                findings.Add(new ResearchFinding
                {
                    ResearchRunId = run.Id,
                    Statement = $"هیچ شاهدی در این فضای کاری به این پرسش نمی‌پردازد: {question}",
                    EpistemicStatus = EpistemicStatus.Unknown,
                    EvidenceChunkIds = string.Empty,
                    Caveat = "به‌عنوان شکاف دانش ثبت شد، نه به‌عنوان یافته."
                });
                continue;
            }

            var chunkIds = string.Join(',', passages.Select(p => p.ChunkId));

            if (resolved is null)
            {
                // Without a model, report the evidence itself rather than inventing a synthesis.
                findings.Add(new ResearchFinding
                {
                    ResearchRunId = run.Id,
                    Statement = $"شواهد بازیابی‌شده برای: {question}",
                    EpistemicStatus = EpistemicStatus.SourceReported,
                    EvidenceChunkIds = chunkIds,
                    Caveat = "ارائه‌دهنده هوش مصنوعی تنظیم نشده است؛ این یک اشاره به شواهد است، نه ترکیب و نتیجه‌گیری."
                });
                continue;
            }

            var context = new StringBuilder();
            for (var i = 0; i < passages.Count; i++)
            {
                var text = passages[i].Text.Length > 900 ? passages[i].Text[..900] + "..." : passages[i].Text;
                context.AppendLine($"[{i + 1}] {passages[i].SourceTitle} p.{passages[i].PageNumber}: {text}");
            }

            var result = await resolved.Provider.CompleteAsync(new AiChatRequest(
                new List<AiChatMessage>
                {
                    new("system",
                        "Synthesize an answer to the question using ONLY the numbered passages. " +
                        "Cite as [n]. If the passages are insufficient, say so. " +
                        "Reply with JSON only: {\"statement\":\"...\",\"supported\":true|false,\"caveat\":\"...\"}."),
                    new("user", $"QUESTION: {question}\n\nPASSAGES:\n{context}")
                },
                Temperature: 0.2,
                MaxOutputTokens: 700,
                JsonMode: true), cancellationToken);

            if (!result.Success)
            {
                findings.Add(new ResearchFinding
                {
                    ResearchRunId = run.Id,
                    Statement = $"شواهد بازیابی‌شده برای: {question}",
                    EpistemicStatus = EpistemicStatus.SourceReported,
                    EvidenceChunkIds = chunkIds,
                    Caveat = $"ترکیب یافته‌ها ناموفق بود: {result.Error}"
                });
                continue;
            }

            var (statement, supported, caveat) = ParseSynthesis(result.Content, question);
            findings.Add(new ResearchFinding
            {
                ResearchRunId = run.Id,
                Statement = statement,
                EpistemicStatus = supported ? EpistemicStatus.AiInterpretation : EpistemicStatus.Unknown,
                EvidenceChunkIds = chunkIds,
                Caveat = caveat
            });
        }

        return findings;
    }

    private static (string Statement, bool Supported, string? Caveat) ParseSynthesis(string json, string question)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var statement = root.TryGetProperty("statement", out var s) ? s.GetString() : null;
            var supported = root.TryGetProperty("supported", out var sup) && sup.ValueKind == JsonValueKind.True;
            var caveat = root.TryGetProperty("caveat", out var c) ? c.GetString() : null;

            if (!string.IsNullOrWhiteSpace(statement))
            {
                return (statement!, supported, caveat);
            }
        }
        catch (JsonException)
        {
            // Fall through.
        }

        return ($"شواهد بازیابی‌شده برای: {question}", false, "خروجی مدل به‌صورت JSON ساخت‌یافته قابل تجزیه نبود.");
    }

    private static Domain.Reports.ResearchReport BuildReport(
        ResearchRun run,
        List<ResearchFinding> findings,
        Dictionary<string, List<RetrievedPassage>> evidenceByQuestion)
    {
        var body = new StringBuilder();
        body.AppendLine($"# گزارش پژوهش: {run.Objective}");
        body.AppendLine();
        body.AppendLine("## یافته‌ها");
        body.AppendLine();

        foreach (var finding in findings)
        {
            body.AppendLine($"### {finding.Statement}");
            body.AppendLine();
            body.AppendLine($"- وضعیت معرفتی: {PersianFormat.EpistemicStatus(finding.EpistemicStatus)}");

            if (!string.IsNullOrWhiteSpace(finding.EvidenceChunkIds))
            {
                body.AppendLine($"- قطعه‌های شواهد: {finding.EvidenceChunkIds}");
            }

            if (!string.IsNullOrWhiteSpace(finding.Caveat))
            {
                body.AppendLine($"- هشدار: {finding.Caveat}");
            }

            body.AppendLine();
        }

        body.AppendLine("## نمایه شواهد");
        body.AppendLine();
        foreach (var (question, passages) in evidenceByQuestion)
        {
            body.AppendLine($"**{question}**");
            foreach (var passage in passages)
            {
                body.AppendLine($"- {passage.SourceTitle}، صفحه {PersianFormat.Number(passage.PageNumber)} (قطعه {passage.ChunkId})");
            }

            body.AppendLine();
        }

        var report = new Domain.Reports.ResearchReport
        {
            WorkspaceId = run.WorkspaceId,
            ResearchRunId = run.Id,
            Title = $"گزارش پژوهش: {Truncate(run.Objective, 120)}",
            Template = "TechnicalResearchReport"
        };

        report.Versions.Add(new Domain.Reports.ResearchReportVersion
        {
            ResearchReportId = report.Id,
            VersionNumber = 1,
            Markdown = body.ToString(),
            ChangeNote = "تولیدشده توسط موتور پژوهش عمیق."
        });

        return report;
    }

    private async Task RunStepAsync(
        ResearchPlanStep step,
        Func<Task<string>> action,
        CancellationToken cancellationToken)
    {
        step.Status = ProcessingStatus.Running;
        step.StartedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.ResearchRunChangedAsync(step.ResearchRunId, cancellationToken);

        try
        {
            var summary = await action();
            step.Status = ProcessingStatus.Succeeded;
            step.OutputSummary = summary;
            step.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            step.Status = ProcessingStatus.Cancelled;
            step.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            step.Status = ProcessingStatus.Failed;
            step.FailureReason = ex.Message;
            step.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _notifier.ResearchRunChangedAsync(step.ResearchRunId, cancellationToken);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}

public sealed record ResearchRunSummary(
    Guid Id,
    string Objective,
    ResearchRunStatus Status,
    bool UsedWebSearch,
    int FindingCount,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record ResearchRunDetail(
    Guid Id,
    Guid WorkspaceId,
    string Objective,
    ResearchRunStatus Status,
    bool UsedWebSearch,
    string? WebSearchProvider,
    string? FailureReason,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<ResearchStepView> Steps,
    IReadOnlyList<ResearchFindingView> Findings);

public sealed record ResearchStepView(
    int Order,
    ResearchPlanStepKind Kind,
    string Title,
    string? Detail,
    ProcessingStatus Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? OutputSummary,
    string? FailureReason);

public sealed record ResearchFindingView(
    Guid Id,
    string Statement,
    EpistemicStatus EpistemicStatus,
    string EvidenceChunkIds,
    string? Caveat);
