using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SAM_UI_Avalonia.Audio;
using SAM_UI_Avalonia.Core;
using SAM_UI_Avalonia.Fx;
using SAM_UI_Avalonia.ViewModels;
using SAM_UI_Avalonia.Views;

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

                Jukebox = new Jukebox(Config);
                VuMeter.LevelSource = () => Jukebox.Level;

                var vm = new MainWindowViewModel(Config);
                desktop.MainWindow = new MainWindow { DataContext = vm };

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
    }
}
