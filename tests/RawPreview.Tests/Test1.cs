using System.Buffers.Binary;
using System.Diagnostics;
using RawPreview.Cli;
using RawPreview.Cli.Export;
using RawPreview.Cli.Runtime;
using RawPreview.Protocol;

namespace RawPreview.Tests;

[TestClass]
public sealed class ExportFormatTests
{
    [TestMethod]
    public void PortraitOrientationUsesDisplayDimensions()
    {
        var metadata = new RawMetadata(6000, 4000, 8);

        Assert.AreEqual(4000, metadata.DisplayWidth);
        Assert.AreEqual(6000, metadata.DisplayHeight);
    }

    [TestMethod]
    public void RawMetadataReaderReadsDimensionsAndOrientationFromLittleEndianTiff()
    {
        var path = WriteTempFile(BuildTiff(littleEndian: true, orientation: 8));
        try
        {
            var metadata = RawMetadataReader.Read(path);

            Assert.AreEqual(6000, metadata.Width);
            Assert.AreEqual(4000, metadata.Height);
            Assert.AreEqual(8, metadata.Orientation);
            Assert.AreEqual(4000, metadata.DisplayWidth);
            Assert.AreEqual(6000, metadata.DisplayHeight);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void RawMetadataReaderReadsBigEndianTiff()
    {
        var path = WriteTempFile(BuildTiff(littleEndian: false, orientation: 1));
        try
        {
            var metadata = RawMetadataReader.Read(path);

            Assert.AreEqual(6000, metadata.Width);
            Assert.AreEqual(4000, metadata.Height);
            Assert.AreEqual(1, metadata.Orientation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void RawMetadataReaderRejectsUnreasonableSubIfdOffsetCountBeforeAllocation()
    {
        var path = WriteTempFile(BuildTiffWithSubIfdOffsetCount(4097));
        try
        {
            var exception = Assert.ThrowsException<InvalidDataException>(() => RawMetadataReader.Read(path));

            StringAssert.Contains(exception.Message, "too many SubIFD offsets");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void JpegValidatorReadsDimensionsAndExifOrientation()
    {
        var path = WriteTempFile(BuildJpeg(4000, 6000, 8));
        try
        {
            var jpeg = JpegValidator.Read(path);

            Assert.IsTrue(jpeg.IsJpeg);
            Assert.AreEqual(4000, jpeg.PixelWidth);
            Assert.AreEqual(6000, jpeg.PixelHeight);
            Assert.AreEqual("8", jpeg.Orientation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void JpegValidatorRejectsTruncatedJpeg()
    {
        var path = WriteTempFile([0xff, 0xd8, 0xff, 0xe1, 0x00, 0x08, 0x45]);
        try
        {
            Assert.ThrowsException<InvalidDataException>(() => JpegValidator.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void JpegValidatorStopsReadingAfterTheJpegHeader()
    {
        var header = BuildJpeg(4000, 6000, 8);
        var bytes = new byte[4 * 1024 * 1024];
        header.CopyTo(bytes, 0);
        using var stream = new CountingReadStream(bytes);

        var jpeg = JpegValidator.Read(stream, bytes.LongLength);

        Assert.AreEqual(4000, jpeg.PixelWidth);
        Assert.AreEqual(6000, jpeg.PixelHeight);
        Assert.IsTrue(stream.BytesRead < 1024, $"Read {stream.BytesRead} bytes from a {bytes.Length}-byte JPEG fixture.");
    }

    [TestMethod]
    public void JpegValidatorAvoidsFileSizedManagedAllocation()
    {
        var path = WriteTempFile(BuildLargeJpeg(16 * 1024 * 1024));
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var legacyAllocated = MeasureAllocatedBytes(() =>
            {
                var bytes = File.ReadAllBytes(path);
                _ = JpegValidator.Read(new MemoryStream(bytes, writable: false), bytes.LongLength);
            });

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var optimizedAllocated = MeasureAllocatedBytes(() => _ = JpegValidator.Read(path));

            Console.WriteLine($"JPEG validator benchmark: legacy={legacyAllocated} bytes, optimized={optimizedAllocated} bytes");
            Assert.IsTrue(optimizedAllocated < legacyAllocated / 4,
                $"Optimized validation allocated {optimizedAllocated} bytes versus legacy {legacyAllocated} bytes.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CommandLineReportsInvalidQualityValue()
    {
        var exception = Assert.ThrowsException<CommandLineException>(() =>
            RawPreview.Cli.CommandLine.Parse(["export", "photo.arw", "--quality", "high"]));

        Assert.AreEqual("quality must be an integer.", exception.Message);
    }

    [TestMethod]
    public void SharedProtocolRejectsRelativePaths()
    {
        var request = new RawPreview.Protocol.WorkerRequest(1, "export", "relative.arw", "relative.jpg", 95, 6000, 4000);

        var exception = Assert.ThrowsException<InvalidDataException>(() => RawPreview.Protocol.WorkerProtocol.ReadRequest(RawPreview.Protocol.WorkerProtocol.Serialize(request)));

        StringAssert.Contains(exception.Message, "absolute");
    }

    [TestMethod]
    public void OutputPathPolicyDetectsCaseInsensitiveStemCollisions()
    {
        Assert.ThrowsException<IOException>(() => OutputPathPolicy.EnsureNoCollisions(
            [Path.Combine(Path.GetTempPath(), "A.ARW"), Path.Combine(Path.GetTempPath(), "a.arw")],
            Path.Combine(Path.GetTempPath(), "jpg")));
    }

    [TestMethod]
    public async Task ExportPipelineContinuesAfterMalformedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rawpreview-pipeline-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "jpg");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "valid.arw"), BuildTiff(littleEndian: true, orientation: 8));
        File.WriteAllBytes(Path.Combine(root, "broken.arw"), [1, 2, 3]);
        try
        {
            var results = await new ExportPipeline(new FakeWorker()).RunAsync(
                new ExportOptions(root, output, 95, false, true), TextWriter.Null, CancellationToken.None);

            Assert.AreEqual(2, results.Count);
            Assert.AreEqual("exported", results.Single(result => result.SourcePath.EndsWith("valid.arw")).Status);
            var failed = results.Single(result => result.SourcePath.EndsWith("broken.arw"));
            Assert.AreEqual("failed", failed.Status);
            Assert.AreEqual("InputMetadataFailed", failed.Code);
            Assert.IsTrue(File.Exists(Path.Combine(output, "valid.jpg")));
            Assert.AreEqual(0, Directory.EnumerateFiles(output, "*.partial").Count());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ExportPipelineDoesNotOverwriteTargetCreatedDuringExport()
    {
        var root = Path.Combine(Path.GetTempPath(), $"rawpreview-pipeline-{Guid.NewGuid():N}");
        var output = Path.Combine(root, "jpg");
        var target = Path.Combine(output, "valid.jpg");
        var original = new byte[] { 1, 2, 3 };
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(root, "valid.arw"), BuildTiff(littleEndian: true, orientation: 8));
        File.WriteAllBytes(target, original);
        try
        {
            var results = await new ExportPipeline(new FakeWorker()).RunAsync(
                new ExportOptions(root, output, 95, false, true), TextWriter.Null, CancellationToken.None);

            Assert.AreEqual(1, results.Count);
            var result = results[0];
            Assert.AreEqual("failed", result.Status);
            Assert.AreEqual("OutputPublishFailed", result.Code);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
            Assert.AreEqual(0, Directory.EnumerateFiles(output, "*.partial").Count());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string WriteTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"rawpreview-test-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] BuildTiff(bool littleEndian, ushort orientation)
    {
        var bytes = new byte[50];
        bytes[0] = bytes[1] = littleEndian ? (byte)'I' : (byte)'M';
        Write16(bytes, 2, 42, littleEndian);
        Write32(bytes, 4, 8, littleEndian);
        Write16(bytes, 8, 3, littleEndian);
        WriteEntry(bytes, 10, 0x0100, 4, 6000, littleEndian);
        WriteEntry(bytes, 22, 0x0101, 4, 4000, littleEndian);
        WriteEntry(bytes, 34, 0x0112, 3, orientation, littleEndian);
        Write32(bytes, 46, 0, littleEndian);
        return bytes;
    }

    private static byte[] BuildTiffWithSubIfdOffsetCount(uint count)
    {
        var bytes = new byte[26];
        bytes[0] = bytes[1] = (byte)'I';
        Write16(bytes, 2, 42, littleEndian: true);
        Write32(bytes, 4, 8, littleEndian: true);
        Write16(bytes, 8, 1, littleEndian: true);
        Write16(bytes, 10, 0x014A, littleEndian: true);
        Write16(bytes, 12, 4, littleEndian: true);
        Write32(bytes, 14, count, littleEndian: true);
        Write32(bytes, 18, 26, littleEndian: true);
        return bytes;
    }

    private static byte[] BuildJpeg(int width, int height, ushort orientation)
    {
        var exif = new byte[32];
        "Exif\0\0"u8.CopyTo(exif);
        exif[6] = exif[7] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(exif.AsSpan(8, 2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(exif.AsSpan(10, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(exif.AsSpan(14, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(exif.AsSpan(16, 2), 0x0112);
        BinaryPrimitives.WriteUInt16LittleEndian(exif.AsSpan(18, 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(exif.AsSpan(20, 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(exif.AsSpan(24, 2), orientation);

        var jpeg = new List<byte>(64) { 0xff, 0xd8, 0xff, 0xe1, 0x00, 0x22 };
        jpeg.AddRange(exif);
        jpeg.AddRange([0xff, 0xc0, 0x00, 0x11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width]);
        jpeg.AddRange(new byte[10]);
        jpeg.AddRange([0xff, 0xd9]);
        return jpeg.ToArray();
    }

    private static byte[] BuildLargeJpeg(int length)
    {
        var jpeg = BuildJpeg(4000, 6000, 8);
        var bytes = new byte[length];
        jpeg.CopyTo(bytes, 0);
        return bytes;
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void WriteEntry(byte[] bytes, int offset, ushort tag, ushort type, uint value, bool littleEndian)
    {
        Write16(bytes, offset, tag, littleEndian);
        Write16(bytes, offset + 2, type, littleEndian);
        Write32(bytes, offset + 4, 1, littleEndian);
        if (type == 3)
        {
            Write16(bytes, offset + 8, checked((ushort)value), littleEndian);
            Write16(bytes, offset + 10, 0, littleEndian);
        }
        else Write32(bytes, offset + 8, value, littleEndian);
    }

    private static void Write16(byte[] bytes, int offset, ushort value, bool littleEndian)
    {
        if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);
        else BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset, 2), value);
    }

    private static void Write32(byte[] bytes, int offset, uint value, bool littleEndian)
    {
        if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset, 4), value);
    }

    private sealed class CountingReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            BytesRead += read;
            return read;
        }
    }

    private sealed class FakeWorker : IWorkerClient
    {
        public Task<WorkerResponse> SendAsync(WorkerRequest request, CancellationToken cancellationToken)
        {
            File.WriteAllBytes(request.TargetPath!, BuildJpeg(4000, 6000, 8));
            return Task.FromResult(new WorkerResponse(1, true, "Ok", "", request.SourcePath, request.TargetPath, 6000, 4000, "8", null, null, null));
        }
    }
}
