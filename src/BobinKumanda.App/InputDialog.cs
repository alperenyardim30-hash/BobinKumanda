using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BobinKumanda.Core.Localization;

namespace BobinKumanda.App;

/// <summary>Tek satırlık yazı soran küçük pencere (sayfa adı gibi).</summary>
internal static class InputDialog
{
    public static string? Ask(Window owner, string title, string prompt, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 14), Padding = new Thickness(4), MinWidth = 280 };
        var ok = new Button { Content = Loc.T("ui.ok"), IsDefault = true, MinWidth = 84, Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = Loc.T("ui.cancel"), IsCancel = true, MinWidth = 84, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var w = new Window
        {
            Title = title,
            Content = panel,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        w.SetResourceReference(Window.BackgroundProperty, "App.Panel");
        w.SetResourceReference(Control.ForegroundProperty, "App.Text");
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
            Keyboard.Focus(box);
        };
        return w.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
