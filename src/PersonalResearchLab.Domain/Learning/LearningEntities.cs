using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Domain.Learning;

/// <summary>
/// A learning path turns collected knowledge into deliberate practice.
/// Mastery is derived from retrieval performance, never from reading completion.
/// </summary>
public class LearningPath : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Objective { get; set; }

    public ICollection<LearningTopic> Topics { get; set; } = new List<LearningTopic>();
}

public class LearningTopic : Entity
{
    public Guid LearningPathId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Optional knowledge node this topic teaches.</summary>
    public Guid? KnowledgeNodeId { get; set; }

    public LearningPath? LearningPath { get; set; }

    public ICollection<Flashcard> Flashcards { get; set; } = new List<Flashcard>();
}

public class Flashcard : Entity
{
    public Guid LearningTopicId { get; set; }

    public string Front { get; set; } = string.Empty;

    public string Back { get; set; } = string.Empty;

    /// <summary>Chunk the card was generated from, so the answer can be traced to a source.</summary>
    public Guid? DocumentChunkId { get; set; }

    /// <summary>SM-2 style ease factor. Starts at 2.5.</summary>
    public double EaseFactor { get; set; } = 2.5;

    public int IntervalDays { get; set; }

    public int Repetitions { get; set; }

    public DateTime? DueAtUtc { get; set; }

    public LearningTopic? LearningTopic { get; set; }

    public ICollection<FlashcardReview> Reviews { get; set; } = new List<FlashcardReview>();
}

public class FlashcardReview : Entity
{
    public Guid FlashcardId { get; set; }

    /// <summary>Self-reported recall quality, 0-5, per the SM-2 scale.</summary>
    public int Quality { get; set; }

    public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;

    public Flashcard? Flashcard { get; set; }
}

public class Quiz : Entity, IWorkspaceScoped
{
    public Guid WorkspaceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public Guid? LearningTopicId { get; set; }

    public ICollection<QuizQuestion> Questions { get; set; } = new List<QuizQuestion>();
}

public class QuizQuestion : Entity
{
    public Guid QuizId { get; set; }

    public string Prompt { get; set; } = string.Empty;

    /// <summary>JSON array of option strings for multiple-choice questions.</summary>
    public string? OptionsJson { get; set; }

    public int? CorrectOptionIndex { get; set; }

    /// <summary>Expected answer text for open-ended questions.</summary>
    public string? ExpectedAnswer { get; set; }

    public string? Explanation { get; set; }

    public Guid? DocumentChunkId { get; set; }

    public Quiz? Quiz { get; set; }
}

public class QuizAttempt : Entity
{
    public Guid QuizId { get; set; }

    public int CorrectCount { get; set; }

    public int TotalCount { get; set; }

    public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>JSON array of per-question results.</summary>
    public string? ResultsJson { get; set; }
}
