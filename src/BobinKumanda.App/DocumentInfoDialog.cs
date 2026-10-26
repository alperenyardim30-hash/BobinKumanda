using System.Windows;
using System.Windows.Controls;
using BobinKumanda.Core.Localization;
using BobinKumanda.Core.Model;

namespace BobinKumanda.App;

/// <summary>Antet bilgileri: devre adı, öğrenci, numara, sınıf, okul, öğretmen, tarih ve notlar.</summary>
internal static class DocumentInfoDialog
{
    /// <summary>Tamam'a basılırsa yeni bilgiler, vazgeçilirse null.</summary>
    public static TitleBlock? Ask(Window owner, TitleBlock current)
    {
        var fields = new (string Key, string Value)[]
        {
            ("title.circuit", current.CircuitName),
            ("title.student", current.StudentName),
            ("title.number", current.StudentNumber),
            ("title.class", current.ClassName),
            ("title.school", current.School),
            ("title.teacher", current.Teacher),
            ("title.date", current.Date.Length > 0 ? current.Date : DateTime.Today.ToString("d", Loc.Culture)),
            ("title.notes", current.Notes)
        };
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        var boxes = new TextBox[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock { Text = Loc.T(fields[i].Key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 12, 3) };
            bool notes = fields[i].Key == "title.notes";
            var box = new TextBox
            {
                Text = fields[i].Value,
                Padding = new Thickness(4),
                Margin = new Thickness(0, 3, 0, 3),
                AcceptsReturn = notes,
                TextWrapping = notes ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = notes ? 54 : 0
            };
            Grid.SetRow(label, i);
            Grid.SetRow(box, i);
            Grid.SetColumn(box, 1);
            grid.Children.Add(label);
            grid.Children.Add(box);
            boxes[i] = box;
        }
        var ok = new Button { Content = Loc.T("ui.ok"), IsDefault = true, MinWidth = 84, Padding = new Thickness(10, 4, 10, 4) };
        var cancel = new Button { Content = Loc.T("ui.cancel"), IsCancel = true, MinWidth = 84, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = Loc.T("ui.docInfoText"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(grid);
        panel.Children.Add(buttons);
        var w = new Window
        {
            Title = Loc.T("ui.docInfo"),
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
        w.Loaded += (_, _) => boxes[0].Focus();
        if (w.ShowDialog() != true)
        {
            return null;
        }
        return new TitleBlock
        {
            CircuitName = boxes[0].Text.Trim(),
            StudentName = boxes[1].Text.Trim(),
            StudentNumber = boxes[2].Text.Trim(),
            ClassName = boxes[3].Text.Trim(),
            School = boxes[4].Text.Trim(),
            Teacher = boxes[5].Text.Trim(),
            Date = boxes[6].Text.Trim(),
            Notes = boxes[7].Text.Trim()
        };
    }
}
