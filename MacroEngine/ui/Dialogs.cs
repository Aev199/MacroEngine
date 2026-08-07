using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MacroEngine.UI;

/// <summary>Simple OK message dialog (Avalonia has no built-in MessageBox).</summary>
internal static class MessageDialog
{
    public static async Task Show(Window owner, string title, string message)
    {
        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var win = new Window
        {
            Title = $"MacroEngine — {title}",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 520,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Icon = AppIcon.Get(),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok
                }
            }
        };

        ok.Click += (_, _) => win.Close();
        await win.ShowDialog(owner);
    }
}

/// <summary>Yes/No confirmation dialog. Returns true when confirmed.</summary>
internal static class ConfirmDialog
{
    public static async Task<bool> Show(Window owner, string message,
                                        string yes = "Да", string no = "Отмена")
    {
        var btnYes = new Button { Content = yes, Width = 110, IsDefault = true, Classes = { "accent" } };
        var btnNo = new Button { Content = no, Width = 110, IsCancel = true };

        var win = new Window
        {
            Title = "MacroEngine — Подтверждение",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 520,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Icon = AppIcon.Get(),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { btnYes, btnNo }
                    }
                }
            }
        };

        btnYes.Click += (_, _) => win.Close(true);
        btnNo.Click += (_, _) => win.Close(false);
        return await win.ShowDialog<bool>(owner);
    }
}

internal enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}

/// <summary>
/// Three-way close guard. Closing the dialog itself is always Cancel, never an
/// implicit discard of user edits.
/// </summary>
internal static class UnsavedChangesDialog
{
    public static async Task<UnsavedChangesChoice> Show(Window owner)
    {
        var save = new Button
        {
            Content = "Сохранить",
            Width = 110,
            IsDefault = true,
            Classes = { "accent" }
        };
        var discard = new Button { Content = "Не сохранять", Width = 120 };
        var cancel = new Button { Content = "Отмена", Width = 100, IsCancel = true };

        var win = new Window
        {
            Title = "MacroEngine — Несохранённые изменения",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 560,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Icon = AppIcon.Get(),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Есть несохранённые изменения. Сохранить их перед закрытием?",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { save, discard, cancel }
                    }
                }
            }
        };

        save.Click += (_, _) => win.Close(UnsavedChangesChoice.Save);
        discard.Click += (_, _) => win.Close(UnsavedChangesChoice.Discard);
        cancel.Click += (_, _) => win.Close(UnsavedChangesChoice.Cancel);

        UnsavedChangesChoice? choice = await win.ShowDialog<UnsavedChangesChoice?>(owner);
        return choice ?? UnsavedChangesChoice.Cancel;
    }
}
