using System.IO.Pipes;
using System.Text;
using RawPreview.Protocol;
using RawPreview.Worker.Photos;

namespace RawPreview.Worker;

public static class Program
{
    private const string PackagePipePrefix = "rawpreview-";

    public static Task<int> Main(string[] args) => args switch
    {
        ["--package-pipe", var pipeName] => RunPackagePipeAsync(pipeName),
        ["--jsonl"] => RunJsonLinesAsync(),
        _ => Task.FromResult(1)
    };

    private static async Task<int> RunJsonLinesAsync()
    {
        var session = new WorkerSession();
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            await Console.Out.WriteLineAsync(WorkerProtocol.Serialize(await session.HandleLineAsync(line)));
            await Console.Out.FlushAsync();
        }
        return 0;
    }

    private static async Task<int> RunPackagePipeAsync(string pipeName)
    {
        if (!IsPackagePipeName(pipeName)) return 1;

        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(120_000);
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            var line = await reader.ReadLineAsync() ?? throw new InvalidDataException("Worker request is empty.");
            var response = await new WorkerSession().HandleLineAsync(line);
            await writer.WriteLineAsync(WorkerProtocol.Serialize(response));
            return response.Ok ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static bool IsPackagePipeName(string pipeName) =>
        pipeName.Length == PackagePipePrefix.Length + 32 &&
        pipeName.StartsWith(PackagePipePrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(pipeName[PackagePipePrefix.Length..], "N", out _);

    private sealed class WorkerSession
    {
        private PhotosRuntime? runtime;

        public async Task<WorkerResponse> HandleLineAsync(string line)
        {
            try
            {
                return await HandleAsync(WorkerProtocol.ReadRequest(line));
            }
            catch (PhotosRuntimeException exception)
            {
                return Failure(exception.Code, exception.Message);
            }
            catch (Exception exception)
            {
                return Failure("WorkerProtocolError", exception.Message);
            }
        }

        private async Task<WorkerResponse> HandleAsync(WorkerRequest request)
        {
            runtime ??= PhotosRuntimeLocator.Discover();
            var report = runtime.Report;
            if (request.Operation == "doctor")
            {
                var code = report.MissingCapabilities.FirstOrDefault() ?? "Ok";
                return new WorkerResponse(WorkerProtocol.Version, code == "Ok", code, "Runtime probe complete.", null, null, 0, 0, null, report.PhotosVersion, report.RawExtensionVersion, report);
            }
            if (request.Operation == "self-test")
                return new WorkerResponse(WorkerProtocol.Version, true, "Ok", "Worker protocol is ready.", null, null, 0, 0, null, report.PhotosVersion, report.RawExtensionVersion, report);
            if (request.SourcePath is null)
                throw new PhotosRuntimeException("InvalidRequest", "sourcePath is required.");
            if (request.Operation == "inspect")
                return new WorkerResponse(WorkerProtocol.Version, true, "Ok", "Source accepted.", request.SourcePath, null, request.Width, request.Height, "1", report.PhotosVersion, report.RawExtensionVersion, report);
            if (request.TargetPath is null)
                throw new PhotosRuntimeException("InvalidRequest", "targetPath is required.");

            var result = await new PhotosResizeBackend(runtime).ExportAsync(request.SourcePath, request.TargetPath, request.Width, request.Height, request.Quality, CancellationToken.None);
            return new WorkerResponse(WorkerProtocol.Version, true, "Ok", "Exported through Photos ResizeService.", result.SourcePath, result.TargetPath, result.Width, result.Height, result.Orientation, result.PhotosVersion, report.RawExtensionVersion, report);
        }

        private static WorkerResponse Failure(string code, string message) =>
            new(WorkerProtocol.Version, false, code, message, null, null, 0, 0, null, null, null, null);
    }
}
