using RawPreview.Protocol;

namespace RawPreview.Cli.Runtime;

public interface IWorkerClient
{
    Task<WorkerResponse> SendAsync(WorkerRequest request, CancellationToken cancellationToken);
}
