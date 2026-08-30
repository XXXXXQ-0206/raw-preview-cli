namespace RawPreview.Cli.Export;

public sealed record ExportOptions(
    string InputPath,
    string OutputDirectory,
    int Quality,
    bool Overwrite,
    bool Json);

public sealed record ExportItemResult(
    string SourcePath,
    string TargetPath,
    string Status,
    string Code,
    int Width,
    int Height,
    string Orientation,
    long Bytes,
    string Message);
