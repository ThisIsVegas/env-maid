using System.Runtime.InteropServices;

namespace EnvMaid.App.Services.Interop;

/// <summary>
/// The kernel32 calls needed to ask whether two paths name the same directory on disk.
/// </summary>
internal static partial class FileSystemNative
{
    private const string Kernel32 = "kernel32.dll";

    internal const uint FILE_READ_ATTRIBUTES = 0x0080;
    internal const uint FILE_SHARE_ALL = 0x07;
    internal const uint OPEN_EXISTING = 3;

    /// <summary>Required to open a <em>directory</em> handle at all.</summary>
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    internal const uint VOLUME_NAME_DOS = 0x0;

    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_PATH_NOT_FOUND = 3;
    internal const int ERROR_ACCESS_DENIED = 5;

    [LibraryImport(Kernel32, EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateFile(
        string path, uint access, uint share, nint securityAttributes,
        uint disposition, uint flags, nint template);

    [LibraryImport(Kernel32, EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial uint GetFinalPathNameByHandle(nint handle, [Out] char[] buffer, uint length, uint flags);

    [LibraryImport(Kernel32, EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFileInformationByHandle(nint handle, out BY_HANDLE_FILE_INFORMATION info);

    [LibraryImport(Kernel32, EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    /// <summary>
    /// Directory identity as the filesystem reports it.
    /// </summary>
    /// <remarks>
    /// The three timestamps are <c>FILETIME</c> — <b>two uints each, not a long</b>. Declaring
    /// them as <c>long</c> adds alignment padding after <c>FileAttributes</c> and shifts every
    /// later field, so <c>VolumeSerialNumber</c> reads padding and comes back zero. That was
    /// observed: with the wrong layout <c>C:\</c>, <c>D:\</c>, <c>E:\</c> and <c>F:\</c> all
    /// reported identical identity, which would have marked four unrelated drives as duplicates
    /// of each other. Do not re-derive this layout.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public uint CreationTimeLow, CreationTimeHigh;
        public uint LastAccessTimeLow, LastAccessTimeHigh;
        public uint LastWriteTimeLow, LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh, FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh, FileIndexLow;
    }
}
