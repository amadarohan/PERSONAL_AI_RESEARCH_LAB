using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Documents;

/// <summary>
/// Extracted, structured representation of a source version. One document per source version.
/// </summary>
public class Document : Entity
{
    public Guid SourceVersionId { get; set; }

    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Language { get; set; }

    public int PageCount { get; set; }

    public int CharacterCount { get; set; }

    /// <summary>True when at least one page required OCR to produce text.</summary>
    public bool UsedOcr { get; set; }

    public ICollection<DocumentPage> Pages { get; set; } = new List<DocumentPage>();

    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}

public class DocumentPage : Entity
{
    public Guid DocumentId { get; set; }

    /// <summary>1-based page number as printed in the original document.</summary>
    public int PageNumber { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool OcrApplied { get; set; }

    public Document? Document { get; set; }
}

/// <summary>
/// Retrieval unit. Chunks carry enough provenance to navigate back to an exact page and offset,
/// which is what makes citations trustworthy.
/// </summary>
public class DocumentChunk : Entity
{
    public Guid DocumentId { get; set; }

    public Guid WorkspaceId { get; set; }

    public int PageNumber { get; set; }

    public int ChunkIndex { get; set; }

    /// <summary>Character offset of the chunk start within the page text.</summary>
    public int StartOffset { get; set; }

    public int EndOffset { get; set; }

    public string Text { get; set; } = string.Empty;

    public int TokenEstimate { get; set; }

    /// <summary>Hash of the normalized chunk text; used to skip re-embedding unchanged content.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public bool IsEmbedded { get; set; }

    public Document? Document { get; set; }
}

public class DocumentAnnotation : Entity
{
    public Guid DocumentId { get; set; }

    public int PageNumber { get; set; }

    public int StartOffset { get; set; }

    public int EndOffset { get; set; }

    public string SelectedText { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string? Color { get; set; }
}

public class DocumentBookmark : Entity
{
    public Guid DocumentId { get; set; }

    public int PageNumber { get; set; }

    public string Label { get; set; } = string.Empty;
}
