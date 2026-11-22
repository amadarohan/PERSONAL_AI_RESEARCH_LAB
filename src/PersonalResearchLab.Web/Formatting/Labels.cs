using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Web.Formatting;

/// <summary>
/// Persian display labels for domain enums. Kept in one place so a label change never requires
/// touching pages, and so no page can invent its own wording for an epistemic category.
/// </summary>
public static class Labels
{
    public static string Epistemic(EpistemicStatus status) => status switch
    {
        EpistemicStatus.Verified => "تأییدشده",
        EpistemicStatus.SourceReported => "به‌نقل از منبع",
        EpistemicStatus.AiInterpretation => "تفسیر هوش مصنوعی",
        EpistemicStatus.LogicalInference => "استنتاج منطقی",
        EpistemicStatus.Hypothesis => "فرضیه",
        EpistemicStatus.Unknown => "نامعلوم",
        _ => status.ToString()
    };

    public static string EpistemicHint(EpistemicStatus status) => status switch
    {
        EpistemicStatus.Verified => "شما این گزاره را در برابر منبع اولیه بررسی کرده‌اید.",
        EpistemicStatus.SourceReported => "منبع این را گفته است؛ درستی آن به اعتبار منبع وابسته است.",
        EpistemicStatus.AiInterpretation => "مدل زبانی این را از مطالب بازیابی‌شده برداشت کرده است.",
        EpistemicStatus.LogicalInference => "از مقدمات صریح استنتاج شده است.",
        EpistemicStatus.Hypothesis => "توضیح پیشنهادی است و هنوز شاهدی ندارد.",
        EpistemicStatus.Unknown => "دانسته‌شده‌ی نادانسته: شواهد کافی برای بیان آن وجود ندارد.",
        _ => string.Empty
    };

    public static string EpistemicBadge(EpistemicStatus status) => status switch
    {
        EpistemicStatus.Verified => "badge-emerald",
        EpistemicStatus.SourceReported => "badge-cyan",
        EpistemicStatus.AiInterpretation => "badge-violet",
        EpistemicStatus.LogicalInference => "badge-neutral",
        EpistemicStatus.Hypothesis => "badge-amber",
        EpistemicStatus.Unknown => "badge-neutral",
        _ => "badge-neutral"
    };

    public static string ClaimStatusLabel(ClaimStatus status) => status switch
    {
        ClaimStatus.Unreviewed => "بررسی‌نشده",
        ClaimStatus.Supported => "پشتیبانی‌شده",
        ClaimStatus.PartiallySupported => "تا حدی پشتیبانی‌شده",
        ClaimStatus.Contested => "مورد مناقشه",
        ClaimStatus.Contradicted => "رد‌شده",
        ClaimStatus.Outdated => "کهنه",
        ClaimStatus.Unverifiable => "غیرقابل بررسی",
        _ => status.ToString()
    };

    public static string ClaimStatusBadge(ClaimStatus status) => status switch
    {
        ClaimStatus.Supported => "badge-emerald",
        ClaimStatus.PartiallySupported => "badge-cyan",
        ClaimStatus.Contested => "badge-amber",
        ClaimStatus.Contradicted => "badge-crimson",
        ClaimStatus.Outdated => "badge-neutral",
        ClaimStatus.Unverifiable => "badge-neutral",
        _ => "badge-neutral"
    };

    public static string ClaimTypeLabel(ClaimType type) => type switch
    {
        ClaimType.Factual => "واقعی",
        ClaimType.Statistical => "آماری",
        ClaimType.Causal => "علّی",
        ClaimType.Predictive => "پیش‌بینی",
        ClaimType.Interpretive => "تفسیری",
        ClaimType.Normative => "هنجاری",
        ClaimType.Hypothetical => "فرضی",
        _ => type.ToString()
    };

    public static string StanceLabel(EvidenceStance stance) => stance switch
    {
        EvidenceStance.Supports => "پشتیبانی می‌کند",
        EvidenceStance.PartiallySupports => "تا حدی پشتیبانی می‌کند",
        EvidenceStance.Neutral => "بی‌طرف",
        EvidenceStance.PartiallyContradicts => "تا حدی رد می‌کند",
        EvidenceStance.Contradicts => "رد می‌کند",
        _ => stance.ToString()
    };

    public static string StanceBadge(EvidenceStance stance) => stance switch
    {
        EvidenceStance.Supports => "badge-emerald",
        EvidenceStance.PartiallySupports => "badge-cyan",
        EvidenceStance.Neutral => "badge-neutral",
        EvidenceStance.PartiallyContradicts => "badge-amber",
        EvidenceStance.Contradicts => "badge-crimson",
        _ => "badge-neutral"
    };

    public static string SourceKindLabel(SourceKind kind) => kind switch
    {
        SourceKind.Pdf => "PDF",
        SourceKind.Docx => "Word",
        SourceKind.Text => "متن",
        SourceKind.Markdown => "Markdown",
        SourceKind.Html => "HTML",
        SourceKind.Csv => "CSV",
        SourceKind.Json => "JSON",
        SourceKind.Image => "تصویر",
        SourceKind.WebPage => "صفحه وب",
        SourceKind.ManualNote => "یادداشت دستی",
        SourceKind.StructuredImport => "ورود ساختاریافته",
        _ => kind.ToString()
    };

    public static string StatusLabel(ProcessingStatus status) => status switch
    {
        ProcessingStatus.Pending => "در انتظار",
        ProcessingStatus.Running => "در حال اجرا",
        ProcessingStatus.Succeeded => "موفق",
        ProcessingStatus.Failed => "ناموفق",
        ProcessingStatus.Cancelled => "لغو‌شده",
        ProcessingStatus.Skipped => "رد‌شده",
        _ => status.ToString()
    };

    public static string StatusBadge(ProcessingStatus status) => status switch
    {
        ProcessingStatus.Pending => "badge-neutral",
        ProcessingStatus.Running => "badge-cyan",
        ProcessingStatus.Succeeded => "badge-emerald",
        ProcessingStatus.Failed => "badge-crimson",
        ProcessingStatus.Cancelled => "badge-neutral",
        ProcessingStatus.Skipped => "badge-neutral",
        _ => "badge-neutral"
    };

    public static string RunStatusLabel(ResearchRunStatus status) => status switch
    {
        ResearchRunStatus.Draft => "پیش‌نویس",
        ResearchRunStatus.AwaitingApproval => "در انتظار تأیید",
        ResearchRunStatus.Running => "در حال اجرا",
        ResearchRunStatus.Paused => "متوقف",
        ResearchRunStatus.Completed => "تکمیل‌شده",
        ResearchRunStatus.Cancelled => "لغو‌شده",
        ResearchRunStatus.Failed => "ناموفق",
        _ => status.ToString()
    };

    public static string RunStatusBadge(ResearchRunStatus status) => status switch
    {
        ResearchRunStatus.Draft => "badge-neutral",
        ResearchRunStatus.AwaitingApproval => "badge-amber",
        ResearchRunStatus.Running => "badge-cyan",
        ResearchRunStatus.Paused => "badge-neutral",
        ResearchRunStatus.Completed => "badge-emerald",
        ResearchRunStatus.Cancelled => "badge-neutral",
        ResearchRunStatus.Failed => "badge-crimson",
        _ => "badge-neutral"
    };

    public static string NodeKindLabel(KnowledgeNodeKind kind) => kind switch
    {
        KnowledgeNodeKind.Concept => "مفهوم",
        KnowledgeNodeKind.Person => "شخص",
        KnowledgeNodeKind.Organization => "سازمان",
        KnowledgeNodeKind.Technology => "فناوری",
        KnowledgeNodeKind.Event => "رویداد",
        KnowledgeNodeKind.Document => "سند",
        KnowledgeNodeKind.Claim => "ادعا",
        KnowledgeNodeKind.ResearchQuestion => "پرسش پژوهشی",
        KnowledgeNodeKind.Topic => "موضوع",
        _ => kind.ToString()
    };

    public static string RelationKindLabel(KnowledgeRelationKind kind) => kind switch
    {
        KnowledgeRelationKind.Supports => "پشتیبانی می‌کند",
        KnowledgeRelationKind.Contradicts => "رد می‌کند",
        KnowledgeRelationKind.DependsOn => "وابسته است به",
        KnowledgeRelationKind.RelatedTo => "مرتبط است با",
        KnowledgeRelationKind.DerivedFrom => "برگرفته از",
        KnowledgeRelationKind.Mentions => "اشاره می‌کند به",
        KnowledgeRelationKind.Explains => "توضیح می‌دهد",
        KnowledgeRelationKind.Causes => "باعث می‌شود",
        _ => kind.ToString()
    };

    public static string PlanStepLabel(ResearchPlanStepKind kind) => kind switch
    {
        ResearchPlanStepKind.ClarifyObjective => "شفاف‌سازی هدف",
        ResearchPlanStepKind.DecomposeQuestions => "تجزیه به پرسش‌ها",
        ResearchPlanStepKind.InventoryKnowledge => "فهرست‌برداری از دانش موجود",
        ResearchPlanStepKind.RetrieveEvidence => "بازیابی شواهد",
        ResearchPlanStepKind.EvaluateEvidence => "ارزیابی شواهد",
        ResearchPlanStepKind.DetectContradictions => "تشخیص تناقض‌ها",
        ResearchPlanStepKind.FollowUpQuestions => "پرسش‌های پیگیری",
        ResearchPlanStepKind.Synthesize => "ترکیب و جمع‌بندی",
        ResearchPlanStepKind.DraftReport => "نگارش پیش‌نویس گزارش",
        _ => kind.ToString()
    };

    public static string DataRouteLabel(DataRoute route) => route switch
    {
        Domain.Common.DataRoute.LocalOnly => "فقط محلی",
        Domain.Common.DataRoute.CloudAllowed => "ابر مجاز",
        _ => route.ToString()
    };

    public static string DataRouteBadge(DataRoute route) => route switch
    {
        Domain.Common.DataRoute.LocalOnly => "badge-emerald",
        Domain.Common.DataRoute.CloudAllowed => "badge-amber",
        _ => "badge-neutral"
    };

    public static string ProviderKindLabel(AiProviderKind kind) => kind switch
    {
        AiProviderKind.OpenAiCompatible => "سازگار با OpenAI",
        AiProviderKind.Ollama => "Ollama (محلی)",
        AiProviderKind.AzureOpenAi => "Azure OpenAI",
        AiProviderKind.DeterministicFake => "آزمایشی قطعی",
        _ => kind.ToString()
    };

    public static string CapabilityLabel(AiCapability capability) => capability switch
    {
        AiCapability.Chat => "گفت‌وگو",
        AiCapability.Embedding => "تعبیه برداری",
        AiCapability.Reranking => "بازچینش",
        AiCapability.Ocr => "تشخیص متن تصویر",
        AiCapability.Transcription => "تبدیل گفتار",
        _ => capability.ToString()
    };

    /// <summary>Maps a status to the CSS class used by the step marker.</summary>
    public static string StepMarkerClass(ProcessingStatus status) => status switch
    {
        ProcessingStatus.Succeeded => "succeeded",
        ProcessingStatus.Running => "running",
        ProcessingStatus.Failed => "failed",
        _ => string.Empty
    };

    public static string StepMarkerGlyph(ProcessingStatus status) => status switch
    {
        ProcessingStatus.Succeeded => "✓",
        ProcessingStatus.Running => "•",
        ProcessingStatus.Failed => "✕",
        ProcessingStatus.Cancelled => "–",
        ProcessingStatus.Skipped => "–",
        _ => "○"
    };

    /// <summary>
    /// Translates the retrieval reason produced by the retrieval layer into Persian.
    /// The retrieval layer emits stable English tokens; the UI must not show them raw.
    /// </summary>
    public static string RetrievalReasonLabel(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "دلیل بازیابی ثبت نشده";
        }

        var parts = reason
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part switch
            {
                "lexical match" => "تطابق واژگانی",
                "semantic similarity" => "شباهت معنایی",
                _ => part
            });

        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Translates a timeline event type key into Persian. Event types are stable machine keys
    /// stored in the database, so the mapping lives in the presentation layer.
    /// </summary>
    public static string EventTypeLabel(string? eventType) => eventType switch
    {
        "workspace.created" => "ساخت فضای کاری",
        "source.added" => "افزودن منبع",
        "research.planned" => "طرح پژوهش",
        "research.completed" => "پایان پژوهش",
        "claim.statusChanged" => "تغییر وضعیت ادعا",
        "claim.created" => "ثبت ادعا",
        "claim.conflictResolved" => "حل تناقض",
        "knowledge.nodeCreated" => "ساخت گره دانش",
        "knowledge.relationshipCreated" => "ساخت رابطه دانش",
        "report.created" => "ساخت گزارش",
        "report.versionAdded" => "نسخه جدید گزارش",
        "learning.reviewed" => "مرور کارت",
        null or "" => "رویداد",
        _ => eventType
    };

    /// <summary>Translates a processing job type key into Persian.</summary>
    public static string JobTypeLabel(string? jobType) => jobType switch
    {
        "DocumentIngestion" => "پردازش سند",
        null or "" => "کار پردازش",
        _ => jobType
    };

    /// <summary>
    /// Translates a report template key into Persian. The stored value is a stable machine key,
    /// so the mapping belongs in the presentation layer.
    /// </summary>
    public static string ReportTemplate(string? template) => template switch
    {
        "technical" or "TechnicalResearchReport" => "فنی",
        "executive" or "ExecutiveSummary" => "خلاصه مدیریتی",
        "literature" or "LiteratureReview" => "مرور ادبیات",
        "comparison" or "ComparativeAnalysis" => "مقایسه‌ای",
        null or "" => "قالب نامشخص",
        _ => template
    };
}
