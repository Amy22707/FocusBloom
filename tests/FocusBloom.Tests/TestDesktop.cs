using System.Runtime.InteropServices;
using System.Text;

namespace FocusBloom.Tests;

internal static class TestDesktop
{
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(int flags, bool inherit, int access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder data, int length, out int needed);
    private static string Name(IntPtr handle) { var text = new StringBuilder(256); if (!GetUserObjectInformation(handle, 2, text, 512, out _)) throw new System.ComponentModel.Win32Exception(); return text.ToString(); }
    public static string Current => Name(GetThreadDesktop(GetCurrentThreadId()));
    public static string Input { get { var handle = OpenInputDesktop(0, false, 1); if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(); try { return Name(handle); } finally { CloseDesktop(handle); } } }
    public static void RequireIsolated()
    {
        var expected = Environment.GetEnvironmentVariable("FOCUSBLOOM_TEST_DESKTOP");
        if (string.IsNullOrEmpty(expected) || Current != expected || Current == Input) throw new InvalidOperationException("界面测试须通过 scripts/test-isolated.ps1 在独立桌面运行。");
    }
}
