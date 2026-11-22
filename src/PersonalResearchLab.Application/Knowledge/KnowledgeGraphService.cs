using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Domain.Common;
using PersonalResearchLab.Domain.Knowledge;

namespace PersonalResearchLab.Application.Knowledge;

/// <summary>
/// Knowledge graph read model. Nodes and relationships are only ever created by explicit user
/// action or by an approved AI proposal, so this service is read-mostly plus confirmation flags.
/// </summary>
public sealed class KnowledgeGraphService
{
    private readonly IAppDbContext _db;

    public KnowledgeGraphService(IAppDbContext db) => _db = db;

    public async Task<KnowledgeGraphView> GetGraphAsync(
        Guid workspaceId,
        KnowledgeNodeKind? kindFilter = null,
        CancellationToken cancellationToken = default)
    {
        var nodeQuery = _db.KnowledgeNodes
            .AsNoTracking()
            .Where(n => n.WorkspaceId == workspaceId && n.MergedIntoNodeId == null);

        if (kindFilter is not null)
        {
            nodeQuery = nodeQuery.Where(n => n.Kind == kindFilter);
        }

        var nodes = await nodeQuery
            .OrderByDescending(n => n.SourceCount)
            .ThenBy(n => n.Name)
            .Select(n => new KnowledgeNodeView(
                n.Id,
                n.Kind,
                n.Name,
                n.Description,
                n.SourceCount,
                n.IsUserConfirmed))
            .ToListAsync(cancellationToken);

        var nodeIds = nodes.Select(n => n.Id).ToHashSet();

        var relationships = await _db.KnowledgeRelationships
            .AsNoTracking()
            .Where(r => r.WorkspaceId == workspaceId)
            .Select(r => new KnowledgeRelationshipView(
                r.Id,
                r.FromNodeId,
                r.ToNodeId,
                r.Kind,
                r.Description,
                r.IsUserConfirmed,
                r.IsAiSuggested,
                r.Evidence.Count))
            .ToListAsync(cancellationToken);

        // Only keep edges whose endpoints survived the filter, so the view is never dangling.
        var visible = relationships
            .Where(r => nodeIds.Contains(r.FromNodeId) && nodeIds.Contains(r.ToNodeId))
            .ToList();

        var counts = new KnowledgeCounts(
            NodeCount: nodes.Count,
            RelationshipCount: visible.Count,
            ConfirmedNodeCount: nodes.Count(n => n.IsUserConfirmed),
            UnconfirmedRelationshipCount: visible.Count(r => !r.IsUserConfirmed),
            OrphanNodeCount: nodes.Count(n =>
                !visible.Any(r => r.FromNodeId == n.Id || r.ToNodeId == n.Id)));

        return new KnowledgeGraphView(nodes, visible, counts);
    }

    public async Task<KnowledgeNodeDetail?> GetNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.KnowledgeNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken);

        if (node is null)
        {
            return null;
        }

        var outgoing = await _db.KnowledgeRelationships
            .AsNoTracking()
            .Where(r => r.FromNodeId == nodeId)
            .Select(r => new KnowledgeEdgeView(
                r.Id,
                r.Kind,
                r.Description,
                r.IsUserConfirmed,
                _db.KnowledgeNodes.Where(n => n.Id == r.ToNodeId).Select(n => n.Name).FirstOrDefault(),
                r.ToNodeId,
                true))
            .ToListAsync(cancellationToken);

        var incoming = await _db.KnowledgeRelationships
            .AsNoTracking()
            .Where(r => r.ToNodeId == nodeId)
            .Select(r => new KnowledgeEdgeView(
                r.Id,
                r.Kind,
                r.Description,
                r.IsUserConfirmed,
                _db.KnowledgeNodes.Where(n => n.Id == r.FromNodeId).Select(n => n.Name).FirstOrDefault(),
                r.FromNodeId,
                false))
            .ToListAsync(cancellationToken);

        return new KnowledgeNodeDetail(
            node.Id,
            node.WorkspaceId,
            node.Kind,
            node.Name,
            node.Description,
            node.SourceCount,
            node.IsUserConfirmed,
            node.CreatedAtUtc,
            outgoing,
            incoming);
    }

    public async Task<Guid> CreateNodeAsync(
        Guid workspaceId,
        string name,
        KnowledgeNodeKind kind,
        string? description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("نام گره الزامی است.", nameof(name));
        }

        var node = new KnowledgeNode
        {
            WorkspaceId = workspaceId,
            Name = name.Trim(),
            NormalizedName = name.Trim().ToLowerInvariant(),
            Kind = kind,
            Description = description?.Trim(),
            IsUserConfirmed = true
        };

        _db.KnowledgeNodes.Add(node);
        await _db.SaveChangesAsync(cancellationToken);
        return node.Id;
    }

    public async Task<Guid> CreateRelationshipAsync(
        Guid workspaceId,
        Guid fromNodeId,
        Guid toNodeId,
        KnowledgeRelationKind kind,
        string? description,
        CancellationToken cancellationToken = default)
    {
        if (fromNodeId == toNodeId)
        {
            throw new ArgumentException("یک گره نمی‌تواند به خودش متصل شود.", nameof(toNodeId));
        }

        var bothExist = await _db.KnowledgeNodes.CountAsync(
            n => n.WorkspaceId == workspaceId && (n.Id == fromNodeId || n.Id == toNodeId),
            cancellationToken) == 2;

        if (!bothExist)
        {
            throw new InvalidOperationException("هر دو گره باید در این فضای کاری وجود داشته باشند.");
        }

        var relationship = new KnowledgeRelationship
        {
            WorkspaceId = workspaceId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            Kind = kind,
            Description = description?.Trim(),
            IsUserConfirmed = true,
            IsAiSuggested = false
        };

        _db.KnowledgeRelationships.Add(relationship);
        await _db.SaveChangesAsync(cancellationToken);
        return relationship.Id;
    }

    public async Task ConfirmNodeAsync(Guid nodeId, CancellationToken cancellationToken = default)
    {
        var node = await _db.KnowledgeNodes.FirstOrDefaultAsync(n => n.Id == nodeId, cancellationToken)
            ?? throw new InvalidOperationException("گره یافت نشد.");

        node.IsUserConfirmed = true;
        node.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ConfirmRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken = default)
    {
        var relationship = await _db.KnowledgeRelationships
            .FirstOrDefaultAsync(r => r.Id == relationshipId, cancellationToken)
            ?? throw new InvalidOperationException("رابطه یافت نشد.");

        relationship.IsUserConfirmed = true;
        relationship.Touch();
        await _db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record KnowledgeGraphView(
    IReadOnlyList<KnowledgeNodeView> Nodes,
    IReadOnlyList<KnowledgeRelationshipView> Relationships,
    KnowledgeCounts Counts);

public sealed record KnowledgeCounts(
    int NodeCount,
    int RelationshipCount,
    int ConfirmedNodeCount,
    int UnconfirmedRelationshipCount,
    int OrphanNodeCount);

public sealed record KnowledgeNodeView(
    Guid Id,
    KnowledgeNodeKind Kind,
    string Name,
    string? Description,
    int SourceCount,
    bool IsUserConfirmed);

public sealed record KnowledgeRelationshipView(
    Guid Id,
    Guid FromNodeId,
    Guid ToNodeId,
    KnowledgeRelationKind Kind,
    string? Description,
    bool IsUserConfirmed,
    bool IsAiSuggested,
    int EvidenceCount);

public sealed record KnowledgeNodeDetail(
    Guid Id,
    Guid WorkspaceId,
    KnowledgeNodeKind Kind,
    string Name,
    string? Description,
    int SourceCount,
    bool IsUserConfirmed,
    DateTime CreatedAtUtc,
    IReadOnlyList<KnowledgeEdgeView> Outgoing,
    IReadOnlyList<KnowledgeEdgeView> Incoming);

public sealed record KnowledgeEdgeView(
    Guid RelationshipId,
    KnowledgeRelationKind Kind,
    string? Description,
    bool IsUserConfirmed,
    string? OtherNodeName,
    Guid OtherNodeId,
    bool IsOutgoing);
