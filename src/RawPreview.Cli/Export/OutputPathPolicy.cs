namespace RawPreview.Cli.Export;

public static class OutputPathPolicy
{
    public static IReadOnlyList<string> EnumerateSources(string inputPath)
    {
        var fullPath = Path.GetFullPath(inputPath);
        if (File.Exists(fullPath))
        {
            if (!string.Equals(Path.GetExtension(fullPath), ".arw", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Input file is not an ARW.");
            return [fullPath];
        }

        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException(fullPath);
        return Directory.EnumerateFiles(fullPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetExtension(path), ".arw", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string GetTargetPath(string sourcePath, string outputDirectory) =>
        Path.Combine(Path.GetFullPath(outputDirectory), Path.GetFileNameWithoutExtension(sourcePath) + ".jpg");

    public static void EnsureNoCollisions(IReadOnlyList<string> sources, string outputDirectory)
    {
        var targets = sources.Select(source => GetTargetPath(source, outputDirectory));
        var duplicate = targets.GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null) throw new IOException($"OutputNameCollision: {duplicate.Key}");
    }
}
