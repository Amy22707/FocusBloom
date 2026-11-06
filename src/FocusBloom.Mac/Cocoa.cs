using System.Runtime.InteropServices;

namespace FocusBloom.Mac;

internal static class Cocoa
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] internal static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] internal static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] internal static extern IntPtr objc_allocateClassPair(IntPtr parent, string name, nuint extra);
    [DllImport(ObjC)] internal static extern void objc_registerClassPair(IntPtr type);
    [DllImport(ObjC)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool class_addMethod(IntPtr type, IntPtr selector, IntPtr callback, string encoding);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send0(IntPtr target, IntPtr method);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send1(IntPtr target, IntPtr method, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send2(IntPtr target, IntPtr method, IntPtr first, IntPtr second);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendString(IntPtr target, IntPtr method, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr CreateView(IntPtr target, IntPtr method, Rect rect, IntPtr configuration);
    [StructLayout(LayoutKind.Sequential)] internal readonly record struct Rect(double X, double Y, double Width, double Height);
    internal static IntPtr Get(IntPtr target, string method) => target == IntPtr.Zero ? IntPtr.Zero : Send0(target, sel_registerName(method));
    internal static IntPtr Call(IntPtr target, string method, IntPtr arg) => Send1(target, sel_registerName(method), arg);
    internal static IntPtr Call(IntPtr target, string method, IntPtr first, IntPtr second) => Send2(target, sel_registerName(method), first, second);
    internal static IntPtr New(string name) => Get(Get(objc_getClass(name), "alloc"), "init");
    internal static IntPtr String(string text) => SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), text);
    internal static string Text(IntPtr value) => value == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Get(value, "UTF8String")) ?? "";
    internal static IntPtr WebView()
    {
        NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");
        var configuration = New("WKWebViewConfiguration");
        var view = CreateView(Get(objc_getClass("WKWebView"), "alloc"), sel_registerName("initWithFrame:configuration:"), new(0, 0, 640, 420), configuration);
        Get(configuration, "release");
        return view;
    }
    internal static string FrontApplication()
    {
        var workspace = Get(objc_getClass("NSWorkspace"), "sharedWorkspace");
        var app = Get(workspace, "frontmostApplication");
        return Text(Get(app, "bundleIdentifier"));
    }
    internal static IReadOnlyList<(string Id, string Name)> Applications()
    {
        var apps = Get(Get(objc_getClass("NSWorkspace"), "sharedWorkspace"), "runningApplications");
        var count = (int)Get(apps, "count");
        var list = new List<(string, string)>();
        for (var i = 0; i < count; i++)
        {
            var app = Call(apps, "objectAtIndex:", (IntPtr)i);
            var id = Text(Get(app, "bundleIdentifier"));
            var name = Text(Get(app, "localizedName"));
            if (id.Length > 0 && name.Length > 0 && (int)Get(app, "activationPolicy") == 0) list.Add((id, name));
        }
        return list;
    }
    internal static bool HideApplication(string id)
    {
        var matches = Call(objc_getClass("NSRunningApplication"), "runningApplicationsWithBundleIdentifier:", String(id));
        if ((int)Get(matches, "count") == 0) return false;
        return Get(Call(matches, "objectAtIndex:", IntPtr.Zero), "hide") != IntPtr.Zero;
    }
    internal static void Activate()
    {
        var app = Get(objc_getClass("NSRunningApplication"), "currentApplication");
        Call(app, "activateWithOptions:", (IntPtr)2);
    }
}
