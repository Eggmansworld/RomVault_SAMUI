using Avalonia;
using System;
using SAM_UI_Avalonia.Core;

namespace SAM_UI_Avalonia
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // "--jukebox toggle|next" comes from the taskbar jump list: pass it to the running
            // SAM-UI and quit without opening a window.
            if (args.Length >= 2 && args[0] == "--jukebox")
            {
                InstanceChannel.Send("jukebox " + args[1]);
                return;
            }

            // Diagnostic: "--tune-levels <out.txt>" measures every tune in the Tunes folder.
            if (args.Length >= 2 && args[0] == "--tune-levels")
            {
                var lines = new List<string> { "tune\trms\tpeak\tgain\tlevel after (rms)" };
                foreach (string f in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Tunes")).OrderBy(x => x))
                {
                    try
                    {
                        var (rms, peak) = Audio.Loudness.Measure(f, 0);
                        double g = Audio.Loudness.GainFor(f, 0);
                        lines.Add($"{Path.GetFileName(f)}\t{rms:0}\t{peak}\t{g:0.00}\t{rms * g:0}");
                    }
                    catch (Exception ex) { lines.Add($"{Path.GetFileName(f)}\tFAILED {ex.Message}"); }
                }
                File.WriteAllLines(args[1], lines);
                return;
            }

            // SAM-UI already open? Add these files/folders to its list (or just bring it to the
            // front) instead of opening a second window with a second set of workers.
            if (InstanceChannel.OtherInstanceRunning())
            {
                string[] paths = args.Where(a => File.Exists(a) || Directory.Exists(a)).Select(Path.GetFullPath).ToArray();
                if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(-1); // let it come to the front
                if (InstanceChannel.Send(paths.Length > 0 ? "stage|" + string.Join("|", paths) : "show"))
                    return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
