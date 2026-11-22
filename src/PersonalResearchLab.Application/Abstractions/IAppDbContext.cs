using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Domain.Ai;
using PersonalResearchLab.Domain.Documents;
using PersonalResearchLab.Domain.Evidence;
using PersonalResearchLab.Domain.Knowledge;
using PersonalResearchLab.Domain.Learning;
using PersonalResearchLab.Domain.Reports;
using PersonalResearchLab.Domain.Research;
using PersonalResearchLab.Domain.Sources;
using PersonalResearchLab.Domain.System;

namespace PersonalResearchLab.Application.Abstractions;

/// <summary>
/// Persistence boundary owned by the Application layer. Infrastructure supplies the EF Core
/// implementation; the Application layer never references SQL Server types.
/// </summary>
public interface IAppDbContext
{
    DbSet<ResearchWorkspace> Workspaces { get; }
    DbSet<ResearchQuestion> ResearchQuestions { get; }
    DbSet<ResearchRun> ResearchRuns { get; }
    DbSet<ResearchPlanStep> ResearchPlanSteps { get; }
    DbSet<ResearchFinding> ResearchFindings { get; }
    DbSet<ResearchNote> ResearchNotes { get; }
    DbSet<ResearchTimelineEvent> TimelineEvents { get; }

    DbSet<Source> Sources { get; }
    DbSet<SourceVersion> SourceVersions { get; }

    DbSet<Document> Documents { get; }
    DbSet<DocumentPage> DocumentPages { get; }
    DbSet<DocumentChunk> DocumentChunks { get; }
    DbSet<DocumentAnnotation> DocumentAnnotations { get; }
    DbSet<DocumentBookmark> DocumentBookmarks { get; }
    DbSet<ChunkTerm> ChunkTerms { get; }
    DbSet<ChunkEmbedding> ChunkEmbeddings { get; }

    DbSet<Claim> Claims { get; }
    DbSet<ClaimEvidence> ClaimEvidence { get; }
    DbSet<ClaimConflict> ClaimConflicts { get; }

    DbSet<KnowledgeNode> KnowledgeNodes { get; }
    DbSet<KnowledgeRelationship> KnowledgeRelationships { get; }
    DbSet<KnowledgeRelationshipEvidence> KnowledgeRelationshipEvidence { get; }

    DbSet<AiProvider> AiProviders { get; }
    DbSet<AiModelConfiguration> AiModelConfigurations { get; }
    DbSet<AiConversation> AiConversations { get; }
    DbSet<AiMessage> AiMessages { get; }
    DbSet<AiExecution> AiExecutions { get; }
    DbSet<AiProposal> AiProposals { get; }

    DbSet<LearningPath> LearningPaths { get; }
    DbSet<LearningTopic> LearningTopics { get; }
    DbSet<Flashcard> Flashcards { get; }
    DbSet<FlashcardReview> FlashcardReviews { get; }
    DbSet<Quiz> Quizzes { get; }
    DbSet<QuizQuestion> QuizQuestions { get; }
    DbSet<QuizAttempt> QuizAttempts { get; }

    DbSet<ResearchReport> ResearchReports { get; }
    DbSet<ResearchReportVersion> ResearchReportVersions { get; }

    DbSet<ProcessingJob> ProcessingJobs { get; }
    DbSet<ProcessingJobStep> ProcessingJobSteps { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
