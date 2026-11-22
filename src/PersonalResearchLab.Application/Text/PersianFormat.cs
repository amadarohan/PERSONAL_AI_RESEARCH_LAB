using System.Globalization;
using System.Text;
using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Application.Text;

/// <summary>
/// Persian display formatting for text that the application layer persists and the UI renders
/// verbatim (timeline details, job titles, stage summaries). Keeping it here means persisted
/// user-facing text is already Persian and does not depend on the request culture.
/// </summary>
public static class PersianFormat
{
    private static readonly char[] PersianDigits =
    {
        '\u06F0', '\u06F1', '\u06F2', '\u06F3', '\u06F4',
        '\u06F5', '\u06F6', '\u06F7', '\u06F8', '\u06F9'
    };

    /// <summary>Replaces ASCII digits with Persian digits.</summary>
    public static string Digits(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch is >= '0' and <= '9' ? PersianDigits[ch - '0'] : ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats a number with Persian digits and the Persian thousands separator (U+066C).
    /// The invariant culture emits an ASCII comma, which looks wrong inside Persian text.
    /// </summary>
    public static string Number(long value) =>
        Digits(value.ToString("N0", CultureInfo.InvariantCulture))
            .Replace(',', '\u066C');

    /// <summary>Persian label for a source kind, used in persisted timeline details.</summary>
    public static string SourceKind(SourceKind kind) => kind switch
    {
        Domain.Common.SourceKind.Pdf => "PDF",
        Domain.Common.SourceKind.Docx => "Word",
        Domain.Common.SourceKind.Text => "متن",
        Domain.Common.SourceKind.Markdown => "Markdown",
        Domain.Common.SourceKind.Html => "HTML",
        Domain.Common.SourceKind.Csv => "CSV",
        Domain.Common.SourceKind.Json => "JSON",
        Domain.Common.SourceKind.Image => "تصویر",
        Domain.Common.SourceKind.WebPage => "صفحه وب",
        Domain.Common.SourceKind.ManualNote => "یادداشت دستی",
        Domain.Common.SourceKind.StructuredImport => "ورود ساختاریافته",
        _ => kind.ToString()
    };

    /// <summary>Persian label for a detected language code, used in persisted stage summaries.</summary>
    public static string Language(string code) => code switch
    {
        "fa" => "فارسی",
        "en" => "انگلیسی",
        "mixed" => "ترکیبی",
        "unknown" => "نامشخص",
        _ => code
    };

    /// <summary>Persian label for an epistemic status, used inside generated report bodies.</summary>
    public static string EpistemicStatus(EpistemicStatus status) => status switch
    {
        Domain.Common.EpistemicStatus.Verified => "تأییدشده",
        Domain.Common.EpistemicStatus.SourceReported => "به‌نقل از منبع",
        Domain.Common.EpistemicStatus.AiInterpretation => "تفسیر هوش مصنوعی",
        Domain.Common.EpistemicStatus.LogicalInference => "استنتاج منطقی",
        Domain.Common.EpistemicStatus.Hypothesis => "فرضیه",
        Domain.Common.EpistemicStatus.Unknown => "نامعلوم",
        _ => status.ToString()
    };
}
