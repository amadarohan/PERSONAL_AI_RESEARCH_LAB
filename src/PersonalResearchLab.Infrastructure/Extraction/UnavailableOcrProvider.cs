using PersonalResearchLab.Application.Abstractions;

namespace PersonalResearchLab.Infrastructure.Extraction;

/// <summary>
/// The default OCR adapter. No OCR engine ships with this build, so this implementation reports
/// itself as unavailable and the image extractor surfaces a clear warning. It exists so the
/// dependency graph is complete and so enabling OCR later is a one-line registration change
/// rather than a refactor.
/// </summary>
public sealed class UnavailableOcrProvider : IOcrProvider
{
    public bool IsAvailable => false;

    public Task<string> RecognizeAsync(
        Stream imageContent,
        string? languageHint,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "هیچ ارائه‌دهنده OCR پیکربندی نشده است. برای خواندن منابع تصویری یک پیاده‌سازی IOcrProvider ثبت کنید.");
}
