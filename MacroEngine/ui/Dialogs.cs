using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MacroEngine.UI;

/// <summary>Compact one-action message dialog.</summary>
internal static class MessageDialog
{
    public static async Task Show(Window owner, string title, string message)
    {
        var close = new Button
        {
            Content = "Закрыть",
            MinWidth = 88,
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
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    close
                }
            }
        };

        close.Click += (_, _) => win.Close();
        await win.ShowDialog(owner);
    }
}

/// <summary>Two-action confirmation dialog. Returns true when confirmed.</summary>
internal static class ConfirmDialog
{
    public static async Task<bool> Show(
        Window owner,
        string message,
        string yes = "Да",
        string no = "Отмена")
    {
        var btnYes = new Button
        {
            Content = yes,
            MinWidth = 96,
            IsDefault = true,
            Classes = { "accent" }
        };
        var btnNo = new Button { Content = no, MinWidth = 88, IsCancel = true };

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
                Margin = new Thickness(16),
                Spacing = 14,
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
            MinWidth = 96,
            IsDefault = true,
            Classes = { "accent" }
        };
        var discard = new Button { Content = "Не сохранять", MinWidth = 112 };
        var cancel = new Button { Content = "Отмена", MinWidth = 88, IsCancel = true };

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
                Margin = new Thickness(16),
                Spacing = 14,
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
