using System.Text.Json;
using RawPreview.Cli.Runtime;
using RawPreview.Protocol;

namespace RawPreview.Cli.Export;

public sealed class ExportPipeline(IWorkerClient workerClient)
{
    public async Task<IReadOnlyList<ExportItemResult>> RunAsync(ExportOptions options, TextWriter progress, CancellationToken cancellationToken)
    {
        var sources = OutputPathPolicy.EnumerateSources(options.InputPath);
        OutputPathPolicy.EnsureNoCollisions(sources, options.OutputDirectory);
        Directory.CreateDirectory(options.OutputDirectory);
        var results = new List<ExportItemResult>(sources.Count);

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = OutputPathPolicy.GetTargetPath(source, options.OutputDirectory);
            if (!options.Overwrite && File.Exists(target))
            {
                try
                {
                    var info = JpegValidator.Read(target);
                    results.Add(new ExportItemResult(source, target, "skipped", "SkippedExisting", info.PixelWidth, info.PixelHeight, info.Orientation, info.Length, "Existing valid JPEG."));
                    continue;
                }
                catch (InvalidDataException) { }
            }

            RawMetadata metadata;
            try
            {
                metadata = RawMetadataReader.Read(source);
            }
            catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
            {
                results.Add(new ExportItemResult(source, target, "failed", "InputMetadataFailed", 0, 0, "1", 0, exception.Message));
                continue;
            }

            var temp = target + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                var response = await workerClient.SendAsync(new WorkerRequest(
                    WorkerProtocol.Version, "export", source, temp, options.Quality,
                    metadata.Width, metadata.Height), cancellationToken);
                if (!response.Ok)
                {
                    results.Add(new ExportItemResult(source, target, "failed", response.Code, 0, 0, response.Orientation ?? "1", 0, response.Message));
                    continue;
                }

                var info = JpegValidator.Read(temp);
                if (info.PixelWidth != metadata.DisplayWidth || info.PixelHeight != metadata.DisplayHeight)
                    throw new InvalidDataException($"OutputValidationFailed: expected {metadata.DisplayWidth}x{metadata.DisplayHeight}, got {info.PixelWidth}x{info.PixelHeight}.");
                if (!string.Equals(info.Orientation, metadata.Orientation.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    throw new InvalidDataException($"OutputValidationFailed: expected Orientation={metadata.Orientation}, got Orientation={info.Orientation}.");
                File.Move(temp, target, overwrite: options.Overwrite);
                results.Add(new ExportItemResult(source, target, "exported", "Ok", info.PixelWidth, info.PixelHeight, info.Orientation, info.Length, "Exported through Photos ResizeService."));
            }
            catch (WorkerClientException exception)
            {
                results.Add(new ExportItemResult(source, target, "failed", exception.Code, 0, 0, "1", 0, exception.Message));
            }
            catch (InvalidDataException exception)
            {
                results.Add(new ExportItemResult(source, target, "failed", "OutputValidationFailed", 0, 0, "1", 0, exception.Message));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                results.Add(new ExportItemResult(source, target, "failed", "OutputPublishFailed", 0, 0, "1", 0, exception.Message));
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        foreach (var result in results)
        {
            await progress.WriteLineAsync(JsonSerializer.Serialize(result));
        }
        return results;
    }
}
