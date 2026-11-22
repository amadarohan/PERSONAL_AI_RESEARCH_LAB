using System.Text;
using PersonalResearchLab.Application.Abstractions;

namespace PersonalResearchLab.Application.Ingestion;

/// <summary>
/// Splits page text into overlapping chunks suitable for retrieval.
/// <para>
/// Chunking is sentence-aware: it prefers to break on sentence boundaries so a retrieved
/// passage reads as a complete thought rather than a truncated fragment. Offsets are preserved
/// so the reader can highlight the exact span.
/// </para>
/// </summary>
public sealed class TextChunker
{
    private const int TargetChunkChars = 1100;
    private const int MaxChunkChars = 1600;
    private const int OverlapChars = 180;

    private static readonly char[] SentenceTerminators = { '.', '!', '?', '۔', '؟', '\n' };

    public IReadOnlyList<ChunkDraft> Chunk(string pageText, int pageNumber)
    {
        if (string.IsNullOrWhiteSpace(pageText))
        {
            return Array.Empty<ChunkDraft>();
        }

        var chunks = new List<ChunkDraft>();
        var index = 0;
        var position = 0;

        while (position < pageText.Length)
        {
            var remaining = pageText.Length - position;
            var length = Math.Min(TargetChunkChars, remaining);

            if (remaining > TargetChunkChars)
            {
                length = FindBreakPoint(pageText, position, length);
            }

            var text = pageText.Substring(position, length).Trim();
            if (text.Length > 0)
            {
                chunks.Add(new ChunkDraft(
                    PageNumber: pageNumber,
                    ChunkIndex: index++,
                    StartOffset: position,
                    EndOffset: position + length,
                    Text: text,
                    TokenEstimate: EstimateTokens(text)));
            }

            if (position + length >= pageText.Length)
            {
                break;
            }

            position += Math.Max(1, length - OverlapChars);
        }

        return chunks;
    }

    private static int FindBreakPoint(string text, int start, int maxLength)
    {
        var limit = Math.Min(start + maxLength, text.Length);
        var searchStart = Math.Min(start + (maxLength / 2), limit - 1);

        for (var i = limit - 1; i >= searchStart; i--)
        {
            if (Array.IndexOf(SentenceTerminators, text[i]) >= 0)
            {
                return i - start + 1;
            }
        }

        // Fall back to the last whitespace so words are not split.
        for (var i = limit - 1; i >= searchStart; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i - start + 1;
            }
        }

        return maxLength;
    }

    /// <summary>Rough token estimate: ~4 characters per token for Latin, ~2.5 for Persian script.</summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var persian = 0;
        foreach (var ch in text)
        {
            if (ch is >= '\u0600' and <= '\u06FF')
            {
                persian++;
            }
        }

        var ratio = (double)persian / text.Length;
        var charsPerToken = 4.0 - (ratio * 1.5);
        return (int)Math.Ceiling(text.Length / charsPerToken);
    }

    public static string HashContent(string text)
    {
        var normalized = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
            {
                normalized.Append(char.ToLowerInvariant(ch));
            }
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(normalized.ToString()));
        return Convert.ToHexString(bytes);
    }
}

public sealed record ChunkDraft(int PageNumber, int ChunkIndex, int StartOffset, int EndOffset, string Text, int TokenEstimate);
