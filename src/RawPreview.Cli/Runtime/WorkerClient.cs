using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using RawPreview.Protocol;
using Windows.Management.Deployment;

namespace RawPreview.Cli.Runtime;

public sealed class WorkerClient : IWorkerClient
{
    private readonly string workerPath;
    private readonly bool isDll;
    private readonly string? workerAumid;

    public WorkerClient(string? workerPath = null)
    {
        if (workerPath is null)
        {
            workerAumid = ResolveWorkerAumid();
            if (workerAumid is not null)
            {
                this.workerPath = string.Empty;
                isDll = false;
                return;
            }
        }

        this.workerPath = workerPath ?? ResolveWorkerPath();
        isDll = string.Equals(Path.GetExtension(this.workerPath), ".dll", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<WorkerResponse> SendAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        if (workerAumid is not null) return await SendPackageAsync(request, workerAumid, cancellationToken);

        var startInfo = CreateStartInfo(workerPath, isDll);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new WorkerClientException("WorkerStartFailed", "Worker did not start.");

        await process.StandardInput.WriteLineAsync(WorkerProtocol.Serialize(request));
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        WorkerResponse response;
        try
        {
            var line = await stdoutTask;
            if (line is null) throw new WorkerClientException("WorkerProtocolError", await stderrTask);
            response = WorkerProtocol.ReadResponse(line);
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;
            if (process.ExitCode != 0 && response.Ok)
                throw new WorkerClientException("WorkerExitFailed", stderr);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        return response;
    }

    private static async Task<WorkerResponse> SendPackageAsync(WorkerRequest request, string aumid, CancellationToken cancellationToken)
    {
        var pipeName = $"rawpreview-{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var hr = ActivationHelper.Activate(aumid, $"--package-pipe {pipeName}", out var processId);
        if (hr < 0) throw new WorkerClientException("WorkerStartFailed", $"Package worker activation failed: 0x{(uint)hr:X8}.");

        await server.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(WorkerProtocol.Serialize(request));
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null) throw new WorkerClientException("WorkerProtocolError", "Package worker closed the pipe without a response.");
        var response = WorkerProtocol.ReadResponse(line);

        if (processId != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (ArgumentException)
            {
            }
        }
        return response;
    }

    internal static ProcessStartInfo CreateStartInfo(string workerPath, bool isDll)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = isDll ? Environment.ProcessPath ?? "dotnet" : workerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (isDll) startInfo.ArgumentList.Add(workerPath);
        startInfo.ArgumentList.Add("--jsonl");
        return startInfo;
    }

    private static string ResolveWorkerPath()
    {
        var configured = Environment.GetEnvironmentVariable("RAWPREVIEW_WORKER");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "RawPreview.Worker.exe"),
            Path.Combine(AppContext.BaseDirectory, "worker", "RawPreview.Worker.exe"),
            Path.Combine(AppContext.BaseDirectory, "RawPreview.Worker.dll"),
            Path.Combine(AppContext.BaseDirectory, "worker", "RawPreview.Worker.dll"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RawPreview.Worker", "bin", "Debug", "net9.0-windows10.0.22621.0", "RawPreview.Worker.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RawPreview.Worker", "bin", "Debug", "net9.0-windows10.0.22621.0", "RawPreview.Worker.dll"))
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new WorkerClientException("WorkerMissing", "RawPreview.Worker.exe was not found. Set RAWPREVIEW_WORKER to its path.");
    }

    private static string? ResolveWorkerAumid()
    {
        var configured = Environment.GetEnvironmentVariable("RAWPREVIEW_WORKER_AUMID");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        try
        {
            var package = new PackageManager().FindPackagesForUser(string.Empty)
                .FirstOrDefault(value => string.Equals(value.Id.Name, "RawPreview.Worker", StringComparison.OrdinalIgnoreCase));
            return package is null ? null : $"{package.Id.FamilyName}!Worker";
        }
        catch
        {
            return null;
        }
    }

    private static class ActivationHelper
    {
        public static int Activate(string appUserModelId, string arguments, out uint processId)
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            return manager.ActivateApplication(appUserModelId, arguments, 0, out processId);
        }

        [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            int ActivateApplication(string appUserModelId, string arguments, uint options, out uint processId);
        }

        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ApplicationActivationManager
        {
        }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }
}
