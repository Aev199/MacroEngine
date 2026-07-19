using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MacroEngine.UI;

/// <summary>
/// Small prompt used to resolve {input:…} and {choice:…} tokens during expansion.
/// Calls are marshalled from the expansion worker onto Avalonia's UI thread.
/// The prompt closes after 60 seconds without user activity or immediately when
/// the running automation is cancelled.
/// </summary>
internal sealed class PromptWindow : Window
{
    private const int TimeoutSeconds = 60;

    private readonly TaskCompletionSource<string> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox? _text;
    private readonly ComboBox? _combo;
    private readonly DispatcherTimer _countdown;
    private readonly CancellationTokenRegistration _cancellationRegistration;

    private int _remaining = TimeoutSeconds;
    private bool _completed;

    private PromptWindow(string label, string[]? choices, CancellationToken cancellationToken)
    {
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;
        Icon = AppIcon.Get();

        var caption = new TextBlock
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
            _combo.SelectionChanged += (_, _) => ResetCountdown();
            input = _combo;
        }
        else
        {
            _text = new TextBox();
            _text.TextChanged += (_, _) => ResetCountdown();
            input = _text;
        }

        input.PointerPressed += (_, _) => ResetCountdown();
        input.KeyDown += (_, _) => ResetCountdown();

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
            Children = { caption, input, buttons }
        };

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            if (--_remaining <= 0)
            {
                Complete("");
                return;
            }

            UpdateTitle();
        };

        _cancellationRegistration = cancellationToken.Register(() =>
        {
            _result.TrySetCanceled(cancellationToken);
            Dispatcher.UIThread.Post(CloseAfterCancellation);
        });

        ResetCountdown();
        _countdown.Start();

        Activated += (_, _) => ResetCountdown();
        Opened += (_, _) => input.Focus();
        Closed += (_, _) => Complete("");
    }

    private string CurrentValue() =>
        _combo != null ? _combo.SelectedItem?.ToString() ?? "" : _text?.Text ?? "";

    private void ResetCountdown()
    {
        _remaining = TimeoutSeconds;
        UpdateTitle();
    }

    private void UpdateTitle() => Title = $"MacroEngine ({_remaining}с)";

    private void Complete(string value)
    {
        if (_completed) return;

        _completed = true;
        _countdown.Stop();
        _cancellationRegistration.Dispose();
        _result.TrySetResult(value);
        Close();
    }

    private void CloseAfterCancellation()
    {
        if (_completed) return;

        _completed = true;
        _countdown.Stop();
        _cancellationRegistration.Dispose();
        Close();
    }

    public static string AskText(string label, CancellationToken cancellationToken = default) =>
        Run(label, null, cancellationToken);

    public static string AskChoice(
        string label,
        string[] choices,
        CancellationToken cancellationToken = default) =>
        Run(label, choices, cancellationToken);

    private static string Run(
        string label,
        string[]? choices,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("PromptWindow must not block the UI thread.");

        var operation = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = new PromptWindow(label, choices, cancellationToken);
            window.Show();
            window.Activate();
            return await window._result.Task.ConfigureAwait(false);
        });

        return operation.GetAwaiter().GetResult();
    }
}
