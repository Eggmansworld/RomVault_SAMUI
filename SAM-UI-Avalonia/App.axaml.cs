using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SAM_UI_Avalonia.Audio;
using SAM_UI_Avalonia.Core;
using SAM_UI_Avalonia.Fx;
using SAM_UI_Avalonia.ViewModels;
using SAM_UI_Avalonia.Views;
using System;
using System.IO;
using System.Linq;

namespace SAM_UI_Avalonia
{
    public partial class App : Application
    {
        public static SamConfig Config { get; private set; } = new();
        public static Jukebox Jukebox { get; private set; } = null!;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Config = SamConfig.Load();
                Skin.Apply(Config.Theme);
                FxClock.Enabled = Config.Effects;
                VectorBackdrop.Mode = Config.Shape;

                FxSettings.Load(Config);

                Jukebox = new Jukebox(Config);
                VuMeter.LevelSource = () => Jukebox.Level;

                // Taskbar right-click: "Pause music" / "Next tune".
                if (OperatingSystem.IsWindows())
                {
                    JumpList.Update(false);
                    Jukebox.PlayingChanged += playing => { if (OperatingSystem.IsWindows()) JumpList.Update(playing); };
                }
                var vm = new MainWindowViewModel(Config);
                desktop.MainWindow = new MainWindow { DataContext = vm };

                // Commands from a second launch: taskbar music tasks, files from Explorer
                // "Send To" / a shortcut, or just "come to the front".
                InstanceChannel.Listen(cmd => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (cmd == "jukebox toggle") Jukebox.TogglePlay();
                    else if (cmd == "jukebox next") Jukebox.Next();
                    else if (cmd == "show") BringToFront(desktop.MainWindow);
                    else if (cmd.StartsWith("stage|", StringComparison.Ordinal))
                        AddFromSecondLaunch(desktop.MainWindow, vm, cmd.Substring(6).Split('|', StringSplitOptions.RemoveEmptyEntries));
                }));

                // Paths on the command line (e.g. from an Explorer "Send To" shortcut) are
                // staged, never started.
                string[] paths = (desktop.Args ?? Array.Empty<string>())
                    .Where(a => File.Exists(a) || Directory.Exists(a)).ToArray();
                if (paths.Length > 0)
                    vm.Stage(paths);
                desktop.Exit += (_, _) =>
                {
                    Jukebox.Shutdown();
                    Config.Save();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        /// <summary>
        /// Files from a second launch. If there's already work in the list, ask where they go:
        /// TOP (processed next - straight into a running job, ahead of what's waiting) or BOTTOM.
        /// </summary>
        private static async void AddFromSecondLaunch(Avalonia.Controls.Window? w, MainWindowViewModel vm, string[] paths)
        {
            BringToFront(w);
            if (w == null || paths.Length == 0) return;
            if (!vm.IsRunning && !vm.HasPending)
            {
                vm.Stage(paths);
                return;
            }

            string what = paths.Length == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : $"{paths.Length} files / folders";
            string msg = vm.IsRunning
                ? $"A job is running. Where should {what} go?\n\nTOP: processed next, as soon as a worker is free.\nBOTTOM: after everything already in the queue."
                : $"Add {what} to the TOP or the BOTTOM of the list?";
            int c = await ConfirmDialog.Choose(w, "ADD FILES", msg, "TOP (NEXT UP)", "BOTTOM", "CANCEL");
            if (c is 0 or 1) vm.Stage(paths, atTop: c == 0, intoRunningJob: true);
        }

        private static void BringToFront(Avalonia.Controls.Window? w)
        {
            if (w == null) return;
            if (w.WindowState == Avalonia.Controls.WindowState.Minimized)
                w.WindowState = Config.WindowMaximized ? Avalonia.Controls.WindowState.Maximized : Avalonia.Controls.WindowState.Normal;
            w.Activate();
        }
    }
}
