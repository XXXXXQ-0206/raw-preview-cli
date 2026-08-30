using System.Text.Json;

namespace RawPreview.Protocol;

public sealed record WorkerRequest(
    int ProtocolVersion,
    string Operation,
    string? SourcePath,
    string? TargetPath,
    int Quality,
    int Width,
    int Height);

public sealed record WorkerResponse(
    int ProtocolVersion,
    bool Ok,
    string Code,
    string Message,
    string? SourcePath,
    string? TargetPath,
    int Width,
    int Height,
    string? Orientation,
    string? PhotosPackageVersion,
    string? RawExtensionVersion,
    RuntimeReportDto? Runtime);

public sealed record RuntimeReportDto(
    bool PhotosInstalled,
    bool RawExtensionInstalled,
    string? PhotosVersion,
    string? RawExtensionVersion,
    string? Architecture,
    string? PhotosInstallLocation,
    string? RawExtensionInstallLocation,
    string? PhotosExecutable,
    string? LightboxMetadataPath,
    string? RawDecoderPath,
    string ArwSubtype,
    bool PhotosModelsMetadataPresent,
    bool PhotosServiceMetadataPresent,
    bool ResizeServicePresent,
    bool SelectTargetFileAsyncPresent,
    bool ResizeAsyncPresent,
    bool LensCorrectionPresent,
    string[] MissingCapabilities);

public static class WorkerProtocol
{
    public const int Version = 1;

    public static string Serialize(WorkerRequest value) => JsonSerializer.Serialize(value);
    public static string Serialize(WorkerResponse value) => JsonSerializer.Serialize(value);

    public static WorkerRequest ReadRequest(string line)
    {
        var value = JsonSerializer.Deserialize<WorkerRequest>(line)
            ?? throw new InvalidDataException("Worker request is empty.");
        ValidateRequest(value);
        return value;
    }

    public static WorkerResponse ReadResponse(string line) =>
        JsonSerializer.Deserialize<WorkerResponse>(line)
        ?? throw new InvalidDataException("Worker response is empty.");

    private static void ValidateRequest(WorkerRequest value)
    {
        if (value.ProtocolVersion != Version)
            throw new InvalidDataException("ProtocolVersionMismatch");
        if (value.Operation is not ("doctor" or "inspect" or "export" or "self-test"))
            throw new InvalidDataException("InvalidRequest: unknown operation");
        if (value.Quality is < 1 or > 100)
            throw new InvalidDataException("InvalidRequest: quality must be 1..100");
        if (value.SourcePath is not null && !Path.IsPathFullyQualified(value.SourcePath))
            throw new InvalidDataException("InvalidRequest: source path must be absolute");
        if (value.TargetPath is not null && !Path.IsPathFullyQualified(value.TargetPath))
            throw new InvalidDataException("InvalidRequest: target path must be absolute");
        if (value.TargetPath is not null && !string.Equals(Path.GetExtension(value.TargetPath), ".jpg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("InvalidRequest: target path must end in .jpg");
    }
}
