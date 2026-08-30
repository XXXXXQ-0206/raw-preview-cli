namespace RawPreview.Cli;

public abstract record Command;
public sealed record HelpCommand : Command;
public sealed record VersionCommand : Command;
public sealed record DoctorCommand(bool Json) : Command;
public sealed record InspectCommand(string SourcePath, bool Json) : Command;
public sealed record ExportCommandOptions(
    string InputPath,
    string OutputDirectory,
    int Quality,
    bool Overwrite,
    bool Json) : Command;
public sealed record SetupRawCommand(bool Install) : Command;

public static class CommandLine
{
    public static Command Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            return new HelpCommand();
        }

        if (args[0] is "--version" or "-v")
        {
            EnsureEnd(args, 1);
            return new VersionCommand();
        }

        return args[0] switch
        {
            "doctor" => ParseDoctor(args),
            "inspect" => ParseInspect(args),
            "export" => ParseExport(args),
            "setup-raw" => ParseSetupRaw(args),
            _ => throw new CommandLineException($"Unknown command: {args[0]}")
        };
    }

    public static string HelpText => """
rawpreview doctor [--json]
rawpreview inspect SOURCE.ARW [--json]
rawpreview export SOURCE_OR_DIRECTORY [--output OUTPUT_DIRECTORY] [--quality 95] [--overwrite] [--json]
rawpreview setup-raw [--install]
rawpreview --version
""";

    private static DoctorCommand ParseDoctor(string[] args)
    {
        var json = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json") json = true;
            else throw new CommandLineException($"Unknown option: {args[i]}");
        }
        return new DoctorCommand(json);
    }

    private static InspectCommand ParseInspect(string[] args)
    {
        string? source = null;
        var json = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--json") json = true;
            else if (source is null && !args[i].StartsWith("--", StringComparison.Ordinal)) source = args[i];
            else throw new CommandLineException($"Unknown option or extra source: {args[i]}");
        }
        if (source is null) throw new CommandLineException("inspect requires an ARW source.");
        return new InspectCommand(Path.GetFullPath(source), json);
    }

    private static ExportCommandOptions ParseExport(string[] args)
    {
        string? input = null;
        string? output = null;
        var quality = 95;
        var overwrite = false;
        var json = false;

        for (var i = 1; i < args.Length; i++)
        {
            var argument = args[i];
            if (argument == "--output") output = RequireValue(args, ref i, argument);
            else if (argument == "--quality")
            {
                if (!int.TryParse(RequireValue(args, ref i, argument), out var parsed))
                    throw new CommandLineException("quality must be an integer.");
                quality = parsed;
            }
            else if (argument == "--overwrite") overwrite = true;
            else if (argument == "--json") json = true;
            else if (input is null && !argument.StartsWith("--", StringComparison.Ordinal)) input = argument;
            else throw new CommandLineException($"Unknown option or extra source: {argument}");
        }

        if (input is null) throw new CommandLineException("export requires a source file or directory.");
        if (quality is < 1 or > 100) throw new CommandLineException("quality must be 1..100.");
        var inputPath = Path.GetFullPath(input);
        var outputPath = output is not null
            ? Path.GetFullPath(output)
            : Path.Combine(File.Exists(inputPath) ? Path.GetDirectoryName(inputPath)! : inputPath, "JPG");
        return new ExportCommandOptions(inputPath, outputPath, quality, overwrite, json);
    }

    private static SetupRawCommand ParseSetupRaw(string[] args)
    {
        var install = false;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--install") install = true;
            else throw new CommandLineException($"Unknown option: {args[i]}");
        }
        return new SetupRawCommand(install);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new CommandLineException($"{option} requires a value.");
        }
        return args[index];
    }

    private static void EnsureEnd(string[] args, int expectedLength)
    {
        if (args.Length != expectedLength) throw new CommandLineException("Unexpected arguments.");
    }
}

public sealed class CommandLineException(string message) : Exception(message);
