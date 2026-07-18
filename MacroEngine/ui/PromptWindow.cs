using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MacroEngine.UI;

/// <summary>
/// Small prompt used to resolve {input:…} and {choice:…} tokens during expansion.
/// The expander runs on an STA worker thread, so the static helpers marshal
/// onto the Avalonia UI thread and block the worker until the user answers.
/// Auto-cancels after 60 seconds of inactivity (countdown shown in the title).
/// </summary>
internal sealed class PromptWindow : Window
{
    private readonly TaskCompletionSource<string> _result = new();
    private readonly TextBox? _text;
    private readonly ComboBox? _combo;
    private readonly DispatcherTimer _countdown;
    private int _remaining = 60;
    private bool _completed;

    private PromptWindow(string label, string[]? choices)
    {
        Title = "MacroEngine (60с)";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;
        Icon = AppIcon.Get();

        var lbl = new TextBlock
        {
            Text = label,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        Control input;
        if (choices != null)
        {
            _combo = new ComboBox
            {
                ItemsSource = choices,
                SelectedIndex = choices.Length > 0 ? 0 : -1,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            input = _combo;
        }
        else
        {
            _text = new TextBox();
            input = _text;
        }

        var ok = new Button { Content = "OK", Width = 90, IsDefault = true };
        var cancel = new Button { Content = "Отмена", Width = 90, IsCancel = true };
        ok.Click += (_, _) => Complete(CurrentValue());
        cancel.Click += (_, _) => Complete("");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel }
        };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children = { lbl, input, buttons }
        };

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            if (--_remaining <= 0) { Complete(""); return; }
            Title = $"MacroEngine ({_remaining}с)";
        };
        _countdown.Start();

        Activated += (_, _) => _remaining = 60;
        Opened += (_, _) => (input as TextBox)?.Focus();
        Closed += (_, _) => Complete("");
    }

    private string CurrentValue() =>
        _combo != null ? _combo.SelectedItem?.ToString() ?? "" : _text?.Text ?? "";

    private void Complete(string value)
    {
        if (_completed) return;
        _completed = true;
        _countdown.Stop();
        _result.TrySetResult(value);
        Close();
    }

    // ── Static API (called from expander worker threads) ────────────

    /// <summary>Ask for free text. Returns "" if cancelled.</summary>
    public static string AskText(string label) => Run(label, null);

    /// <summary>Ask to pick one of <paramref name="choices"/>. Returns "" if cancelled.</summary>
    public static string AskChoice(string label, string[] choices) => Run(label, choices);

    private static string Run(string label, string[]? choices)
    {
        if (Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("PromptWindow must not be awaited from the UI thread.");

        var task = Dispatcher.UIThread.InvokeAsync(() =>
        {
            var w = new PromptWindow(label, choices);
            w.Show();
            w.Activate();
            return w._result.Task;
        });

        return task.GetAwaiter().GetResult();
    }
}
