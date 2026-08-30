using System.Diagnostics;
using System.Text.Json;
using RawPreview.Cli.Export;
using RawPreview.Cli.Runtime;
using RawPreview.Protocol;

namespace RawPreview.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = CommandLine.Parse(args);
            return command switch
            {
                HelpCommand => PrintHelp(),
                VersionCommand => PrintVersion(),
                DoctorCommand doctor => await RunDoctorAsync(doctor),
                InspectCommand inspect => await RunInspectAsync(inspect),
                ExportCommandOptions export => await RunExportAsync(export),
                SetupRawCommand setup => await RunSetupRawAsync(setup),
                _ => 1
            };
        }
        catch (CommandLineException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine(CommandLine.HelpText);
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 6;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 4;
        }
    }

    private static int PrintHelp() { Console.WriteLine(CommandLine.HelpText); return 0; }
    private static int PrintVersion() { Console.WriteLine("rawpreview 1.0.0"); return 0; }

    private static async Task<int> RunDoctorAsync(DoctorCommand command)
    {
        var response = await new WorkerClient().SendAsync(new WorkerRequest(WorkerProtocol.Version, "doctor", null, null, 95, 0, 0), CancellationToken.None);
        if (command.Json) Console.WriteLine(JsonSerializer.Serialize(response.Runtime));
        else Console.WriteLine(DoctorFormatter.Format(response));
        return response.Ok ? 0 : 3;
    }

    private static async Task<int> RunInspectAsync(InspectCommand command)
    {
        if (!File.Exists(command.SourcePath)) throw new FileNotFoundException(command.SourcePath);
        var metadata = RawMetadataReader.Read(command.SourcePath);
        var response = await new WorkerClient().SendAsync(new WorkerRequest(WorkerProtocol.Version, "inspect", command.SourcePath, null, 95, metadata.Width, metadata.Height), CancellationToken.None);
        var result = new { source = command.SourcePath, width = metadata.Width, height = metadata.Height, orientation = metadata.Orientation, runtime = response.Runtime, ok = response.Ok, code = response.Code, message = response.Message };
        if (command.Json) Console.WriteLine(JsonSerializer.Serialize(result));
        else Console.WriteLine(JsonSerializer.Serialize(result, PrettyJson));
        return response.Ok ? 0 : 3;
    }

    private static async Task<int> RunExportAsync(ExportCommandOptions command)
    {
        var results = await new ExportPipeline(new WorkerClient()).RunAsync(
            new ExportOptions(command.InputPath, command.OutputDirectory, command.Quality, command.Overwrite, command.Json),
            Console.Out, CancellationToken.None);
        var failed = results.Count(result => result.Status == "failed");
        return failed == 0 ? 0 : failed == results.Count ? 4 : 2;
    }

    private static async Task<int> RunSetupRawAsync(SetupRawCommand command)
    {
        if (!command.Install)
        {
            var response = await new WorkerClient().SendAsync(new WorkerRequest(WorkerProtocol.Version, "doctor", null, null, 95, 0, 0), CancellationToken.None);
            Console.WriteLine(DoctorFormatter.Format(response));
            return response.Runtime?.RawExtensionInstalled == true ? 0 : 3;
        }

        if (!OperatingSystem.IsWindows()) return 3;
        var startInfo = new ProcessStartInfo("winget.exe") { UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("install");
        startInfo.ArgumentList.Add("--id");
        startInfo.ArgumentList.Add("9NCTDW2W1BH8");
        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add("msstore");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("winget was not found.");
        await process.WaitForExitAsync();
        Console.WriteLine($"winget exit code: {process.ExitCode}");
        return process.ExitCode;
    }
}

public static class DoctorFormatter
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static string Format(WorkerResponse response) => JsonSerializer.Serialize(response.Runtime, PrettyJson);
}
