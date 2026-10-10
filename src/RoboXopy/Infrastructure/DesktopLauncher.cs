using System.Runtime.InteropServices;

namespace RoboXopy.Infrastructure;

/// <summary>
/// Starts a program through the desktop's Explorer process, so the new process inherits nothing from
/// this one – in particular not the RedirectionGuard mitigation an installer may have passed on to us.
/// </summary>
internal static class DesktopLauncher
{
    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const uint SvgioBackground = 0;
    private const int SwShowNormal = 1;

    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid TopLevelBrowserSid = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserIid = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid DispatchIid = new("00020400-0000-0000-C000-000000000046");

    public static bool TryStart(string file, string arguments, string directory)
    {
        var objects = new List<object>();
        try
        {
            if (Type.GetTypeFromCLSID(ShellWindowsClsid, throwOnError: false) is not { } type
                || Activator.CreateInstance(type) is not IShellWindows windows)
            {
                return false;
            }

            objects.Add(windows);
            object location = CsidlDesktop;
            object? root = null;
            if (windows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch, out var desktop) != 0
                || desktop is not IComServiceProvider provider)
            {
                return false;
            }

            objects.Add(desktop);
            var sid = TopLevelBrowserSid;
            var iid = ShellBrowserIid;
            if (provider.QueryService(ref sid, ref iid, out var browserObject) != 0 || browserObject is not IShellBrowser browser) return false;
            objects.Add(browserObject);

            if (browser.QueryActiveShellView(out var view) != 0 || view is null) return false;
            objects.Add(view);

            var dispatchIid = DispatchIid;
            if (view.GetItemObject(SvgioBackground, ref dispatchIid, out var folderView) != 0 || folderView is null) return false;
            objects.Add(folderView);

            dynamic shell = ((dynamic)folderView).Application;
            objects.Add(shell);
            shell.ShellExecute(file, arguments, directory, "open", SwShowNormal);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            foreach (var obj in objects)
            {
                if (Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj);
            }
        }
    }

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        // IDispatch
        void GetTypeInfoCount();
        void GetTypeInfo();
        void GetIDsOfNames();
        void Invoke();

        void get_Count();
        void Item();
        void _NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [PreserveSig]
        int FindWindowSW(ref object location, ref object? root, int windowClass, out int hwnd, int options,
            [MarshalAs(UnmanagedType.IDispatch)] out object? dispatch);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow
        void GetWindow();
        void ContextSensitiveHelp();

        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView(out IShellView? view);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        // IOleWindow
        void GetWindow();
        void ContextSensitiveHelp();

        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();
        void SelectItem();

        [PreserveSig]
        int GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.IDispatch)] out object? result);
    }
}
