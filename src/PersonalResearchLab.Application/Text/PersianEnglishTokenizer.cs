using System.Globalization;
using System.Text;
using PersonalResearchLab.Application.Abstractions;

namespace PersonalResearchLab.Application.Text;

/// <summary>
/// Language-aware tokenizer for Persian and English.
/// <para>
/// Persian needs explicit handling: Arabic presentation forms must be unified, the Arabic
/// yeh/kaf must map to the Persian forms, tatweel must be stripped, and ZWNJ must be treated
/// as a word separator so that compound words index correctly.
/// </para>
/// </summary>
public sealed class PersianEnglishTokenizer : ITextTokenizer
{
    private static readonly char[] Separators =
    {
        ' ', '\t', '\r', '\n', '\u200C', '\u200F', '\u200E', '\u00A0',
        '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}',
        '/', '\\', '|', '-', '_', '+', '=', '*', '&', '^', '%', '$', '#', '@', '~', '`', '<', '>',
        '،', '؛', '؟', '«', '»', '٪', 'ـ'
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // English
        "the", "a", "an", "and", "or", "but", "if", "then", "than", "that", "this", "these", "those",
        "is", "are", "was", "were", "be", "been", "being", "to", "of", "in", "on", "at", "for", "with",
        "by", "from", "as", "it", "its", "we", "you", "they", "he", "she", "i", "not", "no", "do",
        "does", "did", "can", "could", "should", "would", "will", "may", "might", "have", "has", "had",
        // Persian
        "و", "در", "به", "از", "که", "این", "آن", "را", "با", "برای", "است", "هست", "بود", "شد",
        "می", "نمی", "تا", "بر", "هم", "یا", "اگر", "چون", "ولی", "اما", "هر", "چه", "کە", "شود",
        "کرد", "کند", "کنند", "دارد", "دارند", "نیز", "بین", "روی", "پس", "پیش", "یک"
    };

    public string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(NormalizeChar(ch));
        }

        return builder.ToString();
    }

    private static char NormalizeChar(char ch) => ch switch
    {
        // Arabic yeh / alef maksura -> Persian yeh
        '\u064A' or '\u0649' => '\u06CC',
        // Arabic kaf -> Persian keheh
        '\u0643' => '\u06A9',
        // Arabic alef variants -> bare alef
        '\u0622' or '\u0623' or '\u0625' => '\u0627',
        // Teh marbuta -> heh
        '\u0629' => '\u0647',
        // Tatweel
        '\u0640' => '\u200C',
        // Arabic-Indic digits -> ASCII
        >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),
        // Extended Arabic-Indic digits -> ASCII
        >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),
        _ => ch
    };

    public IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var normalized = Normalize(text).ToLowerInvariant();
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in normalized)
        {
            if (Array.IndexOf(Separators, ch) >= 0 || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                Flush();
                continue;
            }

            // Strip Arabic diacritics (harakat) which are not meaningful for search.
            if (ch is >= '\u064B' and <= '\u0652')
            {
                continue;
            }

            current.Append(ch);
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var token = current.ToString();
            current.Clear();

            if (token.Length < 2 || StopWords.Contains(token))
            {
                return;
            }

            tokens.Add(token);
        }
    }

    /// <summary>Counts term frequencies for the inverted index.</summary>
    public IReadOnlyDictionary<string, int> CountTerms(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in Tokenize(text))
        {
            counts[token] = counts.TryGetValue(token, out var existing) ? existing + 1 : 1;
        }

        return counts;
    }
}

/// <summary>
/// Script-ratio language detection. Persian and English are the supported primary languages;
/// mixed content is reported as <c>mixed</c> rather than guessing.
/// </summary>
public sealed class ScriptLanguageDetector : ILanguageDetector
{
    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "unknown";
        }

        var persian = 0;
        var latin = 0;

        foreach (var ch in text)
        {
            if (ch is >= '\u0600' and <= '\u06FF')
            {
                persian++;
            }
            else if (char.IsLetter(ch) && ch < 128)
            {
                latin++;
            }
        }

        var total = persian + latin;
        if (total == 0)
        {
            return "unknown";
        }

        var persianRatio = (double)persian / total;
        if (persianRatio > 0.85)
        {
            return "fa";
        }

        if (persianRatio < 0.15)
        {
            return "en";
        }

        return "mixed";
    }
}