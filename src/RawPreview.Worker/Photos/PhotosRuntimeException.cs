namespace RawPreview.Worker.Photos;

public sealed class PhotosRuntimeException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}
