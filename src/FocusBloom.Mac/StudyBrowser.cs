using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace FocusBloom.Mac;

internal sealed class StudyBrowser : NativeControlHost
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Decide(IntPtr self, IntPtr selector, IntPtr view, IntPtr navigation, IntPtr completion);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Complete(IntPtr block, nint policy);
    private static readonly Dictionary<IntPtr, StudyBrowser> Instances = [];
    private static readonly Decide DecideNavigation = OnNavigation;
    private static IntPtr _delegateType;
    private IntPtr _view;
    private IntPtr _delegate;
    private string _address = "about:blank";
    internal Func<string, bool> Allowed { get; set; } = _ => true;
    internal Action<string>? Blocked { get; set; }
    internal string Address => _view == IntPtr.Zero ? _address : Cocoa.Text(Cocoa.Get(Cocoa.Get(_view, "URL"), "absoluteString"));

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsMacOS()) return base.CreateNativeControlCore(parent);
        if (_delegateType == IntPtr.Zero)
        {
            _delegateType = Cocoa.objc_allocateClassPair(Cocoa.objc_getClass("NSObject"), "FocusBloomWebNavigationDelegate", 0);
            Cocoa.class_addMethod(_delegateType, Cocoa.sel_registerName("webView:decidePolicyForNavigationAction:decisionHandler:"),
                Marshal.GetFunctionPointerForDelegate(DecideNavigation), "v@:@@@?");
            Cocoa.objc_registerClassPair(_delegateType);
        }
        _view = Cocoa.WebView();
        _delegate = Cocoa.Get(Cocoa.Get(_delegateType, "alloc"), "init");
        Instances.Add(_view, this);
        Cocoa.Call(_view, "setNavigationDelegate:", _delegate);
        Load(_address);
        return new PlatformHandle(_view, "NSView");
    }

    internal void Navigate(string address)
    {
        if (!Uri.TryCreate(address.Contains("://") ? address : "https://" + address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new FormatException("请输入 http 或 https 网址 / Enter an HTTP or HTTPS URL.");
        _address = uri.AbsoluteUri;
        if (Allowed(_address)) Load(_address);
        else Blocked?.Invoke(_address);
    }
    private void Load(string address)
    {
        if (_view == IntPtr.Zero) return;
        var url = Cocoa.Call(Cocoa.objc_getClass("NSURL"), "URLWithString:", Cocoa.String(address));
        var request = Cocoa.Call(Cocoa.objc_getClass("NSURLRequest"), "requestWithURL:", url);
        Cocoa.Call(_view, "loadRequest:", request);
    }
    internal void EnforceRules()
    {
        if (_view == IntPtr.Zero || Allowed(Address)) return;
        var denied = Address;
        Cocoa.Get(_view, "stopLoading");
        Cocoa.Call(_view, "loadHTMLString:baseURL:", Cocoa.String("<html><body style='font:20px system-ui;padding:40px'>FocusBloom<br>此网站在本次专注中受限 / Website restricted during this focus session.</body></html>"), IntPtr.Zero);
        Blocked?.Invoke(denied);
    }
    internal void Back() { if (_view != IntPtr.Zero) Cocoa.Get(_view, "goBack"); }
    internal void Reload() { if (_view != IntPtr.Zero) Cocoa.Get(_view, "reload"); }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_view == IntPtr.Zero) { base.DestroyNativeControlCore(control); return; }
        _address = Address is { Length: > 0 } address ? address : _address;
        Instances.Remove(_view);
        Cocoa.Call(_view, "setNavigationDelegate:", IntPtr.Zero);
        Cocoa.Get(_view, "stopLoading");
        Cocoa.Get(_view, "release"); _view = IntPtr.Zero;
        Cocoa.Get(_delegate, "release"); _delegate = IntPtr.Zero;
    }
    private static void OnNavigation(IntPtr self, IntPtr selector, IntPtr view, IntPtr navigation, IntPtr completion)
    {
        var allow = false;
        try
        {
            var address = Cocoa.Text(Cocoa.Get(Cocoa.Get(Cocoa.Get(navigation, "request"), "URL"), "absoluteString"));
            if (Instances.TryGetValue(view, out var browser))
            {
                var frame = Cocoa.Get(navigation, "targetFrame");
                var mainFrame = frame == IntPtr.Zero || Cocoa.Get(frame, "isMainFrame") != IntPtr.Zero;
                allow = !mainFrame || address is "about:blank" or "" || browser.Allowed(address);
                if (!allow) browser.Blocked?.Invoke(address);
            }
        }
        catch { allow = false; }
        finally
        {
            var callback = Marshal.ReadIntPtr(completion, IntPtr.Size * 2);
            Marshal.GetDelegateForFunctionPointer<Complete>(callback)(completion, allow ? 1 : 0);
        }
    }
}
