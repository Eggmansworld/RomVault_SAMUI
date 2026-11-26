using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SAM_UI_Avalonia.Views;

/// <summary>Small themed Yes / No dialog with an optional "don't ask again" box.</summary>
public sealed class ConfirmDialog : Window
{
    private bool _result;
    private readonly CheckBox _dontAsk;

    private ConfirmDialog(string title, string message, string? dontAskText)
    {
        Title = "SAM-UI // " + title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        _dontAsk = new CheckBox { Content = dontAskText ?? "", IsVisible = dontAskText != null, Margin = new Thickness(0, 4, 0, 0) };

        var yes = new Button { Content = "YES", MinWidth = 100, IsDefault = true };
        var no = new Button { Content = "NO", MinWidth = 100, IsCancel = true };
        yes.Classes.Add("kg"); yes.Classes.Add("go");
        no.Classes.Add("kg");
        yes.Click += (_, _) => { _result = true; Close(); };
        no.Click += (_, _) => { _result = false; Close(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } };
        var header = new TextBlock { Text = "══[ " + title + " ]══" };
        header.Classes.Add("h");

        Content = new Border
        {
            Padding = new Thickness(20, 16),
            Child = new StackPanel { Spacing = 12, Children = { header, text, _dontAsk, buttons } },
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Y) { _result = true; Close(); }
            else if (e.Key == Key.N) { _result = false; Close(); }
        };
    }

    /// <summary>
    /// Several buttons: returns the index of the one clicked, or -1 if the window was closed / Esc.
    /// The first button is the default (Enter).
    /// </summary>
    public static async Task<int> Choose(Window owner, string title, string message, params string[] buttons)
    {
        int choice = -1;
        var dlg = new Window
        {
            Title = "SAM-UI // " + title,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Icon = owner.Icon,
            Topmost = true,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var b = new Button { Content = buttons[i], MinWidth = 110, IsDefault = i == 0 };
            b.Classes.Add("kg");
            if (i == 0) b.Classes.Add("go");
            b.Click += (_, _) => { choice = idx; dlg.Close(); };
            row.Children.Add(b);
        }
        var header = new TextBlock { Text = "══[ " + title + " ]══" };
        header.Classes.Add("h");
        dlg.Content = new Border
        {
            Padding = new Thickness(20, 16),
            Child = new StackPanel
            {
                Spacing = 12,
                Children = { header, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13 }, row },
            },
        };
        dlg.KeyDown += (_, e) => { if (e.Key == Key.Escape) dlg.Close(); };
        await dlg.ShowDialog(owner);
        return choice;
    }

    /// <returns>(answer, "don't ask again" ticked)</returns>
    public static async Task<(bool Ok, bool DontAsk)> Ask(Window owner, string title, string message, string? dontAskText = null)
    {
        var dlg = new ConfirmDialog(title, message, dontAskText) { Icon = owner.Icon };
        await dlg.ShowDialog(owner);
        return (dlg._result, dlg._result && dlg._dontAsk.IsChecked == true);
    }
}
