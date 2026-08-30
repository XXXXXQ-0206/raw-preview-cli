namespace RawPreview.Cli.Runtime;

public sealed class WorkerClientException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
