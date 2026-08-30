using System.Buffers;

namespace RawPreview.Cli.Export;

public sealed record JpegInfo(int PixelWidth, int PixelHeight, string Orientation, long Length, bool IsJpeg);

public static class JpegValidator
{
    public static JpegInfo Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        return Read(stream, stream.Length);
    }

    public static JpegInfo Read(Stream stream, long length)
    {
        if (length < 4 || ReadByte(stream) != 0xff || ReadByte(stream) != 0xd8)
            throw new InvalidDataException("OutputValidationFailed: JPEG SOI marker not found.");

        var width = 0;
        var height = 0;
        var orientation = "1";
        Span<byte> segmentLength = stackalloc byte[2];
        Span<byte> frame = stackalloc byte[5];
        while (stream.Position < length)
        {
            var value = ReadByte(stream);
            while (value != 0xff && stream.Position < length) value = ReadByte(stream);
            if (stream.Position >= length) break;
            do { value = ReadByte(stream); } while (value == 0xff && stream.Position < length);
            if (value < 0) break;
            var marker = (byte)value;
            if (marker is 0xd9 or 0xda) break;
            if (marker is 0xd8 or >= 0xd0 and <= 0xd7 or 0x01) continue;
            ReadExactly(stream, segmentLength);
            var segmentSize = (segmentLength[0] << 8) | segmentLength[1];
            var dataLength = segmentSize - 2;
            if (segmentSize < 2 || stream.Position + dataLength > length)
                throw new InvalidDataException("OutputValidationFailed: truncated JPEG segment.");
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                if (dataLength >= 5)
                {
                    ReadExactly(stream, frame);
                    height = (frame[1] << 8) | frame[2];
                    width = (frame[3] << 8) | frame[4];
                    Skip(stream, dataLength - frame.Length);
                }
                else Skip(stream, dataLength);
            }
            else if (marker == 0xe1)
            {
                var rented = ArrayPool<byte>.Shared.Rent(dataLength);
                try
                {
                    var segment = rented.AsSpan(0, dataLength);
                    ReadExactly(stream, segment);
                    orientation = ReadExifOrientation(segment) ?? orientation;
                }
                finally { ArrayPool<byte>.Shared.Return(rented); }
            }
            else Skip(stream, dataLength);
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("OutputValidationFailed: JPEG dimensions not found.");
        return new JpegInfo(width, height, orientation, length, true);
    }

    private static int ReadByte(Stream stream) => stream.ReadByte();

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read == 0) throw new InvalidDataException("OutputValidationFailed: truncated JPEG segment.");
            buffer = buffer[read..];
        }
    }

    private static void Skip(Stream stream, int count)
    {
        if (count == 0) return;
        if (stream.CanSeek)
        {
            stream.Seek(count, SeekOrigin.Current);
            return;
        }

        Span<byte> buffer = stackalloc byte[8192];
        while (count > 0)
        {
            var read = stream.Read(buffer[..Math.Min(count, buffer.Length)]);
            if (read == 0) throw new InvalidDataException("OutputValidationFailed: truncated JPEG segment.");
            count -= read;
        }
    }

    private static string? ReadExifOrientation(ReadOnlySpan<byte> segment)
    {
        if (segment.Length < 14 || !segment[..6].SequenceEqual("Exif\0\0"u8)) return null;
        var tiff = segment[6..];
        var littleEndian = tiff[0] == (byte)'I';
        if (Read16(tiff[2..4], littleEndian) != 42) return null;
        var ifd = checked((int)Read32(tiff[4..8], littleEndian));
        if (ifd < 0 || ifd + 2 > tiff.Length) return null;
        var count = Read16(tiff[ifd..(ifd + 2)], littleEndian);
        for (var i = 0; i < count; i++)
        {
            var offset = ifd + 2 + i * 12;
            if (offset + 12 > tiff.Length) break;
            if (Read16(tiff[offset..(offset + 2)], littleEndian) != 0x0112) continue;
            var type = Read16(tiff[(offset + 2)..(offset + 4)], littleEndian);
            var itemCount = Read32(tiff[(offset + 4)..(offset + 8)], littleEndian);
            if (type == 3 && itemCount == 1) return Read16(tiff[(offset + 8)..(offset + 10)], littleEndian).ToString();
        }
        return null;
    }

    private static ushort Read16(ReadOnlySpan<byte> bytes, bool little) => little ? (ushort)(bytes[0] | bytes[1] << 8) : (ushort)(bytes[1] | bytes[0] << 8);
    private static uint Read32(ReadOnlySpan<byte> bytes, bool little) => little ? (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24) : (uint)(bytes[3] | bytes[2] << 8 | bytes[1] << 16 | bytes[0] << 24);
}
