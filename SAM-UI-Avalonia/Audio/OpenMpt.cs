using System.Runtime.InteropServices;
using SAM_UI_Avalonia.Core;

namespace SAM_UI_Avalonia.Audio;

/// <summary>
/// libopenmpt 0.8.10 (BSD-3, https://lib.openmpt.org/) for MOD/S3M/XM/IT and friends.
/// The DLLs ship in the "Native" folder next to the exe (Windows x64).
/// Ported from Eggman's Desktop Manager.
/// </summary>
internal static class OpenMpt
{
    private static readonly string[] LoadOrder = { "openmpt-ogg.dll", "openmpt-zlib.dll", "openmpt-mpg123.dll", "openmpt-vorbis.dll", "libopenmpt.dll" };
    private static IntPtr _lib;
    private static bool _tried;
    public static string? LoadError { get; private set; }

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr openmpt_module_create_from_memory2(byte[] data, UIntPtr size, IntPtr logfunc, IntPtr loguser,
        IntPtr errfunc, IntPtr erruser, out int error, out IntPtr errorMessage, IntPtr ctls);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    public static extern void openmpt_module_destroy(IntPtr mod);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr openmpt_module_read_interleaved_stereo(IntPtr mod, int rate, UIntPtr count, short[] buffer);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    public static extern int openmpt_module_set_repeat_count(IntPtr mod, int count);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    public static extern double openmpt_module_get_duration_seconds(IntPtr mod);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    public static extern int openmpt_module_set_render_param(IntPtr mod, int param, int value);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    public static extern int openmpt_module_ctl_set_text(IntPtr mod, [MarshalAs(UnmanagedType.LPUTF8Str)] string ctl, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr openmpt_module_get_metadata(IntPtr mod, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    private static extern void openmpt_free_string(IntPtr s);

    [DllImport("libopenmpt", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr openmpt_get_supported_extensions();

    public static bool EnsureLoaded()
    {
        if (_lib != IntPtr.Zero) return true;
        if (_tried) return false;
        _tried = true;
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("the jukebox is Windows only");
            string dir = Path.Combine(AppContext.BaseDirectory, "Native");
            IntPtr h = IntPtr.Zero;
            foreach (string name in LoadOrder) h = NativeLibrary.Load(Path.Combine(dir, name));
            _lib = h;
            NativeLibrary.SetDllImportResolver(typeof(OpenMpt).Assembly, (lib, _, _) => lib == "libopenmpt" ? _lib : IntPtr.Zero);
            return true;
        }
        catch (DllNotFoundException ex)
        {
            LoadError = "libopenmpt could not load - install the Microsoft Visual C++ 2015-2022 Redistributable (x64). " + ex.Message;
        }
        catch (Exception ex)
        {
            LoadError = "libopenmpt could not load: " + ex.Message;
        }
        Log.Error(LoadError);
        return false;
    }

    public static string Metadata(IntPtr mod, string key)
    {
        IntPtr p = openmpt_module_get_metadata(mod, key);
        if (p == IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUTF8(p) ?? ""; }
        finally { openmpt_free_string(p); }
    }

    public static HashSet<string> SupportedExtensions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!EnsureLoaded()) return set;
        IntPtr p = openmpt_get_supported_extensions();
        string s = Marshal.PtrToStringUTF8(p) ?? "";
        openmpt_free_string(p);
        foreach (string e in s.Split(';', StringSplitOptions.RemoveEmptyEntries)) set.Add("." + e.Trim());
        return set;
    }

    public static IntPtr Create(byte[] data, out string error)
    {
        error = "";
        if (!EnsureLoaded()) { error = LoadError ?? "libopenmpt unavailable"; return IntPtr.Zero; }
        IntPtr mod = openmpt_module_create_from_memory2(data, (UIntPtr)data.Length, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out int err, out IntPtr msg, IntPtr.Zero);
        if (mod == IntPtr.Zero)
        {
            error = msg != IntPtr.Zero ? Marshal.PtrToStringUTF8(msg) ?? $"error {err}" : $"error {err}";
            if (msg != IntPtr.Zero) openmpt_free_string(msg);
        }
        return mod;
    }
}

/// <summary>A tracker module played through libopenmpt.</summary>
internal sealed class ModSource : ITuneSource
{
    private IntPtr _mod;

    public string Title { get; }
    public string Format { get; }
    public double Duration { get; }

    public ModSource(string path)
    {
        _mod = OpenMpt.Create(File.ReadAllBytes(path), out string err);
        if (_mod == IntPtr.Zero) throw new InvalidDataException(err);
        OpenMpt.openmpt_module_set_repeat_count(_mod, 0);
        OpenMpt.openmpt_module_set_render_param(_mod, 2, 70);   // stereo separation: Amiga hard-panning softened a bit
        OpenMpt.openmpt_module_set_render_param(_mod, 3, 8);    // 8-tap windowed sinc
        OpenMpt.openmpt_module_ctl_set_text(_mod, "render.resampler.emulate_amiga", "1");
        // file names are what people organise by; many module titles are junk like "003"
        Title = Path.GetFileNameWithoutExtension(path);
        Format = OpenMpt.Metadata(_mod, "type").ToUpperInvariant();
        Duration = OpenMpt.openmpt_module_get_duration_seconds(_mod);
    }

    public int Render(short[] buffer, int frames)
    {
        if (_mod == IntPtr.Zero) return 0;
        return (int)OpenMpt.openmpt_module_read_interleaved_stereo(_mod, StreamOut.Rate, (UIntPtr)frames, buffer);
    }

    public void Dispose()
    {
        if (_mod != IntPtr.Zero) OpenMpt.openmpt_module_destroy(_mod);
        _mod = IntPtr.Zero;
    }
}
