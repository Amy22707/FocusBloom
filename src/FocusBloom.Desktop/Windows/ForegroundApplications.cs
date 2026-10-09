using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using FocusBloom.Core.Restrictions;

namespace FocusBloom.Desktop.Windows;

public sealed record WindowApplication(int ProcessId, string ProcessName, string Name, string? ExecutablePath);

public static class ForegroundApplications
{
    public static IReadOnlyList<AllowedApplication> GetRunningApplications()
    {
        var applications = new List<AllowedApplication>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero) continue;
                    var app = FromWindow(process.MainWindowHandle);
                    if (app?.ExecutablePath is not null) applications.Add(new AllowedApplication(app.Name, app.ExecutablePath));
                }
                catch (InvalidOperationException) { }
            }
        }
        return applications.DistinctBy(a => a.ExecutablePath, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name).ToArray();
    }

    public static WindowApplication? FromWindow(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var id);
        var application = Read((int)id);
        if (application?.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) == true)
        {
            EnumChildWindows(window, (child, _) =>
            {
                GetWindowThreadProcessId(child, out var childId);
                if (childId == id) return true;
                var hosted = Read((int)childId);
                if (hosted is null) return true;
                application = hosted;
                return false;
            }, IntPtr.Zero);
        }
        return application;
    }

    private static WindowApplication? Read(int id)
    {
        if (id == 0) return null;
        try
        {
            using var process = Process.GetProcessById(id);
            var path = ReadPath(id);
            string? name = null;
            if (path is not null)
            {
                try { name = FileVersionInfo.GetVersionInfo(path).FileDescription; }
                catch (System.IO.FileNotFoundException) { }
            }
            return new WindowApplication(id, process.ProcessName, string.IsNullOrWhiteSpace(name) ? process.ProcessName : name, path);
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (Win32Exception) { return null; }
    }

    private static string? ReadPath(int id)
    {
        var handle = OpenProcess(0x1000, false, id);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(32768);
            var length = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref length) ? buffer.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    private delegate bool EnumChildCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumChildCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
