using System.Text;
using AngleSharp.Html.Parser;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Common;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PersonalResearchLab.Infrastructure.Extraction;

/// <summary>
/// Extracts text from PDF files page by page. Pages with no extractable text are reported as
/// empty rather than silently dropped, so the UI can tell the user OCR is required.
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    private readonly ILogger<PdfTextExtractor> _logger;

    public PdfTextExtractor(ILogger<PdfTextExtractor> logger) => _logger = logger;

    public bool CanHandle(SourceKind kind, string? fileName) =>
        kind == SourceKind.Pdf ||
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractionResult> ExtractAsync(
        Stream content,
        SourceKind kind,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        var pages = new List<ExtractedPage>();
        var emptyPages = 0;

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        buffer.Position = 0;

        using var document = PdfDocument.Open(buffer);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = BuildPageText(page);
            if (string.IsNullOrWhiteSpace(text))
            {
                emptyPages++;
            }

            pages.Add(new ExtractedPage(page.Number, text, OcrApplied: false));
        }

        var warning = emptyPages > 0
            ? $"{emptyPages} page(s) contained no extractable text and may require OCR."
            : null;

        if (emptyPages > 0)
        {
            _logger.LogWarning("PDF {FileName}: {EmptyPages} page(s) had no extractable text", fileName, emptyPages);
        }

        return Task.FromResult(new ExtractionResult(pages, null, false, warning));
    }

    private static string BuildPageText(Page page)
    {
        var builder = new StringBuilder();
        var lastY = double.NaN;

        // Reconstruct reading order by grouping words into lines using their vertical position.
        // PdfPig exposes word geometry through BoundingBox; Bottom is the baseline-ish anchor.
        foreach (var word in page.GetWords()
                     .OrderByDescending(w => w.BoundingBox.Bottom)
                     .ThenBy(w => w.BoundingBox.Left))
        {
            var y = word.BoundingBox.Bottom;
            if (!double.IsNaN(lastY) && Math.Abs(y - lastY) > 2.5)
            {
                builder.Append('\n');
            }
            else if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append(' ');
            }

            builder.Append(word.Text);
            lastY = y;
        }

        return builder.ToString().Trim();
    }
}

/// <summary>Extracts paragraph text from DOCX files, preserving paragraph breaks.</summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    public bool CanHandle(SourceKind kind, string? fileName) =>
        kind == SourceKind.Docx ||
        string.Equals(Path.GetExtension(fileName), ".docx", StringComparison.OrdinalIgnoreCase);

    public Task<ExtractionResult> ExtractAsync(
        Stream content,
        SourceKind kind,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        buffer.Position = 0;

        using var document = WordprocessingDocument.Open(buffer, false);
        var body = document.MainDocumentPart?.Document?.Body;
        var builder = new StringBuilder();

        if (body is not null)
        {
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text);
                }
            }
        }

        var pages = new List<ExtractedPage>
        {
            new(1, builder.ToString().Trim(), OcrApplied: false)
        };

        return Task.FromResult(new ExtractionResult(pages, null, false));
    }
}

/// <summary>Extracts visible text from HTML, stripping scripts and styles.</summary>
public sealed class HtmlTextExtractor : ITextExtractor
{
    public bool CanHandle(SourceKind kind, string? fileName)
    {
        var extension = Path.GetExtension(fileName);
        return kind is SourceKind.Html or SourceKind.WebPage ||
               string.Equals(extension, ".html", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".htm", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ExtractionResult> ExtractAsync(
        Stream content,
        SourceKind kind,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var html = await reader.ReadToEndAsync(cancellationToken);

        var parser = new HtmlParser();
        using var document = await parser.ParseDocumentAsync(html, cancellationToken);

        foreach (var element in document.QuerySelectorAll("script, style, noscript, svg"))
        {
            element.Remove();
        }

        var text = document.Body?.TextContent ?? document.DocumentElement.TextContent;
        var normalized = NormalizeWhitespace(text);

        var pages = new List<ExtractedPage> { new(1, normalized, OcrApplied: false) };
        return new ExtractionResult(pages, null, false);
    }

    private static string NormalizeWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var lastWasWhitespace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasWhitespace)
                {
                    builder.Append(ch == '\n' || ch == '\r' ? '\n' : ' ');
                }

                lastWasWhitespace = true;
                continue;
            }

            builder.Append(ch);
            lastWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}

/// <summary>Handles plain text, Markdown, CSV, and JSON by reading them as UTF-8 text.</summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    public bool CanHandle(SourceKind kind, string? fileName)
    {
        if (kind is SourceKind.Text or SourceKind.Markdown or SourceKind.Csv or SourceKind.Json or SourceKind.ManualNote)
        {
            return true;
        }

        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension is ".txt" or ".md" or ".markdown" or ".csv" or ".json";
    }

    public async Task<ExtractionResult> ExtractAsync(
        Stream content,
        SourceKind kind,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken);

        var pages = new List<ExtractedPage> { new(1, text.Trim(), OcrApplied: false) };
        return new ExtractionResult(pages, null, false);
    }
}

/// <summary>
/// Image sources require OCR. When no OCR provider is available the extractor reports a clear
/// warning instead of pretending the image was read.
/// </summary>
public sealed class ImageTextExtractor : ITextExtractor
{
    private readonly IOcrProvider _ocr;

    public ImageTextExtractor(IOcrProvider ocr) => _ocr = ocr;

    public bool CanHandle(SourceKind kind, string? fileName)
    {
        if (kind == SourceKind.Image)
        {
            return true;
        }

        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".tif" or ".tiff";
    }

    public async Task<ExtractionResult> ExtractAsync(
        Stream content,
        SourceKind kind,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        if (!_ocr.IsAvailable)
        {
            return new ExtractionResult(
                new List<ExtractedPage> { new(1, string.Empty, OcrApplied: false) },
                null,
                false,
                "OCR پیکربندی نشده است. برای خواندن منابع تصویری، ارائه‌دهنده OCR را در تنظیمات فعال کنید.");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var text = await _ocr.RecognizeAsync(buffer, null, cancellationToken);
        var pages = new List<ExtractedPage> { new(1, text, OcrApplied: true) };
        return new ExtractionResult(pages, null, true);
    }
}
