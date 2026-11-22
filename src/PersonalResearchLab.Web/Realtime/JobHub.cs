using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PersonalResearchLab.Application.Abstractions;

namespace PersonalResearchLab.Web.Realtime;

/// <summary>
/// SignalR hub for live processing and research state. Clients subscribe to a workspace group
/// so a busy workspace does not push updates to unrelated tabs.
/// </summary>
public sealed class JobHub : Hub
{
    public Task JoinWorkspace(Guid workspaceId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupName(workspaceId));

    public Task LeaveWorkspace(Guid workspaceId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(workspaceId));

    public static string GroupName(Guid workspaceId) => $"workspace:{workspaceId}";
}

/// <summary>
/// Application-layer notifier implemented over SignalR. The Application layer only sees
/// <see cref="IJobNotifier"/>; this class is the adapter.
/// </summary>
public sealed class SignalRJobNotifier : IJobNotifier
{
    private readonly IHubContext<JobHub> _hub;
    private readonly IAppDbContext _db;

    public SignalRJobNotifier(IHubContext<JobHub> hub, IAppDbContext db)
    {
        _hub = hub;
        _db = db;
    }

    public async Task JobChangedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _db.ProcessingJobs
            .Where(j => j.Id == jobId)
            .Select(j => new
            {
                j.Id,
                j.WorkspaceId,
                j.Title,
                j.JobType,
                j.Status,
                j.ProgressPercent,
                j.CurrentStage,
                j.FailureReason,
                j.CancellationRequested,
                j.StartedAtUtc,
                j.CompletedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return;
        }

        await _hub.Clients
            .Group(JobHub.GroupName(job.WorkspaceId))
            .SendAsync("jobChanged", job, cancellationToken);
    }

    public async Task ResearchRunChangedAsync(Guid researchRunId, CancellationToken cancellationToken = default)
    {
        var run = await _db.ResearchRuns
            .Where(r => r.Id == researchRunId)
            .Select(r => new { r.Id, r.WorkspaceId, r.Status, r.Objective })
            .FirstOrDefaultAsync(cancellationToken);

        if (run is null)
        {
            return;
        }

        await _hub.Clients
            .Group(JobHub.GroupName(run.WorkspaceId))
            .SendAsync("researchRunChanged", run, cancellationToken);
    }
}
