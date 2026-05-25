using System.Windows;
using System.Windows.Controls;

namespace MyEfVibe.VisualStudio.ToolWindows;

internal sealed class InputDialog : Window
{
    private readonly TextBox _textBox;

    private InputDialog(string title, string prompt)
    {
        Title = title;
        Width = 420;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });

        _textBox = new TextBox { MinWidth = 360 };
        root.Children.Add(_textBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;
    }

    internal static string? Prompt(string title, string prompt)
    {
        var dialog = new InputDialog(title, prompt);
        return dialog.ShowDialog() == true ? dialog._textBox.Text.Trim() : null;
    }
}
