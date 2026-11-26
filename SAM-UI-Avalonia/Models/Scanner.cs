using TrrntZip;

namespace SAM_UI_Avalonia.Models;

public sealed record ScanItem(string Path, long Size, bool IsDir);

public sealed class ScanResult
{
    public required InputZipType Input { get; init; }
    /// <summary>What will be processed, in processing order.</summary>
    public List<ScanItem> Items { get; } = new();
    /// <summary>Paths handed to FileAdder: the items plus stray __*.tztmp files for it to delete.</summary>
    public List<string> HandOff { get; } = new();
    public long TotalBytes { get; set; }
}

/// <summary>
/// Expands staged files/folders into the list of items SAM will process. Mirrors the rules and
/// the ordering of TrrntZip.FileAdder exactly (dropped files first, then each folder: its files
/// sorted, then its sub-folders sorted), but without deleting anything, so it doubles as PREVIEW.
/// </summary>
public static class Scanner
{
    public static ScanResult Scan(IReadOnlyList<string> roots, InputZipType input, CancellationToken ct)
    {
        var r = new ScanResult { Input = input };

        foreach (string p in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(p)) AddFile(r, new FileInfo(p), input);
        }

        foreach (string p in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(p)) continue;

            if (input == InputZipType.Directory)
            {
                long size = DirSize(new DirectoryInfo(p), ct);
                r.Items.Add(new ScanItem(p, size, true));
                r.HandOff.Add(p);
                r.TotalBytes += size;
            }
            else
                AddDirectory(r, new DirectoryInfo(p), input, ct);
        }
        return r;
    }

    /// <summary>Same test as FileAdder.AddFile.</summary>
    public static bool Matches(string path, InputZipType input)
    {
        string extn = Path.GetExtension(path).ToLowerInvariant();
        if (extn == ".zip" && input is InputZipType.Zip or InputZipType.Archive or InputZipType.All) return true;
        if (extn == ".7z" && input is InputZipType.SevenZip or InputZipType.Archive or InputZipType.All) return true;
        return input is InputZipType.File or InputZipType.All;
    }

    private static bool IsTempJunk(FileInfo f) =>
        f.Extension.Equals(".tztmp", StringComparison.OrdinalIgnoreCase) && f.Name.StartsWith("__", StringComparison.Ordinal);

    private static void AddFile(ScanResult r, FileInfo f, InputZipType input)
    {
        if (IsTempJunk(f)) { r.HandOff.Add(f.FullName); return; }
        if (!Matches(f.FullName, input)) return;
        long size = SafeLength(f);
        r.Items.Add(new ScanItem(f.FullName, size, false));
        r.HandOff.Add(f.FullName);
        if (size > 0) r.TotalBytes += size;
    }

    private static void AddDirectory(ScanResult r, DirectoryInfo di, InputZipType input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        FileInfo[] files;
        DirectoryInfo[] dirs;
        try
        {
            files = di.GetFiles();
            dirs = di.GetDirectories();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // unreadable folder: skip it, like the rest of the scan would
        }

        Array.Sort(files, (x, y) => string.Compare(x.FullName, y.FullName, StringComparison.Ordinal));
        foreach (FileInfo f in files) AddFile(r, f, input);

        Array.Sort(dirs, (x, y) => string.Compare(x.FullName, y.FullName, StringComparison.Ordinal));
        foreach (DirectoryInfo d in dirs) AddDirectory(r, d, input, ct);
    }

    private static long SafeLength(FileInfo f)
    {
        try { return f.Length; } catch { return -1; }
    }

    private static long DirSize(DirectoryInfo di, CancellationToken ct)
    {
        long total = 0;
        try
        {
            foreach (FileInfo fi in di.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                ct.ThrowIfCancellationRequested();
                total += fi.Length;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        return total;
    }
}
