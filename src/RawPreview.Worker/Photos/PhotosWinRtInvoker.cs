using System.Reflection;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace RawPreview.Worker.Photos;

public sealed class PhotosWinRtInvoker
{
    private static readonly Guid ResizeServiceIid = new("CD2468BB-9DEA-58F0-8A9F-F26DBF55B12F");
    private static readonly Guid MediaItemFactoryIid = new("5F75EF70-C061-53EA-BF34-8FF4ADB38EFB");
    private static readonly Guid MediaStoreIid = new("5FEC44EA-228A-5D5A-94A4-0CC12810B64E");
    private static readonly Guid StorageFileIid = new("FA3F6186-4214-428C-A64C-14C9AC7315EA");
    private static readonly object BootstrapGate = new();
    private static readonly object PhotoModulesGate = new();
    private static bool bootstrapInitialized;
    private static bool photoModulesLoaded;
    private static readonly List<IntPtr> photoModules = [];

    public static async Task ExportAsync(string sourcePath, string targetPath, uint width, uint height, double quality, string lightboxDllPath, CancellationToken cancellationToken)
    {
        var bootstrapPath = Path.Combine(Path.GetDirectoryName(lightboxDllPath)!, "Microsoft.WindowsAppRuntime.Bootstrap.dll");
        InitializeWindowsAppRuntime(bootstrapPath);
        LoadPhotoSupportModules(Path.GetDirectoryName(lightboxDllPath)!);

        StorageFile sourceFile;
        try { sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath); }
        catch (Exception exception) { throw new PhotosRuntimeException("PhotosRawDecodeFailed", exception.Message, exception); }

        using var sourceMarshaler = MarshalInspectable<StorageFile>.CreateMarshaler(sourceFile, false);
        var sourceObject = MarshalInspectable<StorageFile>.GetAbi(sourceMarshaler);
        var sourceInterface = QueryInterface(sourceObject, StorageFileIid);
        var lightboxModule = LoadLightbox(lightboxDllPath);
        try
        {
            var mediaStoreObject = Activate("Lightbox.MediaStore", lightboxModule);
            var mediaStoreInterface = QueryInterface(mediaStoreObject, MediaStoreIid);
            try
            {
                var itemFactory = GetActivationFactory("Lightbox.MediaItem", MediaItemFactoryIid, lightboxModule);
                try
                {
                    var create = Vtable<CreateMediaItemDelegate>(itemFactory, 6);
                    ThrowIfFailed(create(itemFactory, 0, sourceInterface, mediaStoreInterface, 3, out var mediaItem), "IMediaItemFactory.CreateInstance");
                    try
                    {
                        var resizeServiceObject = Activate("Lightbox.ResizeService", lightboxModule);
                        var resizeService = QueryInterface(resizeServiceObject, ResizeServiceIid);
                        try
                        {
                            var extension = HString.Create(".jpg");
                            try
                            {
                                var parameters = new ResizeParamsAbi
                                {
                                    TargetWidth = width,
                                    TargetHeight = height,
                                    TargetQuality = quality / 100.0,
                                    TargetFileExtension = extension.Handle
                                };
                                var resize = Vtable<ResizeDelegate>(resizeService, 6);
                                ThrowIfFailed(resize(resizeService, mediaItem, parameters, out var resizeOperation), "IResizeService.ResizeAsync");
                                try
                                {
                                    using var resizeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                                    resizeTimeout.CancelAfter(TimeSpan.FromSeconds(120));
                                    var resizeResult = await WaitForResultAsync(resizeOperation, "ResizeAsync", lightboxDllPath, resizeTimeout.Token);
                                    await CopyResizeResultAsync(resizeResult, targetPath, cancellationToken);
                                }
                                finally { Marshal.Release(resizeOperation); }
                            }
                            finally { extension.Dispose(); }
                        }
                        finally { Marshal.Release(resizeService); Marshal.Release(resizeServiceObject); }
                    }
                    finally { Marshal.Release(mediaItem); }
                }
                finally { Marshal.Release(itemFactory); }
            }
            finally
            {
                Marshal.Release(mediaStoreInterface);
                Marshal.Release(mediaStoreObject);
            }
        }
        finally
        {
            Marshal.Release(sourceInterface);
            FreeLibrary(lightboxModule);
        }

    }

    private static IntPtr Activate(string className, IntPtr module)
    {
        var factory = GetDllActivationFactory(className, module);
        try
        {
            var activate = Vtable<ActivateInstanceDelegate>(factory, 6);
            var hr = activate(factory, out var instance);
            ThrowIfFailed(hr, "IActivationFactory.ActivateInstance");
            return instance;
        }
        finally { Marshal.Release(factory); }
    }

    private static IntPtr GetActivationFactory(string className, Guid iid, IntPtr module)
    {
        var factory = GetDllActivationFactory(className, module);
        try
        {
            return QueryInterface(factory, iid);
        }
        finally { Marshal.Release(factory); }
    }

    private static IntPtr GetDllActivationFactory(string className, IntPtr module)
    {
        var address = GetProcAddress(module, "DllGetActivationFactory");
        if (address == IntPtr.Zero)
            throw new PhotosRuntimeException("PhotosContextInitializationFailed", "Lightbox.dll does not export DllGetActivationFactory.");
        var getFactory = Marshal.GetDelegateForFunctionPointer<DllGetActivationFactoryDelegate>(address);
        ThrowIfFailed(WindowsCreateString(className, className.Length, out var hstring), "WindowsCreateString");
        try
        {
            ThrowIfFailed(getFactory(hstring, out var factory), $"DllGetActivationFactory({className})");
            return factory;
        }
        finally { ThrowIfFailed(WindowsDeleteString(hstring), "WindowsDeleteString"); }
    }

    private static IntPtr LoadLightbox(string path)
    {
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x00000008);
        if (module == IntPtr.Zero)
            throw new PhotosRuntimeException("PhotosContextInitializationFailed", $"Lightbox.dll could not be loaded from {path} (Win32 error {Marshal.GetLastWin32Error()}).");
        return module;
    }

    public static void ProbeLoad(string lightboxDllPath)
    {
        var bootstrapPath = Path.Combine(Path.GetDirectoryName(lightboxDllPath)!, "Microsoft.WindowsAppRuntime.Bootstrap.dll");
        InitializeWindowsAppRuntime(bootstrapPath);
        var module = LoadLightbox(lightboxDllPath);
        FreeLibrary(module);
    }

    private static void LoadPhotoSupportModules(string photoRoot)
    {
        lock (PhotoModulesGate)
        {
            if (photoModulesLoaded) return;
            var loaded = new List<IntPtr>(3);
            try
            {
                foreach (var name in new[] { "Photos.Models.dll", "PhotosServiceSdk.dll", "PhotosCore.dll" })
                {
                    var path = Path.Combine(photoRoot, name);
                    if (!File.Exists(path)) continue;
                    var module = LoadLibraryEx(path, IntPtr.Zero, 0x00000008);
                    if (module == IntPtr.Zero)
                        throw new PhotosRuntimeException("PhotosContextInitializationFailed", $"{name} could not be loaded (Win32 error {Marshal.GetLastWin32Error()}).");
                    loaded.Add(module);
                }
                photoModules.AddRange(loaded);
                photoModulesLoaded = true;
            }
            catch
            {
                foreach (var module in loaded) FreeLibrary(module);
                throw;
            }
        }
    }

    private static void InitializeWindowsAppRuntime(string bootstrapPath)
    {
        lock (BootstrapGate)
        {
            if (bootstrapInitialized) return;
            if (HasPackageIdentity())
            {
                bootstrapInitialized = true;
                return;
            }
            var module = LoadLibraryEx(bootstrapPath, IntPtr.Zero, 0x00000008);
            if (module == IntPtr.Zero)
                throw new PhotosRuntimeException("PhotosContextInitializationFailed", $"Windows App Runtime bootstrap could not be loaded from {bootstrapPath} (Win32 error {Marshal.GetLastWin32Error()}).");
            var address = GetProcAddress(module, "MddBootstrapInitialize2");
            if (address == IntPtr.Zero)
                throw new PhotosRuntimeException("PhotosContextInitializationFailed", "Windows App Runtime bootstrap does not export MddBootstrapInitialize2.");
            var initialize = Marshal.GetDelegateForFunctionPointer<MddBootstrapInitialize2Delegate>(address);
            var version = new PackageVersion { Major = 2, Minor = 0, Build = 0, Revision = 0 };
            ThrowIfFailed(initialize(0x00020000, null, version, 0), "MddBootstrapInitialize2");
            bootstrapInitialized = true;
        }
    }

    private static bool HasPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id.FullName;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr QueryInterface(IntPtr instance, Guid iid)
    {
        var iidCopy = iid;
        var hr = Marshal.QueryInterface(instance, iidCopy, out var result);
        if (hr < 0)
            throw new PhotosRuntimeException("PhotosContractInvocationFailed", $"QueryInterface({iid}) failed with HRESULT 0x{hr:X8}; available={DescribeInterfaces(instance)}");
        return result;
    }

    private static string DescribeInterfaces(IntPtr instance)
    {
        try
        {
            var getIids = Vtable<GetIidsDelegate>(instance, 3);
            ThrowIfFailed(getIids(instance, out var count, out var iids), "IInspectable.GetIids");
            try
            {
                var values = new string[count];
                for (var index = 0; index < count; index++)
                    values[index] = Marshal.PtrToStructure<Guid>(iids + index * 16).ToString();
                return string.Join(",", values);
            }
            finally
            {
                CoTaskMemFree(iids);
            }
        }
        catch (Exception exception)
        {
            return $"<unavailable:{exception.Message}>";
        }
    }

    private static T Vtable<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
    }

    private static async Task<object> WaitForResultAsync(IntPtr operation, string operationName, string lightboxDllPath, CancellationToken cancellationToken)
    {
        var projectionPath = Path.Combine(Path.GetDirectoryName(lightboxDllPath)!, "PhotosCsProjection.dll");
        if (!File.Exists(projectionPath))
            throw new PhotosRuntimeException("PhotosContextInitializationFailed", "PhotosCsProjection.dll was not found.");
        var projectionAssembly = Assembly.LoadFrom(projectionPath);
        var resultType = projectionAssembly.GetType("Lightbox.ResizeResult", throwOnError: true)!;
        var operationType = typeof(IAsyncOperation<>).MakeGenericType(resultType);
        var operationMarshaler = typeof(MarshalInspectable<>).MakeGenericType(operationType);
        var fromAbi = operationMarshaler.GetMethod("FromAbi", BindingFlags.Public | BindingFlags.Static)
            ?? throw new PhotosRuntimeException("PhotosContextInitializationFailed", "The WinRT async operation projection is unavailable.");
        var operationObject = fromAbi.Invoke(null, [operation]);
        if (operationObject is null)
            throw new PhotosRuntimeException("PhotosContextInitializationFailed", "The Photos async operation could not be projected.");
        try
        {
            var asTask = typeof(System.WindowsRuntimeSystemExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "AsTask" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1)
                .Single(method => method.GetParameters() is [{ ParameterType.IsGenericType: true }] parameters &&
                    parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(IAsyncOperation<>));
            var task = (Task)asTask.MakeGenericMethod(resultType).Invoke(null, [operationObject])!;
            await task.WaitAsync(cancellationToken);
            var resultObject = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance)?.GetValue(task)
                ?? throw new PhotosRuntimeException("PhotosJpegWriteFailed", "Photos returned no resize result.");
            return resultObject;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new PhotosRuntimeException("PhotosCancelled", $"Photos async operation {operationName} timed out or was cancelled.");
        }
        finally
        {
            GC.KeepAlive(operationObject);
        }
    }

    private static void ThrowIfFailed(int hr, string call)
    {
        if (hr < 0) throw new PhotosRuntimeException("PhotosContractInvocationFailed", $"{call} failed with HRESULT 0x{hr:X8}.");
    }

    private static async Task CopyResizeResultAsync(object resizeResult, string targetPath, CancellationToken cancellationToken)
    {
        var streamProperty = resizeResult.GetType().GetProperty("OutputFileStream", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new PhotosRuntimeException("PhotosJpegWriteFailed", "Photos resize result has no output stream.");
        var stream = streamProperty.GetValue(resizeResult) as InMemoryRandomAccessStream
            ?? throw new PhotosRuntimeException("PhotosJpegWriteFailed", "Photos returned no output stream.");
        if (stream.Size == 0)
            throw new PhotosRuntimeException("PhotosJpegWriteFailed", "Photos returned an empty output stream.");
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        ulong remaining = stream.Size;
        while (remaining > 0)
        {
            var requested = (uint)Math.Min(128 * 1024, remaining);
            var loaded = await reader.LoadAsync(requested).AsTask(cancellationToken);
            if (loaded == 0)
                throw new PhotosRuntimeException("PhotosJpegWriteFailed", "Photos output stream ended before its declared size.");
            var chunk = new byte[checked((int)loaded)];
            reader.ReadBytes(chunk);
            await output.WriteAsync(chunk, cancellationToken);
            remaining -= loaded;
        }
        await output.FlushAsync(cancellationToken);
    }

    private sealed class HString : IDisposable
    {
        public IntPtr Handle { get; private set; }
        private HString(IntPtr handle) => Handle = handle;
        public static HString Create(string value)
        {
            ThrowIfFailed(WindowsCreateString(value, value.Length, out var handle), "WindowsCreateString");
            return new HString(handle);
        }
        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                ThrowIfFailed(WindowsDeleteString(Handle), "WindowsDeleteString");
                Handle = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ResizeParamsAbi
    {
        public uint TargetWidth;
        public uint TargetHeight;
        public double TargetQuality;
        public IntPtr TargetFileExtension;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PackageVersion
    {
        public ushort Major;
        public ushort Minor;
        public ushort Build;
        public ushort Revision;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ResizeDelegate(IntPtr self, IntPtr mediaItem, ResizeParamsAbi resizeParams, out IntPtr operation);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIidsDelegate(IntPtr self, out uint count, out IntPtr iids);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ActivateInstanceDelegate(IntPtr self, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetActivationFactoryDelegate(IntPtr classId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MddBootstrapInitialize2Delegate(uint majorMinorVersion, [MarshalAs(UnmanagedType.LPWStr)] string? versionTag, PackageVersion minVersion, uint options);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateMediaItemDelegate(IntPtr self, uint queryResultIndex, IntPtr storageFile, IntPtr mediaStore, int activationKind, out IntPtr result);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoTaskMemFree(IntPtr memory);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string procedureName);
}
