namespace RawPreview.Cli.Export;

public sealed record RawMetadata(int Width, int Height, int Orientation)
{
    public int DisplayWidth => Orientation is >= 5 and <= 8 ? Height : Width;
    public int DisplayHeight => Orientation is >= 5 and <= 8 ? Width : Height;
}

public static class RawMetadataReader
{
    private const uint MaximumSubIfdOffsets = 4096;
    private const ushort ImageWidth = 0x0100;
    private const ushort ImageHeight = 0x0101;
    private const ushort OrientationTag = 0x0112;
    private const ushort ExifIfdPointer = 0x8769;
    private const ushort SubIfdPointer = 0x014A;
    private const ushort PixelXDimension = 0xA002;
    private const ushort PixelYDimension = 0xA003;

    public static RawMetadata Read(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        ReadExactly(stream, header);
        var littleEndian = header[0] == (byte)'I' && header[1] == (byte)'I';
        if ((!littleEndian && !(header[0] == (byte)'M' && header[1] == (byte)'M')) || ReadUInt16(header[2..4], littleEndian) != 42)
            throw new InvalidDataException("ARW TIFF header not found.");

        var pending = new Queue<uint>();
        var visited = new HashSet<uint>();
        pending.Enqueue(ReadUInt32(header[4..8], littleEndian));
        var width = 0;
        var height = 0;
        var orientation = 1;

        while (pending.Count > 0)
        {
            var ifdOffset = pending.Dequeue();
            if (ifdOffset == 0 || !visited.Add(ifdOffset)) continue;
            var entries = ReadIfd(stream, ifdOffset, littleEndian);
            foreach (var entry in entries.Entries)
            {
                var value = ReadScalar(entry, littleEndian);
                if (value is not null)
                {
                    if (entry.Tag is ImageWidth or PixelXDimension && value > 0) width = checked((int)value.Value);
                    else if (entry.Tag is ImageHeight or PixelYDimension && value > 0) height = checked((int)value.Value);
                    else if (entry.Tag == OrientationTag && value is >= 1 and <= 8) orientation = checked((int)value.Value);
                }

                if (entry.Tag == ExifIfdPointer && entry.Type == 4 && entry.Count == 1 && value is not null)
                    pending.Enqueue(value.Value);
                else if (entry.Tag == SubIfdPointer && entry.Type == 4)
                    foreach (var offset in ReadOffsets(stream, entry, littleEndian)) pending.Enqueue(offset);
            }
            if (entries.NextIfd != 0) pending.Enqueue(entries.NextIfd);
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("ARW dimensions not found.");
        return new RawMetadata(width, height, orientation);
    }

    private static IfdData ReadIfd(FileStream stream, uint offset, bool littleEndian)
    {
        EnsureRange(stream, offset, 2);
        stream.Position = offset;
        Span<byte> countBytes = stackalloc byte[2];
        ReadExactly(stream, countBytes);
        var count = ReadUInt16(countBytes, littleEndian);
        if (count > 4096) throw new InvalidDataException("ARW TIFF directory is unreasonable.");

        var entries = new List<TiffEntry>(count);
        Span<byte> raw = stackalloc byte[12];
        for (var i = 0; i < count; i++)
        {
            ReadExactly(stream, raw);
            entries.Add(new TiffEntry(
                ReadUInt16(raw[..2], littleEndian),
                ReadUInt16(raw[2..4], littleEndian),
                ReadUInt32(raw[4..8], littleEndian),
                raw.ToArray()));
        }

        Span<byte> nextBytes = stackalloc byte[4];
        ReadExactly(stream, nextBytes);
        return new IfdData(entries, ReadUInt32(nextBytes, littleEndian));
    }

    private static uint? ReadScalar(TiffEntry entry, bool littleEndian)
    {
        if (entry.Count != 1 || entry.Type is not (3 or 4)) return null;
        return entry.Type == 3
            ? ReadUInt16(entry.Raw.AsSpan(8, 2), littleEndian)
            : ReadUInt32(entry.Raw.AsSpan(8, 4), littleEndian);
    }

    private static IEnumerable<uint> ReadOffsets(FileStream stream, TiffEntry entry, bool littleEndian)
    {
        if (entry.Count == 0) yield break;
        if (entry.Count > MaximumSubIfdOffsets)
            throw new InvalidDataException("ARW TIFF has too many SubIFD offsets.");

        var size = checked((long)entry.Count * TypeSize(entry.Type));
        if (size > int.MaxValue) throw new InvalidDataException("ARW TIFF offset array is unreasonable.");
        if (size > 4) EnsureRange(stream, entry.ValueOffset(littleEndian), size);

        var bytes = new byte[checked((int)size)];
        if (size <= 4)
        {
            Array.Copy(entry.Raw, 8, bytes, 0, (int)size);
        }
        else
        {
            stream.Position = entry.ValueOffset(littleEndian);
            ReadExactly(stream, bytes);
        }

        for (var offset = 0; offset + 4 <= bytes.Length; offset += 4)
            yield return ReadUInt32(bytes.AsSpan(offset, 4), littleEndian);
    }

    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => throw new InvalidDataException($"Unsupported TIFF field type: {type}.")
    };

    private static void EnsureRange(FileStream stream, long offset, long length)
    {
        if (offset < 0 || length < 0 || offset > stream.Length - length)
            throw new InvalidDataException("ARW TIFF directory points outside the file.");
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian) => littleEndian
        ? (ushort)(bytes[0] | bytes[1] << 8)
        : (ushort)(bytes[1] | bytes[0] << 8);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian) => littleEndian
        ? (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24)
        : (uint)(bytes[3] | bytes[2] << 8 | bytes[1] << 16 | bytes[0] << 24);

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read == 0) throw new EndOfStreamException();
            buffer = buffer[read..];
        }
    }

    private static void ReadExactly(Stream stream, byte[] buffer) => ReadExactly(stream, buffer.AsSpan());

    private sealed record TiffEntry(ushort Tag, ushort Type, uint Count, byte[] Raw)
    {
        public uint ValueOffset(bool littleEndian) => ReadUInt32(Raw.AsSpan(8, 4), littleEndian);
    }

    private sealed record IfdData(IReadOnlyList<TiffEntry> Entries, uint NextIfd);
}
