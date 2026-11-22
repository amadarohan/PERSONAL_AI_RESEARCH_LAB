using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Application.Abstractions;

/// <summary>Managed local file storage. Paths are always relative to a configured root.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string suggestedFileName, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Resolves a relative path to an absolute path, rejecting traversal attempts.</summary>
    string ResolveAbsolutePath(string relativePath);
}

/// <summary>Extracts plain text plus page boundaries from a stored file.</summary>
public interface ITextExtractor
{
    /// <summary>Returns true when this extractor can handle the given source kind and file name.</summary>
    bool CanHandle(SourceKind kind, string? fileName);

    Task<ExtractionResult> ExtractAsync(
        Stream content,
        SourceKind kind,
        string? fileName,
        CancellationToken cancellationToken = default);
}

public sealed record ExtractedPage(int PageNumber, string Text, bool OcrApplied);

public sealed record ExtractionResult(
    IReadOnlyList<ExtractedPage> Pages,
    string? DetectedLanguage,
    bool UsedOcr,
    string? Warning = null);

/// <summary>Optional OCR adapter. Implementations must degrade gracefully when unavailable.</summary>
public interface IOcrProvider
{
    bool IsAvailable { get; }

    Task<string> RecognizeAsync(Stream imageContent, string? languageHint, CancellationToken cancellationToken = default);
}

/// <summary>Normalizes and tokenizes text for the lexical index. Language-aware, no external service.</summary>
public interface ITextTokenizer
{
    IReadOnlyList<string> Tokenize(string text);

    string Normalize(string text);

    /// <summary>Term frequencies for the inverted index, keyed by normalized term.</summary>
    IReadOnlyDictionary<string, int> CountTerms(string text);
}

/// <summary>Deterministic, dependency-free language detection for Persian/English/mixed content.</summary>
public interface ILanguageDetector
{
    string Detect(string text);
}
