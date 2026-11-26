using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SAM_UI_Avalonia.Views;

/// <summary>Theme, effect layers and jukebox options. Every change applies and saves at once.</summary>
public partial class SetupWindow : Window
{
    public SetupWindow()
    {
        InitializeComponent();
        Icon = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Icon;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
