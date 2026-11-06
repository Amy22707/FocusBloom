using System.Runtime.InteropServices;

namespace FocusBloom.Mac;

internal static class MacNotifications
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Completion(int code, IntPtr context);
    private static readonly Completion Completed = (code, context) =>
    {
        var handle = GCHandle.FromIntPtr(context);
        var task = (TaskCompletionSource<int>)handle.Target!;
        handle.Free();
        task.SetResult(code);
    };
    [DllImport("libFocusBloomNotifications.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void focusbloom_notify(int action, [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string body, IntPtr context, Completion callback);
    internal static Task<int> Send(bool authorize, string title = "", string body = "")
    {
        var task = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(task);
        try { focusbloom_notify(authorize ? 0 : 1, title, body, GCHandle.ToIntPtr(handle), Completed); }
        catch { handle.Free(); throw; }
        return task.Task;
    }
}
