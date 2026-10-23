using System.Runtime.InteropServices;
using System.IO;

namespace YingView.Files;

/// <summary>Moves one local file to the Windows Recycle Bin using the Shell operation API.</summary>
public static class RecycleBinService
{
    private const uint FofSilent = 0x0004;
    private const uint FofNoConfirmation = 0x0010;
    private const uint FofNoErrorUi = 0x0400;
    private const uint FofxRecycleOnDelete = 0x00080000;
    private const uint FofxEarlyFailure = 0x00100000;
    private const uint FofxAddUndoRecord = 0x20000000;
    private const uint RequiredFlags = FofSilent | FofNoConfirmation | FofNoErrorUi | FofxRecycleOnDelete | FofxEarlyFailure | FofxAddUndoRecord;
    private const int CoInitApartmentThreaded = 0x2;
    private const int ErrorCancelled = unchecked((int)0x800704C7);
    private const int ErrorAborted = unchecked((int)0x80004004);

    /// <summary>Recycles one existing file without UI or permanent-delete fallback. Returns false when the Shell operation is aborted.</summary>
    public static Task<bool> RecycleAsync(string path, IntPtr ownerHwnd)
    {
        var fullPath = ValidateFilePath(path);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var initialized = false;
            IFileOperation? operation = null;
            var progressSink = new RecycleOnlyProgressSink();
            IntPtr shellItem = IntPtr.Zero;
            try
            {
                var hr = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                initialized = true;

                var operationType = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), throwOnError: true)!;
                operation = (IFileOperation)Activator.CreateInstance(operationType)!;
                Check(operation.SetOwnerWindow(ownerHwnd));
                Check(operation.SetOperationFlags(RequiredFlags));
                var shellItemIid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
                Check(SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ref shellItemIid, out shellItem));
                Check(operation.DeleteItem(shellItem, progressSink));
                Check(operation.PerformOperations());
                Check(operation.GetAnyOperationsAborted(out var aborted));
                if (aborted != 0) { completion.TrySetResult(false); return; }
                if (File.Exists(fullPath)) throw new IOException("The Shell operation completed, but the source file is still present.");
                completion.TrySetResult(true);
            }
            catch (Exception ex) when (ex.HResult == ErrorCancelled)
            {
                completion.TrySetResult(false);
            }
            catch (Exception ex) when (ex.HResult == ErrorAborted)
            {
                completion.TrySetResult(false);
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                if (shellItem != IntPtr.Zero) Marshal.Release(shellItem);
                if (operation is not null && Marshal.IsComObject(operation)) Marshal.FinalReleaseComObject(operation);
                if (initialized) CoUninitialize();
            }
        }) { IsBackground = true, Name = "YingView Recycle Bin operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static string ValidateFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath)) throw new ArgumentException("Folders cannot be sent to the Recycle Bin by this operation.", nameof(path));
        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal)) throw new NotSupportedException("Network files cannot be sent to the local Recycle Bin.");
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root) || GetDriveType(root) != 3)
            throw new NotSupportedException("Only files on a ready local fixed drive are supported.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The file does not exist.", fullPath);
        return fullPath;
    }

    private static void Check(int hresult) { if (hresult < 0) Marshal.ThrowExceptionForHR(hresult); }

    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(IntPtr reserved, int coInit);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
    [DllImport("shell32.dll", EntryPoint = "SHCreateItemFromParsingName", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [In] ref Guid iid, out IntPtr shellItem);
    [DllImport("kernel32.dll", EntryPoint = "GetDriveTypeW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint GetDriveType(string rootPathName);

    // Method order and signatures follow the installed Windows SDK shobjidl_core.h IFileOperation declaration.
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr hwnd);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int MoveItem(IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IntPtr destination);
        [PreserveSig] int CopyItem(IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IntPtr destination);
        [PreserveSig] int DeleteItem(IntPtr item, [MarshalAs(UnmanagedType.Interface)] IFileOperationProgressSink sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IntPtr destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted(out int aborted);
    }

    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostRenameItem(uint flags, IntPtr item, [MarshalAs(UnmanagedType.LPWStr)] string newName, int result, IntPtr newlyCreated);
        [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, IntPtr newlyCreated);
        [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int result, IntPtr newlyCreated);
        [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
        [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newlyCreated);
        [PreserveSig] int PreNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName);
        [PreserveSig] int PostNewItem(uint flags, IntPtr destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, uint fileAttributes, int result, IntPtr newItem);
        [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class RecycleOnlyProgressSink : IFileOperationProgressSink
    {
        private const uint TransferSourceDeleteRecycleIfPossible = 0x00000080;
        private const int SOk = 0;
        private const int EAbort = unchecked((int)0x80004004);

        public int StartOperations() => SOk;
        public int FinishOperations(int result) => SOk;
        public int PreRenameItem(uint flags, IntPtr item, string? newName) => SOk;
        public int PostRenameItem(uint flags, IntPtr item, string newName, int result, IntPtr newlyCreated) => SOk;
        public int PreMoveItem(uint flags, IntPtr item, IntPtr destination, string? newName) => SOk;
        public int PostMoveItem(uint flags, IntPtr item, IntPtr destination, string? newName, int result, IntPtr newlyCreated) => SOk;
        public int PreCopyItem(uint flags, IntPtr item, IntPtr destination, string? newName) => SOk;
        public int PostCopyItem(uint flags, IntPtr item, IntPtr destination, string? newName, int result, IntPtr newlyCreated) => SOk;
        public int PreDeleteItem(uint flags, IntPtr item) => (flags & TransferSourceDeleteRecycleIfPossible) != 0 ? SOk : EAbort;
        public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr newlyCreated) => SOk;
        public int PreNewItem(uint flags, IntPtr destination, string? newName) => SOk;
        public int PostNewItem(uint flags, IntPtr destination, string? newName, string? templateName, uint fileAttributes, int result, IntPtr newItem) => SOk;
        public int UpdateProgress(uint workTotal, uint workSoFar) => SOk;
        public int ResetTimer() => SOk;
        public int PauseTimer() => SOk;
        public int ResumeTimer() => SOk;
    }
}
