using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using RawPreview.Protocol;

namespace RawPreview.Worker.Photos;

public sealed record PhotosRuntime(
    RuntimeReportDto Report,
    ContractProbeResult Contract,
    string? LightboxDllPath,
    string? PhotosModelsDllPath,
    string? PhotosServiceSdkDllPath);

public static class PhotosRuntimeLocator
{
    public const string PhotosPackageName = "Microsoft.Windows.Photos";
    public const string RawPackageName = "Microsoft.RawImageExtension";
    public const string ArwSubtype = "41945702-8302-44A6-9445-AC98E8AFA086";

    public static PhotosRuntime Discover()
    {
        var packages = FindPackages();
        var photos = SelectPackage(packages, PhotosPackageName);
        var raw = SelectPackage(packages, RawPackageName);
        var processArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
        var photoPath = photos?.InstalledLocation.Path;
        var rawPath = raw?.InstalledLocation.Path;
        var lightboxWinmd = Existing(photoPath, "Lightbox.winmd");
        var lightboxDll = Existing(photoPath, "Lightbox.dll");
        var photosModelsWinmd = Existing(photoPath, "Photos.Models.winmd");
        var photosServiceWinmd = Existing(photoPath, "PhotosServiceSdk.winmd");
        var photosServiceDll = Existing(photoPath, "PhotosServiceSdk.dll");
        var photosModelsDll = Existing(photoPath, "Photos.Models.dll");
        var photosExe = Existing(photoPath, "Photos.exe");
        var decoder = FindDecoder(rawPath, processArchitecture);
        var contract = PhotosContractProbe.Probe(lightboxWinmd);
        var missing = new List<string>();
        if (photos is null) missing.Add("PhotosPackageMissing");
        if (raw is null) missing.Add("RawImageExtensionMissing");
        if (decoder is null) missing.Add("ArwDecoderUnavailable");
        if (!contract.MetadataPresent || !contract.ResizeServicePresent || !contract.SelectTargetFileAsyncPresent || !contract.ResizeAsyncPresent || !contract.MediaStorePresent || !contract.MediaItemPresent || !contract.ResizeParamsPresent)
            missing.Add("PhotosContractMissing");

        var report = new RuntimeReportDto(
            photos is not null,
            raw is not null,
            FormatVersion(photos),
            FormatVersion(raw),
            processArchitecture,
            photoPath,
            rawPath,
            photosExe,
            lightboxWinmd,
            decoder,
            ArwSubtype,
            photosModelsWinmd is not null,
            photosServiceWinmd is not null,
            contract.ResizeServicePresent,
            contract.SelectTargetFileAsyncPresent,
            contract.ResizeAsyncPresent,
            contract.LensCorrectionPresent,
            missing.Distinct(StringComparer.Ordinal).ToArray());
        return new PhotosRuntime(report, contract, lightboxDll, photosModelsDll, photosServiceDll);
    }

    public static string? FindPackageFamilyName(string packageName) =>
        SelectPackage(FindPackages(), packageName)?.Id.FamilyName;

    public static string? FindPackageFile(string packageName, string fileName)
    {
        var package = SelectPackage(FindPackages(), packageName);
        return Existing(package?.InstalledLocation.Path, fileName);
    }

    private static Package[] FindPackages()
    {
        try
        {
            return new PackageManager().FindPackagesForUser("").ToArray();
        }
        catch
        {
            try { return [Package.Current]; } catch { return []; }
        }
    }

    private static string? FormatVersion(Package? package)
    {
        if (package is null) return null;
        var version = package.Id.Version;
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    private static Package? SelectPackage(IReadOnlyList<Package> packages, string name) =>
        packages.Where(package => string.Equals(package.Id.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(package => package.Id.Version.Major)
            .ThenByDescending(package => package.Id.Version.Minor)
            .ThenByDescending(package => package.Id.Version.Build)
            .ThenByDescending(package => package.Id.Version.Revision)
            .FirstOrDefault();

    private static string? Existing(string? root, string name)
    {
        if (root is null) return null;
        var path = Path.Combine(root, name);
        return File.Exists(path) ? path : null;
    }

    private static string? FindDecoder(string? root, string architecture)
    {
        if (root is null) return null;
        var folders = architecture.Equals("Arm64", StringComparison.OrdinalIgnoreCase) ? new[] { "arm64", "x64" } : new[] { "x64", "x86" };
        return folders.Select(folder => Path.Combine(root, folder, "MSRAWImage_store.dll")).FirstOrDefault(File.Exists);
    }
}
