namespace PersonalResearchLab.Application.Abstractions;

/// <summary>
/// Pushes real-time job state to connected clients. Implemented with SignalR in the web layer;
/// the Application layer only knows this contract.
/// </summary>
public interface IJobNotifier
{
    Task JobChangedAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task ResearchRunChangedAsync(Guid researchRunId, CancellationToken cancellationToken = default);
}
