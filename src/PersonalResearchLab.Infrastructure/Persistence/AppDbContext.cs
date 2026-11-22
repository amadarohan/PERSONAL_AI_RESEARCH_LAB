using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Ai;
using PersonalResearchLab.Domain.Documents;
using PersonalResearchLab.Domain.Evidence;
using PersonalResearchLab.Domain.Knowledge;
using PersonalResearchLab.Domain.Learning;
using PersonalResearchLab.Domain.Reports;
using PersonalResearchLab.Domain.Research;
using PersonalResearchLab.Domain.Sources;
using PersonalResearchLab.Domain.System;

namespace PersonalResearchLab.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ResearchWorkspace> Workspaces => Set<ResearchWorkspace>();
    public DbSet<ResearchQuestion> ResearchQuestions => Set<ResearchQuestion>();
    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();
    public DbSet<ResearchPlanStep> ResearchPlanSteps => Set<ResearchPlanStep>();
    public DbSet<ResearchFinding> ResearchFindings => Set<ResearchFinding>();
    public DbSet<ResearchNote> ResearchNotes => Set<ResearchNote>();
    public DbSet<ResearchTimelineEvent> TimelineEvents => Set<ResearchTimelineEvent>();

    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SourceVersion> SourceVersions => Set<SourceVersion>();

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<DocumentAnnotation> DocumentAnnotations => Set<DocumentAnnotation>();
    public DbSet<DocumentBookmark> DocumentBookmarks => Set<DocumentBookmark>();
    public DbSet<ChunkTerm> ChunkTerms => Set<ChunkTerm>();
    public DbSet<ChunkEmbedding> ChunkEmbeddings => Set<ChunkEmbedding>();

    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimEvidence> ClaimEvidence => Set<ClaimEvidence>();
    public DbSet<ClaimConflict> ClaimConflicts => Set<ClaimConflict>();

    public DbSet<KnowledgeNode> KnowledgeNodes => Set<KnowledgeNode>();
    public DbSet<KnowledgeRelationship> KnowledgeRelationships => Set<KnowledgeRelationship>();
    public DbSet<KnowledgeRelationshipEvidence> KnowledgeRelationshipEvidence => Set<KnowledgeRelationshipEvidence>();

    public DbSet<AiProvider> AiProviders => Set<AiProvider>();
    public DbSet<AiModelConfiguration> AiModelConfigurations => Set<AiModelConfiguration>();
    public DbSet<AiConversation> AiConversations => Set<AiConversation>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AiExecution> AiExecutions => Set<AiExecution>();
    public DbSet<AiProposal> AiProposals => Set<AiProposal>();

    public DbSet<LearningPath> LearningPaths => Set<LearningPath>();
    public DbSet<LearningTopic> LearningTopics => Set<LearningTopic>();
    public DbSet<Flashcard> Flashcards => Set<Flashcard>();
    public DbSet<FlashcardReview> FlashcardReviews => Set<FlashcardReview>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();

    public DbSet<ResearchReport> ResearchReports => Set<ResearchReport>();
    public DbSet<ResearchReportVersion> ResearchReportVersions => Set<ResearchReportVersion>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<ProcessingJobStep> ProcessingJobSteps => Set<ProcessingJobStep>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Domain.Common.Entity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
