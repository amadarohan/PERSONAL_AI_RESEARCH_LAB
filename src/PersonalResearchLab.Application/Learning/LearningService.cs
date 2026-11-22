using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Learning;

namespace PersonalResearchLab.Application.Learning;

/// <summary>
/// Learning paths, flashcards and quizzes. Mastery is derived from actual review performance
/// using SM-2, never from "opened the page" events.
/// </summary>
public sealed class LearningService
{
    private readonly IAppDbContext _db;

    public LearningService(IAppDbContext db) => _db = db;

    public async Task<LearningOverview> GetOverviewAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var paths = await _db.LearningPaths
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId)
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Select(p => new LearningPathView(
                p.Id,
                p.Title,
                p.Objective,
                p.Topics.Count,
                p.Topics.SelectMany(t => t.Flashcards).Count(),
                p.Topics.SelectMany(t => t.Flashcards).Count(f => f.DueAtUtc != null && f.DueAtUtc <= DateTime.UtcNow)))
            .ToListAsync(cancellationToken);

        var quizzes = await _db.Quizzes
            .AsNoTracking()
            .Where(q => q.WorkspaceId == workspaceId)
            .OrderByDescending(q => q.UpdatedAtUtc)
            .Select(q => new QuizView(
                q.Id,
                q.Title,
                q.Questions.Count,
                _db.QuizAttempts.Where(a => a.QuizId == q.Id).Count(),
                _db.QuizAttempts
                    .Where(a => a.QuizId == q.Id)
                    .OrderByDescending(a => a.CompletedAtUtc)
                    .Select(a => (int?)a.CorrectCount)
                    .FirstOrDefault(),
                _db.QuizAttempts
                    .Where(a => a.QuizId == q.Id)
                    .OrderByDescending(a => a.CompletedAtUtc)
                    .Select(a => (int?)a.TotalCount)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var dueCards = await _db.Flashcards
            .AsNoTracking()
            .Where(f => f.LearningTopic!.LearningPath!.WorkspaceId == workspaceId)
            .Where(f => f.DueAtUtc != null && f.DueAtUtc <= DateTime.UtcNow)
            .OrderBy(f => f.DueAtUtc)
            .Take(20)
            .Select(f => new DueCardView(
                f.Id,
                f.Front,
                f.Back,
                f.Repetitions,
                f.IntervalDays,
                f.EaseFactor,
                f.DueAtUtc,
                f.LearningTopic!.Title))
            .ToListAsync(cancellationToken);

        return new LearningOverview(paths, quizzes, dueCards);
    }

    public async Task<LearningPathDetail?> GetPathAsync(Guid pathId, CancellationToken cancellationToken = default)
    {
        var path = await _db.LearningPaths
            .AsNoTracking()
            .Include(p => p.Topics)
            .FirstOrDefaultAsync(p => p.Id == pathId, cancellationToken);

        if (path is null)
        {
            return null;
        }

        var topicIds = path.Topics.Select(t => t.Id).ToList();

        var cards = await _db.Flashcards
            .AsNoTracking()
            .Where(f => topicIds.Contains(f.LearningTopicId))
            .Select(f => new { f.LearningTopicId, f.Id, f.Repetitions, f.DueAtUtc })
            .ToListAsync(cancellationToken);

        var topics = path.Topics
            .OrderBy(t => t.SortOrder)
            .Select(t =>
            {
                var topicCards = cards.Where(c => c.LearningTopicId == t.Id).ToList();
                return new LearningTopicView(
                    t.Id,
                    t.Title,
                    t.Summary,
                    t.SortOrder,
                    topicCards.Count,
                    topicCards.Count(c => c.DueAtUtc != null && c.DueAtUtc <= DateTime.UtcNow),
                    topicCards.Count(c => c.Repetitions > 0));
            })
            .ToList();

        return new LearningPathDetail(path.Id, path.WorkspaceId, path.Title, path.Objective, topics);
    }

    /// <summary>
    /// Applies SM-2 to a review. Quality below 3 resets the interval, which is the standard
    /// behaviour and prevents a single lucky recall from inflating mastery.
    /// </summary>
    public async Task<ReviewOutcome> ReviewCardAsync(
        Guid flashcardId,
        int quality,
        CancellationToken cancellationToken = default)
    {
        if (quality is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(quality), "کیفیت باید بین ۰ و ۵ باشد.");
        }

        var card = await _db.Flashcards.FirstOrDefaultAsync(f => f.Id == flashcardId, cancellationToken)
            ?? throw new InvalidOperationException("کارت یافت نشد.");

        if (quality < 3)
        {
            card.Repetitions = 0;
            card.IntervalDays = 1;
        }
        else
        {
            card.Repetitions++;
            card.IntervalDays = card.Repetitions switch
            {
                1 => 1,
                2 => 6,
                _ => (int)Math.Round(card.IntervalDays * card.EaseFactor)
            };
        }

        card.EaseFactor = Math.Max(
            1.3,
            card.EaseFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02)));

        card.DueAtUtc = DateTime.UtcNow.AddDays(card.IntervalDays);

        _db.FlashcardReviews.Add(new FlashcardReview
        {
            FlashcardId = card.Id,
            Quality = quality,
            ReviewedAtUtc = DateTime.UtcNow
        });

        card.Touch();
        await _db.SaveChangesAsync(cancellationToken);

        return new ReviewOutcome(card.Id, card.Repetitions, card.IntervalDays, card.EaseFactor, card.DueAtUtc!.Value);
    }

    public async Task<QuizDetail?> GetQuizAsync(Guid quizId, CancellationToken cancellationToken = default)
    {
        var quiz = await _db.Quizzes
            .AsNoTracking()
            .Include(q => q.Questions)
            .FirstOrDefaultAsync(q => q.Id == quizId, cancellationToken);

        if (quiz is null)
        {
            return null;
        }

        var questions = quiz.Questions
            .OrderBy(q => q.CreatedAtUtc)
            .Select(q => new QuizQuestionView(
                q.Id,
                q.Prompt,
                q.OptionsJson,
                q.CorrectOptionIndex,
                q.ExpectedAnswer,
                q.Explanation,
                q.DocumentChunkId))
            .ToList();

        return new QuizDetail(quiz.Id, quiz.WorkspaceId, quiz.Title, questions);
    }

    public async Task<QuizAttemptResult> SubmitAttemptAsync(
        Guid quizId,
        IReadOnlyDictionary<Guid, int> answers,
        CancellationToken cancellationToken = default)
    {
        var questions = await _db.QuizQuestions
            .AsNoTracking()
            .Where(q => q.QuizId == quizId)
            .ToListAsync(cancellationToken);

        if (questions.Count == 0)
        {
            throw new InvalidOperationException("آزمون هیچ پرسشی ندارد.");
        }

        var results = new List<QuizAnswerResult>();
        var correct = 0;

        foreach (var question in questions)
        {
            answers.TryGetValue(question.Id, out var chosen);
            var isCorrect = question.CorrectOptionIndex is not null && chosen == question.CorrectOptionIndex;
            if (isCorrect)
            {
                correct++;
            }

            results.Add(new QuizAnswerResult(
                question.Id,
                question.Prompt,
                chosen,
                question.CorrectOptionIndex,
                isCorrect,
                question.Explanation));
        }

        _db.QuizAttempts.Add(new QuizAttempt
        {
            QuizId = quizId,
            CorrectCount = correct,
            TotalCount = questions.Count,
            CompletedAtUtc = DateTime.UtcNow,
            ResultsJson = System.Text.Json.JsonSerializer.Serialize(
                results.Select(r => new { r.QuestionId, r.ChosenIndex, r.IsCorrect }))
        });

        await _db.SaveChangesAsync(cancellationToken);
        return new QuizAttemptResult(correct, questions.Count, results);
    }
}

public sealed record LearningOverview(
    IReadOnlyList<LearningPathView> Paths,
    IReadOnlyList<QuizView> Quizzes,
    IReadOnlyList<DueCardView> DueCards);

public sealed record LearningPathView(
    Guid Id,
    string Title,
    string? Objective,
    int TopicCount,
    int CardCount,
    int DueCount);

public sealed record QuizView(
    Guid Id,
    string Title,
    int QuestionCount,
    int AttemptCount,
    int? LastCorrect,
    int? LastTotal);

public sealed record DueCardView(
    Guid Id,
    string Front,
    string Back,
    int Repetitions,
    int IntervalDays,
    double EaseFactor,
    DateTime? DueAtUtc,
    string TopicTitle);

public sealed record LearningPathDetail(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    string? Objective,
    IReadOnlyList<LearningTopicView> Topics);

public sealed record LearningTopicView(
    Guid Id,
    string Title,
    string? Summary,
    int SortOrder,
    int CardCount,
    int DueCount,
    int ReviewedCount);

public sealed record ReviewOutcome(
    Guid FlashcardId,
    int Repetitions,
    int IntervalDays,
    double EaseFactor,
    DateTime DueAtUtc);

public sealed record QuizDetail(
    Guid Id,
    Guid WorkspaceId,
    string Title,
    IReadOnlyList<QuizQuestionView> Questions);

public sealed record QuizQuestionView(
    Guid Id,
    string Prompt,
    string? OptionsJson,
    int? CorrectOptionIndex,
    string? ExpectedAnswer,
    string? Explanation,
    Guid? DocumentChunkId);

public sealed record QuizAttemptResult(
    int CorrectCount,
    int TotalCount,
    IReadOnlyList<QuizAnswerResult> Results);

public sealed record QuizAnswerResult(
    Guid QuestionId,
    string Prompt,
    int ChosenIndex,
    int? CorrectIndex,
    bool IsCorrect,
    string? Explanation);
