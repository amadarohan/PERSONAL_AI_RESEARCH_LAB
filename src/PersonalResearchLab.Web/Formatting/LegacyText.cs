using System.Globalization;
using System.Text.RegularExpressions;
using PersonalResearchLab.Application.Text;

namespace PersonalResearchLab.Web.Formatting;

/// <summary>
/// Presentation-only normalizer for user-facing text that was persisted before the UI became
/// Persian-first. Rows written by older builds still carry English stage names, job titles and
/// timeline details; rewriting stored history would falsify the audit trail, so the translation
/// happens at render time instead. New rows are already Persian and pass through unchanged.
/// </summary>
public static partial class LegacyText
{
    /// <summary>Normalizes a persisted processing-job title such as <c>Ingest: ...</c>.</summary>
    public static string JobTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var match = IngestTitlePattern().Match(title);
        return match.Success
            ? $"پردازش: {match.Groups[1].Value}"
            : title;
    }

    /// <summary>Normalizes a persisted processing-job step name.</summary>
    public static string StageName(string? name) => name switch
    {
        null or "" => string.Empty,
        "Validate file" => "اعتبارسنجی فایل",
        "Hash and detect duplicates" => "درهم‌سازی و تشخیص تکرار",
        "Extract text" => "استخراج متن",
        "Detect language" => "تشخیص زبان",
        "Segment into chunks" => "قطعه‌بندی متن",
        "Build lexical index" => "ساخت نمایه واژگانی",
        "Generate embeddings" => "تولید تعبیه برداری",
        "Complete" => "پایان",
        _ => name
    };

    /// <summary>Normalizes the current-stage label shown on a job card.</summary>
    public static string CurrentStage(string? stage) =>
        string.IsNullOrWhiteSpace(stage) ? "—" : StageName(stage);

    /// <summary>Normalizes a persisted processing-job step detail line.</summary>
    public static string StageDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        switch (detail)
        {
            case "Ingestion finished":
                return "پردازش کامل شد";
            case "No duplicates":
                return "نسخه تکراری یافت نشد";
            case "Skipped: no embedding provider configured (lexical search still works)":
                return "رد شد: ارائه‌دهنده تعبیه برداری تنظیم نشده است (جست‌وجوی واژگانی همچنان کار می‌کند)";
        }

        var bytes = BytesPattern().Match(detail);
        if (bytes.Success)
        {
            return $"{PersianNumber(bytes.Groups[1].Value)} بایت";
        }

        var pages = PagesPattern().Match(detail);
        if (pages.Success)
        {
            return $"{PersianNumber(pages.Groups[1].Value)} صفحه، {PersianNumber(pages.Groups[2].Value)} نویسه";
        }

        var chunks = ChunksPattern().Match(detail);
        if (chunks.Success)
        {
            return $"{PersianNumber(chunks.Groups[1].Value)} قطعه";
        }

        var postings = PostingsPattern().Match(detail);
        if (postings.Success)
        {
            return $"{PersianNumber(postings.Groups[1].Value)} ورودی نمایه";
        }

        var detected = DetectedPattern().Match(detail);
        if (detected.Success)
        {
            return $"تشخیص‌داده‌شده: {LanguageName(detected.Groups[1].Value)}";
        }

        return detail;
    }

    /// <summary>Normalizes a persisted timeline event title such as <c>Source added: ...</c>.</summary>
    public static string TimelineTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var source = SourceAddedPattern().Match(title);
        if (source.Success)
        {
            return $"منبع افزوده شد: {source.Groups[1].Value}";
        }

        var workspace = WorkspaceCreatedPattern().Match(title);
        return workspace.Success
            ? $"فضای کاری ساخته شد: {workspace.Groups[1].Value}"
            : title;
    }

    /// <summary>Normalizes a persisted timeline event detail such as <c>ManualNote, 1,894 bytes</c>.</summary>
    public static string TimelineDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var match = SourceDetailPattern().Match(detail);
        if (!match.Success)
        {
            return detail;
        }

        var kind = match.Groups[1].Value switch
        {
            "ManualNote" => "یادداشت دستی",
            "WebPage" => "صفحه وب",
            "StructuredImport" => "ورود ساختاریافته",
            "Image" => "تصویر",
            "Text" => "متن",
            "Pdf" => "PDF",
            "Docx" => "Word",
            "Markdown" => "Markdown",
            "Html" => "HTML",
            "Csv" => "CSV",
            "Json" => "JSON",
            var other => other
        };

        return $"{kind}، {PersianNumber(match.Groups[2].Value)} بایت";
    }

    private static string PersianNumber(string value)
    {
        var digits = value.Replace(",", string.Empty, StringComparison.Ordinal);
        digits = ToAsciiDigits(digits);
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? PersianFormat.Number(number)
            : value;
    }

    /// <summary>Maps Persian and Arabic-Indic digits back to ASCII so a mixed row still parses.</summary>
    private static string ToAsciiDigits(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            buffer[i] = ch switch
            {
                >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
                >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
                _ => ch
            };
        }

        return new string(buffer);
    }

    private static string LanguageName(string code) => code switch
    {
        "fa" => "فارسی",
        "en" => "انگلیسی",
        "mixed" => "ترکیبی",
        "unknown" => "نامشخص",
        _ => code
    };

    [GeneratedRegex(@"^Ingest:\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex IngestTitlePattern();

    [GeneratedRegex(@"^Source added:\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceAddedPattern();

    [GeneratedRegex(@"^Workspace created:\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceCreatedPattern();

    [GeneratedRegex(@"^([A-Za-z]+)[,،]\s*([\d,]+)\s*(?:bytes|بایت)$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceDetailPattern();

    [GeneratedRegex(@"^([\d,]+)\s*bytes$", RegexOptions.CultureInvariant)]
    private static partial Regex BytesPattern();

    [GeneratedRegex(@"^([\d,]+)\s*page\(s\),\s*([\d,]+)\s*characters$", RegexOptions.CultureInvariant)]
    private static partial Regex PagesPattern();

    [GeneratedRegex(@"^([\d,]+)\s*chunk\(s\)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChunksPattern();

    [GeneratedRegex(@"^([\d,]+)\s*term posting\(s\)$", RegexOptions.CultureInvariant)]
    private static partial Regex PostingsPattern();

    [GeneratedRegex(@"^Detected:\s*(\w+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DetectedPattern();
}
