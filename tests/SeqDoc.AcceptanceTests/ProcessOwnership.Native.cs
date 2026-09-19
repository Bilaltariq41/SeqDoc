namespace SeqDoc.AcceptanceTests;

// Native adapter extension points are kept in a separate file so the lifecycle coordinator never owns
// P/Invoke policy.  The adapter itself remains internal to this acceptance primitive.
internal static class ProcessOwnershipNativeAdapter
{
    internal static NativeCallResult Close(ProcessOwnershipNativeCalls calls, nint handle) =>
        calls.CloseHandle?.Invoke(handle) ?? (NativeMethods.CloseHandle(handle)
            ? NativeCallResult.Success() : NativeCallResult.Failure(System.Runtime.InteropServices.Marshal.GetLastWin32Error()));
}
