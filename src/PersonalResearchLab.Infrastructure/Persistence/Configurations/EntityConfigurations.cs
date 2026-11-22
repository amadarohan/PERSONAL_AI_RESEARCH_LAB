using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalResearchLab.Domain.Ai;
using PersonalResearchLab.Domain.Common;
using PersonalResearchLab.Domain.Documents;
using PersonalResearchLab.Domain.Evidence;
using PersonalResearchLab.Domain.Knowledge;
using PersonalResearchLab.Domain.Learning;
using PersonalResearchLab.Domain.Reports;
using PersonalResearchLab.Domain.Research;
using PersonalResearchLab.Domain.Sources;
using PersonalResearchLab.Domain.System;

namespace PersonalResearchLab.Infrastructure.Persistence.Configurations;

internal static class ConfigurationHelpers
{
    /// <summary>Applies the shared conventions every entity in this model follows.</summary>
    public static EntityTypeBuilder<T> ConfigureEntity<T>(this EntityTypeBuilder<T> builder)
        where T : Entity
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.UpdatedAtUtc).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();
        return builder;
    }
}

public sealed class ResearchWorkspaceConfiguration : IEntityTypeConfiguration<ResearchWorkspace>
{
    public void Configure(EntityTypeBuilder<ResearchWorkspace> builder)
    {
        builder.ConfigureEntity();
        builder.Property(w => w.Name).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Slug).IsRequired().HasMaxLength(200);
        builder.Property(w => w.Description).HasMaxLength(4000);
        builder.Property(w => w.Objective).HasMaxLength(4000);
        builder.Property(w => w.AccentColor).HasMaxLength(16);
        builder.Property(w => w.AiInstructions).HasMaxLength(8000);
        builder.Property(w => w.PreferredLanguage).HasMaxLength(16);
        builder.HasIndex(w => w.Slug).IsUnique();
        builder.HasIndex(w => w.IsArchived);
    }
}

public sealed class ResearchQuestionConfiguration : IEntityTypeConfiguration<ResearchQuestion>
{
    public void Configure(EntityTypeBuilder<ResearchQuestion> builder)
    {
        builder.ConfigureEntity();
        builder.Property(q => q.Text).IsRequired().HasMaxLength(2000);
        builder.Property(q => q.Rationale).HasMaxLength(2000);
        builder.HasOne(q => q.Workspace)
            .WithMany(w => w.Questions)
            .HasForeignKey(q => q.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(q => new { q.WorkspaceId, q.SortOrder });
    }
}

public sealed class ResearchRunConfiguration : IEntityTypeConfiguration<ResearchRun>
{
    public void Configure(EntityTypeBuilder<ResearchRun> builder)
    {
        builder.ConfigureEntity();
        builder.Property(r => r.Objective).IsRequired().HasMaxLength(4000);
        builder.Property(r => r.FailureReason).HasMaxLength(2000);
        builder.Property(r => r.WebSearchProvider).HasMaxLength(100);
        builder.HasOne(r => r.Workspace)
            .WithMany(w => w.Runs)
            .HasForeignKey(r => r.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.WorkspaceId, r.Status });
    }
}

public sealed class ResearchPlanStepConfiguration : IEntityTypeConfiguration<ResearchPlanStep>
{
    public void Configure(EntityTypeBuilder<ResearchPlanStep> builder)
    {
        builder.ConfigureEntity();
        builder.Property(s => s.Title).IsRequired().HasMaxLength(300);
        builder.Property(s => s.Detail).HasMaxLength(4000);
        builder.Property(s => s.OutputSummary).HasMaxLength(4000);
        builder.Property(s => s.FailureReason).HasMaxLength(2000);
        builder.HasOne(s => s.ResearchRun)
            .WithMany(r => r.PlanSteps)
            .HasForeignKey(s => s.ResearchRunId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.ResearchRunId, s.Order });
    }
}

public sealed class ResearchFindingConfiguration : IEntityTypeConfiguration<ResearchFinding>
{
    public void Configure(EntityTypeBuilder<ResearchFinding> builder)
    {
        builder.ConfigureEntity();
        builder.Property(f => f.Statement).IsRequired().HasMaxLength(4000);
        builder.Property(f => f.EvidenceChunkIds).HasMaxLength(4000);
        builder.Property(f => f.Caveat).HasMaxLength(2000);
        builder.HasOne(f => f.ResearchRun)
            .WithMany(r => r.Findings)
            .HasForeignKey(f => f.ResearchRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ResearchNoteConfiguration : IEntityTypeConfiguration<ResearchNote>
{
    public void Configure(EntityTypeBuilder<ResearchNote> builder)
    {
        builder.ConfigureEntity();
        builder.Property(n => n.Title).IsRequired().HasMaxLength(300);
        builder.Property(n => n.Body).IsRequired().HasMaxLength(20000);
        builder.Property(n => n.Tags).HasMaxLength(500);
        builder.HasOne(n => n.Workspace)
            .WithMany(w => w.Notes)
            .HasForeignKey(n => n.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ResearchTimelineEventConfiguration : IEntityTypeConfiguration<ResearchTimelineEvent>
{
    public void Configure(EntityTypeBuilder<ResearchTimelineEvent> builder)
    {
        builder.ConfigureEntity();
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(300);
        builder.Property(e => e.Detail).HasMaxLength(2000);
        builder.HasIndex(e => new { e.WorkspaceId, e.CreatedAtUtc });
    }
}

public sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ConfigureEntity();
        builder.Property(s => s.Title).IsRequired().HasMaxLength(500);
        builder.Property(s => s.Author).HasMaxLength(300);
        builder.Property(s => s.Publisher).HasMaxLength(300);
        builder.Property(s => s.Url).HasMaxLength(2000);
        builder.Property(s => s.Language).HasMaxLength(16);
        builder.Property(s => s.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(s => s.OriginalFileName).HasMaxLength(500);
        builder.Property(s => s.Tags).HasMaxLength(500);
        builder.Property(s => s.ReliabilityRationale).HasMaxLength(2000);
        builder.HasIndex(s => new { s.WorkspaceId, s.IsArchived });
        builder.HasIndex(s => s.ContentHash);
    }
}

public sealed class SourceVersionConfiguration : IEntityTypeConfiguration<SourceVersion>
{
    public void Configure(EntityTypeBuilder<SourceVersion> builder)
    {
        builder.ConfigureEntity();
        builder.Property(v => v.ContentHash).IsRequired().HasMaxLength(64);
        builder.Property(v => v.StoragePath).IsRequired().HasMaxLength(1000);
        builder.Property(v => v.ProcessingError).HasMaxLength(2000);
        builder.HasOne(v => v.Source)
            .WithMany(s => s.Versions)
            .HasForeignKey(v => v.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => new { v.SourceId, v.VersionNumber }).IsUnique();
    }
}

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ConfigureEntity();
        builder.Property(d => d.Title).IsRequired().HasMaxLength(500);
        builder.Property(d => d.Language).HasMaxLength(16);
        builder.HasOne<SourceVersion>()
            .WithOne(v => v.Document)
            .HasForeignKey<Document>(d => d.SourceVersionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => d.WorkspaceId);
    }
}

public sealed class DocumentPageConfiguration : IEntityTypeConfiguration<DocumentPage>
{
    public void Configure(EntityTypeBuilder<DocumentPage> builder)
    {
        builder.ConfigureEntity();
        builder.Property(p => p.Text).IsRequired();
        builder.HasOne(p => p.Document)
            .WithMany(d => d.Pages)
            .HasForeignKey(p => p.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.DocumentId, p.PageNumber }).IsUnique();
    }
}

public sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.ConfigureEntity();
        builder.Property(c => c.Text).IsRequired();
        builder.Property(c => c.ContentHash).IsRequired().HasMaxLength(64);
        builder.HasOne(c => c.Document)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex }).IsUnique();
        builder.HasIndex(c => c.WorkspaceId);
    }
}

public sealed class ChunkTermConfiguration : IEntityTypeConfiguration<ChunkTerm>
{
    public void Configure(EntityTypeBuilder<ChunkTerm> builder)
    {
        builder.ConfigureEntity();
        builder.Property(t => t.Term).IsRequired().HasMaxLength(120);
        builder.HasIndex(t => new { t.WorkspaceId, t.Term });
        builder.HasIndex(t => t.DocumentChunkId);
    }
}

public sealed class ChunkEmbeddingConfiguration : IEntityTypeConfiguration<ChunkEmbedding>
{
    public void Configure(EntityTypeBuilder<ChunkEmbedding> builder)
    {
        builder.ConfigureEntity();
        builder.Property(e => e.Vector).IsRequired();
        builder.Property(e => e.ModelName).IsRequired().HasMaxLength(200);
        builder.HasIndex(e => e.DocumentChunkId).IsUnique();
        builder.HasIndex(e => e.WorkspaceId);
    }
}

public sealed class DocumentAnnotationConfiguration : IEntityTypeConfiguration<DocumentAnnotation>
{
    public void Configure(EntityTypeBuilder<DocumentAnnotation> builder)
    {
        builder.ConfigureEntity();
        builder.Property(a => a.SelectedText).IsRequired().HasMaxLength(4000);
        builder.Property(a => a.Note).HasMaxLength(4000);
        builder.Property(a => a.Color).HasMaxLength(16);
        builder.HasIndex(a => a.DocumentId);
    }
}

public sealed class DocumentBookmarkConfiguration : IEntityTypeConfiguration<DocumentBookmark>
{
    public void Configure(EntityTypeBuilder<DocumentBookmark> builder)
    {
        builder.ConfigureEntity();
        builder.Property(b => b.Label).IsRequired().HasMaxLength(300);
        builder.HasIndex(b => b.DocumentId);
    }
}

public sealed class ClaimConfiguration : IEntityTypeConfiguration<Claim>
{
    public void Configure(EntityTypeBuilder<Claim> builder)
    {
        builder.ConfigureEntity();
        builder.Property(c => c.Statement).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.SourceExcerpt).HasMaxLength(4000);
        builder.Property(c => c.TemporalContext).HasMaxLength(200);
        builder.Property(c => c.UserAnnotation).HasMaxLength(4000);
        builder.HasIndex(c => new { c.WorkspaceId, c.Status });
        builder.HasIndex(c => c.SourceId);
    }
}

public sealed class ClaimEvidenceConfiguration : IEntityTypeConfiguration<ClaimEvidence>
{
    public void Configure(EntityTypeBuilder<ClaimEvidence> builder)
    {
        builder.ConfigureEntity();
        builder.Property(e => e.Excerpt).IsRequired().HasMaxLength(4000);
        builder.Property(e => e.Rationale).HasMaxLength(2000);
        builder.HasOne(e => e.Claim)
            .WithMany(c => c.Evidence)
            .HasForeignKey(e => e.ClaimId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.DocumentChunkId);
    }
}

public sealed class ClaimConflictConfiguration : IEntityTypeConfiguration<ClaimConflict>
{
    public void Configure(EntityTypeBuilder<ClaimConflict> builder)
    {
        builder.ConfigureEntity();
        builder.Property(c => c.Explanation).IsRequired().HasMaxLength(4000);
        builder.Property(c => c.Resolution).HasMaxLength(4000);
        builder.HasIndex(c => c.WorkspaceId);
    }
}

public sealed class KnowledgeNodeConfiguration : IEntityTypeConfiguration<KnowledgeNode>
{
    public void Configure(EntityTypeBuilder<KnowledgeNode> builder)
    {
        builder.ConfigureEntity();
        builder.Property(n => n.Name).IsRequired().HasMaxLength(300);
        builder.Property(n => n.NormalizedName).IsRequired().HasMaxLength(300);
        builder.Property(n => n.Description).HasMaxLength(4000);
        builder.HasIndex(n => new { n.WorkspaceId, n.NormalizedName });
        builder.HasIndex(n => new { n.WorkspaceId, n.Kind });
    }
}

public sealed class KnowledgeRelationshipConfiguration : IEntityTypeConfiguration<KnowledgeRelationship>
{
    public void Configure(EntityTypeBuilder<KnowledgeRelationship> builder)
    {
        builder.ConfigureEntity();
        builder.Property(r => r.Description).HasMaxLength(2000);
        builder.HasIndex(r => new { r.WorkspaceId, r.FromNodeId });
        builder.HasIndex(r => new { r.WorkspaceId, r.ToNodeId });
        builder.HasIndex(r => new { r.FromNodeId, r.ToNodeId, r.Kind }).IsUnique();
    }
}

public sealed class KnowledgeRelationshipEvidenceConfiguration : IEntityTypeConfiguration<KnowledgeRelationshipEvidence>
{
    public void Configure(EntityTypeBuilder<KnowledgeRelationshipEvidence> builder)
    {
        builder.ConfigureEntity();
        builder.Property(e => e.Excerpt).IsRequired().HasMaxLength(4000);
        builder.HasOne(e => e.KnowledgeRelationship)
            .WithMany(r => r.Evidence)
            .HasForeignKey(e => e.KnowledgeRelationshipId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AiProviderConfiguration : IEntityTypeConfiguration<AiProvider>
{
    public void Configure(EntityTypeBuilder<AiProvider> builder)
    {
        builder.ConfigureEntity();
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.BaseUrl).IsRequired().HasMaxLength(1000);
        builder.Property(p => p.ApiKeySecretName).HasMaxLength(200);
        builder.Property(p => p.LastValidationResult).HasMaxLength(2000);
        builder.HasIndex(p => p.Name).IsUnique();
    }
}

public sealed class AiModelConfigurationConfiguration : IEntityTypeConfiguration<AiModelConfiguration>
{
    public void Configure(EntityTypeBuilder<AiModelConfiguration> builder)
    {
        builder.ConfigureEntity();
        builder.Property(m => m.ModelName).IsRequired().HasMaxLength(200);
        builder.HasOne(m => m.AiProvider)
            .WithMany()
            .HasForeignKey(m => m.AiProviderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.AiProviderId, m.Capability });
    }
}

public sealed class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ConfigureEntity();
        builder.Property(c => c.Title).IsRequired().HasMaxLength(300);
        builder.Property(c => c.ContextKind).HasMaxLength(50);
        builder.HasIndex(c => new { c.WorkspaceId, c.CreatedAtUtc });
    }
}

public sealed class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        builder.ConfigureEntity();
        builder.Property(m => m.Role).IsRequired().HasMaxLength(20);
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.ModelName).HasMaxLength(200);
        builder.HasOne(m => m.AiConversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.AiConversationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.AiConversationId, m.CreatedAtUtc });
    }
}

public sealed class AiExecutionConfiguration : IEntityTypeConfiguration<AiExecution>
{
    public void Configure(EntityTypeBuilder<AiExecution> builder)
    {
        builder.ConfigureEntity();
        builder.Property(e => e.Purpose).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ProviderName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.ModelName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Error).HasMaxLength(2000);
        builder.HasIndex(e => new { e.WorkspaceId, e.StartedAtUtc });
    }
}

public sealed class AiProposalConfiguration : IEntityTypeConfiguration<AiProposal>
{
    public void Configure(EntityTypeBuilder<AiProposal> builder)
    {
        builder.ConfigureEntity();
        builder.Property(p => p.ProposalType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Title).IsRequired().HasMaxLength(300);
        builder.Property(p => p.PayloadJson).IsRequired();
        builder.HasIndex(p => new { p.WorkspaceId, p.IsApproved, p.IsRejected });
    }
}

public sealed class LearningPathConfiguration : IEntityTypeConfiguration<LearningPath>
{
    public void Configure(EntityTypeBuilder<LearningPath> builder)
    {
        builder.ConfigureEntity();
        builder.Property(p => p.Title).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Objective).HasMaxLength(2000);
        builder.HasIndex(p => p.WorkspaceId);
    }
}

public sealed class LearningTopicConfiguration : IEntityTypeConfiguration<LearningTopic>
{
    public void Configure(EntityTypeBuilder<LearningTopic> builder)
    {
        builder.ConfigureEntity();
        builder.Property(t => t.Title).IsRequired().HasMaxLength(300);
        builder.Property(t => t.Summary).HasMaxLength(4000);
        builder.HasOne(t => t.LearningPath)
            .WithMany(p => p.Topics)
            .HasForeignKey(t => t.LearningPathId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.LearningPathId, t.SortOrder });
    }
}

public sealed class FlashcardConfiguration : IEntityTypeConfiguration<Flashcard>
{
    public void Configure(EntityTypeBuilder<Flashcard> builder)
    {
        builder.ConfigureEntity();
        builder.Property(f => f.Front).IsRequired().HasMaxLength(2000);
        builder.Property(f => f.Back).IsRequired().HasMaxLength(4000);
        builder.HasOne(f => f.LearningTopic)
            .WithMany(t => t.Flashcards)
            .HasForeignKey(f => f.LearningTopicId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(f => f.DueAtUtc);
    }
}

public sealed class FlashcardReviewConfiguration : IEntityTypeConfiguration<FlashcardReview>
{
    public void Configure(EntityTypeBuilder<FlashcardReview> builder)
    {
        builder.ConfigureEntity();
        builder.HasOne(r => r.Flashcard)
            .WithMany(f => f.Reviews)
            .HasForeignKey(r => r.FlashcardId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.FlashcardId, r.ReviewedAtUtc });
    }
}

public sealed class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> builder)
    {
        builder.ConfigureEntity();
        builder.Property(q => q.Title).IsRequired().HasMaxLength(300);
        builder.HasIndex(q => q.WorkspaceId);
    }
}

public sealed class QuizQuestionConfiguration : IEntityTypeConfiguration<QuizQuestion>
{
    public void Configure(EntityTypeBuilder<QuizQuestion> builder)
    {
        builder.ConfigureEntity();
        builder.Property(q => q.Prompt).IsRequired().HasMaxLength(2000);
        builder.Property(q => q.ExpectedAnswer).HasMaxLength(4000);
        builder.Property(q => q.Explanation).HasMaxLength(4000);
        builder.HasOne(q => q.Quiz)
            .WithMany(q => q.Questions)
            .HasForeignKey(q => q.QuizId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public void Configure(EntityTypeBuilder<QuizAttempt> builder)
    {
        builder.ConfigureEntity();
        builder.HasIndex(a => new { a.QuizId, a.CompletedAtUtc });
    }
}

public sealed class ResearchReportConfiguration : IEntityTypeConfiguration<ResearchReport>
{
    public void Configure(EntityTypeBuilder<ResearchReport> builder)
    {
        builder.ConfigureEntity();
        builder.Property(r => r.Title).IsRequired().HasMaxLength(300);
        builder.Property(r => r.Template).IsRequired().HasMaxLength(50);
        builder.HasIndex(r => r.WorkspaceId);
    }
}

public sealed class ResearchReportVersionConfiguration : IEntityTypeConfiguration<ResearchReportVersion>
{
    public void Configure(EntityTypeBuilder<ResearchReportVersion> builder)
    {
        builder.ConfigureEntity();
        builder.Property(v => v.Markdown).IsRequired();
        builder.Property(v => v.CitationsJson).IsRequired();
        builder.Property(v => v.ChangeNote).HasMaxLength(1000);
        builder.HasOne(v => v.ResearchReport)
            .WithMany(r => r.Versions)
            .HasForeignKey(v => v.ResearchReportId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => new { v.ResearchReportId, v.VersionNumber }).IsUnique();
    }
}

public sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.ConfigureEntity();
        builder.Property(j => j.JobType).IsRequired().HasMaxLength(100);
        builder.Property(j => j.Title).IsRequired().HasMaxLength(300);
        builder.Property(j => j.CurrentStage).HasMaxLength(200);
        builder.Property(j => j.FailureReason).HasMaxLength(2000);
        builder.HasIndex(j => new { j.WorkspaceId, j.Status });
        builder.HasIndex(j => j.CreatedAtUtc);
    }
}

public sealed class ProcessingJobStepConfiguration : IEntityTypeConfiguration<ProcessingJobStep>
{
    public void Configure(EntityTypeBuilder<ProcessingJobStep> builder)
    {
        builder.ConfigureEntity();
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Detail).HasMaxLength(2000);
        builder.HasOne(s => s.ProcessingJob)
            .WithMany(j => j.Steps)
            .HasForeignKey(s => s.ProcessingJobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.ProcessingJobId, s.Order });
    }
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ConfigureEntity();
        builder.Property(s => s.Key).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(8000);
        builder.HasIndex(s => s.Key).IsUnique();
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ConfigureEntity();
        builder.Property(a => a.Category).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Detail).HasMaxLength(4000);
        builder.HasIndex(a => a.CreatedAtUtc);
    }
}
