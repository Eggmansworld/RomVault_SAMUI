using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SAM_UI_Avalonia.Core;

/// <summary>
/// Windows taskbar jump list (right-click the SAM-UI taskbar button): music tasks.
/// Each task re-launches the exe with "--jukebox &lt;cmd&gt;", which hands the command to the
/// running window through <see cref="InstanceChannel"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class JumpList
{
    public static void Update(bool musicPlaying)
    {
        try
        {
            string exe = Environment.ProcessPath ?? "";
            if (exe.Length == 0) return;

            var list = (ICustomDestinationList)new CDestinationList();
            Guid iidObjectArray = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref iidObjectArray, out _);

            var tasks = (IObjectCollection)new CEnumerableObjectCollection();
            tasks.AddObject(MakeLink(exe, "--jukebox toggle", musicPlaying ? "Pause music" : "Play music"));
            tasks.AddObject(MakeLink(exe, "--jukebox next", "Next tune"));
            list.AddUserTasks((IObjectArray)tasks);
            list.CommitList();
        }
        catch (Exception ex)
        {
            Log.Warn("Jump list not updated: " + ex.Message);
        }
    }

    private static IShellLinkW MakeLink(string exe, string args, string title)
    {
        var link = (IShellLinkW)new CShellLink();
        link.SetPath(exe);
        link.SetArguments(args);
        link.SetIconLocation(exe, 0);
        link.SetDescription(title);

        var store = (IPropertyStore)link;
        var key = new PropertyKey { fmtid = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), pid = 2 }; // System.Title
        var pv = new PropVariant { vt = 31 /* VT_LPWSTR */, p = Marshal.StringToCoTaskMemUni(title) };
        try
        {
            store.SetValue(ref key, ref pv);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(pv.p);
        }
        return link;
    }

    // ------------------------------------------------------------------ COM interop

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr p;
    }

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")] private class CDestinationList { }
    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")] private class CEnumerableObjectCollection { }
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class CShellLink { }

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint pcMinSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray poa);
        void CommitList();
        void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint cObjects);
        void GetAt(uint iIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        // IObjectArray
        void GetCount(out uint cObjects);
        void GetAt(uint iIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        // IObjectCollection
        void AddObject([MarshalAs(UnmanagedType.Interface)] object punk);
        void AddFromArray(IObjectArray poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription(IntPtr pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory(IntPtr pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments(IntPtr pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }
}
