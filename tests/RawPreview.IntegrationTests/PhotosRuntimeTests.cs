using RawPreview.Cli.Export;
using RawPreview.Worker.Photos;

namespace RawPreview.IntegrationTests;

[TestClass]
public sealed class PhotosRuntimeTests
{
    [TestMethod]
    public void RuntimeProbeReturnsStableCapabilityReport()
    {
        var runtime = PhotosRuntimeLocator.Discover();

        Assert.IsNotNull(runtime.Report);
        Assert.IsNotNull(runtime.Report.MissingCapabilities);
        foreach (var path in new[]
        {
            runtime.Report.PhotosInstallLocation,
            runtime.Report.RawExtensionInstallLocation,
            runtime.Report.PhotosExecutable,
            runtime.Report.LightboxMetadataPath,
            runtime.Report.RawDecoderPath
        }.Where(path => path is not null))
        {
            Assert.IsTrue(Path.IsPathFullyQualified(path!));
            Assert.IsTrue(File.Exists(path!) || Directory.Exists(path!));
        }
    }

    [TestMethod]
    public async Task PhotosExportPreservesConfiguredPortraitMetadataWhenFixtureIsAvailable()
    {
        var runtime = PhotosRuntimeLocator.Discover();
        if (runtime.Report.MissingCapabilities.Length > 0)
            Assert.Inconclusive(string.Join(",", runtime.Report.MissingCapabilities));

        var source = Environment.GetEnvironmentVariable("RAWPREVIEW_TEST_ARW");
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            Assert.Inconclusive("Set RAWPREVIEW_TEST_ARW to run the Photos integration export.");

        var metadata = RawMetadataReader.Read(source);
        var target = Path.Combine(Path.GetTempPath(), $"rawpreview-integration-{Guid.NewGuid():N}.jpg");
        try
        {
            var result = await new PhotosResizeBackend(runtime).ExportAsync(
                source, target, metadata.Width, metadata.Height, 95, CancellationToken.None);
            var jpeg = JpegValidator.Read(target);

            Assert.AreEqual(metadata.DisplayWidth, jpeg.PixelWidth);
            Assert.AreEqual(metadata.DisplayHeight, jpeg.PixelHeight);
            Assert.AreEqual(metadata.Orientation.ToString(), jpeg.Orientation);
            Assert.AreEqual(source, result.SourcePath);
        }
        finally
        {
            if (File.Exists(target)) File.Delete(target);
        }
    }
}
