namespace PersonalResearchLab.Application.Abstractions;

/// <summary>
/// Queue for background work. The Application layer enqueues; Infrastructure supplies the
/// in-process implementation. Keeping the contract here preserves the dependency direction.
/// </summary>
public interface IBackgroundJobQueue
{
    ValueTask EnqueueIngestionAsync(Guid sourceVersionId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Marks a job for cooperative cancellation. The worker observes the flag between stages.</summary>
    Task RequestCancellationAsync(Guid jobId, CancellationToken cancellationToken = default);
}
