using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace RawPreview.Worker.Photos;

public sealed record ContractProbeResult(
    bool MetadataPresent,
    bool ResizeServicePresent,
    bool SelectTargetFileAsyncPresent,
    bool ResizeAsyncPresent,
    bool MediaStorePresent,
    bool MediaItemPresent,
    bool ResizeParamsPresent,
    bool LensCorrectionPresent,
    IReadOnlyDictionary<string, int> MethodParameterCounts);

public static class PhotosContractProbe
{
    public static ContractProbeResult Probe(string? lightboxMetadataPath)
    {
        if (string.IsNullOrWhiteSpace(lightboxMetadataPath) || !File.Exists(lightboxMetadataPath))
            return new ContractProbeResult(false, false, false, false, false, false, false, false, new Dictionary<string, int>());

        var types = new HashSet<string>(StringComparer.Ordinal);
        var methods = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var stream = File.OpenRead(lightboxMetadataPath);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.TypeDefinitions)
            {
                var definition = metadata.GetTypeDefinition(handle);
                var ns = metadata.GetString(definition.Namespace);
                var name = metadata.GetString(definition.Name);
                var fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
                types.Add(fullName);
                foreach (var methodHandle in definition.GetMethods())
                {
                    var method = metadata.GetMethodDefinition(methodHandle);
                    var methodName = metadata.GetString(method.Name);
                    if (methodName is "ResizeAsync" or "SelectTargetFileAsync")
                        methods[fullName + "." + methodName] = method.GetParameters().Count(parameter => metadata.GetParameter(parameter).SequenceNumber > 0);
                }
            }
        }
        catch (Exception exception)
        {
            throw new PhotosRuntimeException("PhotosMetadataProbeFailed", exception.Message, exception);
        }

        var resizeService = types.Contains("Lightbox.ResizeService") || types.Contains("Lightbox.IResizeService");
        var select = methods.ContainsKey("Lightbox.ResizeService.SelectTargetFileAsync") || methods.ContainsKey("Lightbox.IResizeService.SelectTargetFileAsync");
        var resize = methods.ContainsKey("Lightbox.ResizeService.ResizeAsync") || methods.ContainsKey("Lightbox.IResizeService.ResizeAsync");
        var lens = types.Any(name => name.Contains("Lens", StringComparison.OrdinalIgnoreCase) || name.Contains("Distortion", StringComparison.OrdinalIgnoreCase)) ||
                   methods.Keys.Any(name => name.Contains("Lens", StringComparison.OrdinalIgnoreCase) || name.Contains("Distortion", StringComparison.OrdinalIgnoreCase));
        return new ContractProbeResult(
            true,
            resizeService,
            select,
            resize,
            types.Contains("Lightbox.MediaStore"),
            types.Contains("Lightbox.MediaItem"),
            types.Contains("Lightbox.ResizeParams"),
            lens,
            methods);
    }
}
