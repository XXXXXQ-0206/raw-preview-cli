namespace RawPreview.Worker.Photos;

public sealed record PhotosExportResult(string SourcePath, string TargetPath, int Width, int Height, string Orientation, string PhotosVersion);

public sealed class PhotosResizeBackend(PhotosRuntime runtime)
{
    public async Task<PhotosExportResult> ExportAsync(string sourcePath, string targetPath, int width, int height, int quality, CancellationToken cancellationToken)
    {
        if (runtime.Report.MissingCapabilities.Length > 0)
            throw new PhotosRuntimeException(runtime.Report.MissingCapabilities[0], string.Join(", ", runtime.Report.MissingCapabilities));
        if (runtime.LightboxDllPath is null)
            throw new PhotosRuntimeException("PhotosContextInitializationFailed", "Lightbox.dll was not found in the installed Photos package.");
        await PhotosWinRtInvoker.ExportAsync(sourcePath, targetPath, checked((uint)width), checked((uint)height), quality, runtime.LightboxDllPath, cancellationToken);
        return new PhotosExportResult(sourcePath, targetPath, width, height, "1", runtime.Report.PhotosVersion ?? "unknown");
    }
}
