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

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

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
