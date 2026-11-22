using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PersonalResearchLab.Application.Abstractions;
using PersonalResearchLab.Application.Ingestion;
using PersonalResearchLab.Domain.Common;

namespace PersonalResearchLab.Infrastructure.Jobs;

/// <summary>
/// In-process queue for background work. A local-first single-user application does not need a
/// distributed message broker; a bounded channel plus a hosted worker gives durable-enough
/// semantics because every job's state lives in the database, not in the queue.
/// </summary>
public sealed record IngestionWorkItem(Guid SourceVersionId, Guid JobId);

public sealed class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<IngestionWorkItem> _channel =
        Channel.CreateBounded<IngestionWorkItem>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    private readonly IServiceScopeFactory _scopeFactory;

    public BackgroundJobQueue(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public ValueTask EnqueueIngestionAsync(Guid sourceVersionId, Guid jobId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(new IngestionWorkItem(sourceVersionId, jobId), cancellationToken);

    public async Task RequestCancellationAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // The queue is a singleton, so it must not hold a scoped DbContext. A short-lived scope
        // keeps the cancellation flag write isolated from any request's unit of work.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var job = await db.ProcessingJobs.FindAsync(new object?[] { jobId }, cancellationToken);
        if (job is null || job.Status is ProcessingStatus.Succeeded or ProcessingStatus.Failed or ProcessingStatus.Cancelled)
        {
            return;
        }

        job.CancellationRequested = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    internal IAsyncEnumerable<IngestionWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Drains the ingestion queue. Each item runs in its own DI scope so a failed job cannot leak
/// a dirty DbContext into the next one.
/// </summary>
public sealed class IngestionWorker : BackgroundService
{
    private readonly BackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(
        BackgroundJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<IngestionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<IngestionService>();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

                // Honour a cancellation request that arrived while the item sat in the queue.
                var job = await db.ProcessingJobs.FindAsync(new object?[] { item.JobId }, stoppingToken);
                if (job is { CancellationRequested: true })
                {
                    job.Status = ProcessingStatus.Cancelled;
                    job.CurrentStage = "پیش از شروع لغو شد";
                    job.CompletedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(stoppingToken);
                    continue;
                }

                await ingestion.ProcessAsync(item.SourceVersionId, item.JobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // IngestionService already records failures; this guards the worker loop itself.
                _logger.LogError(ex, "Unhandled error while processing ingestion job {JobId}", item.JobId);
            }
        }
    }
}
