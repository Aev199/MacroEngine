using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MacroEngine.UI;

/// <summary>
/// Small prompt used to resolve {input:...} and {choice:...} tokens during expansion.
/// Calls are marshalled from the expansion worker onto Avalonia's UI thread.
/// </summary>
internal sealed class PromptWindow : Window
{
    private const int TimeoutSeconds = 60;
    private const int VisibleCountdownSeconds = 10;

    private readonly TaskCompletionSource<string> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TextBox? _text;
    private readonly ComboBox? _combo;
    private readonly TextBlock _timeoutText;
    private readonly DispatcherTimer _countdown;
    private readonly CancellationTokenRegistration _cancellationRegistration;

    private int _remaining = TimeoutSeconds;
    private bool _completed;

    private PromptWindow(string label, string[]? choices, CancellationToken cancellationToken)
    {
        Title = "MacroEngine";
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

        _timeoutText = new TextBlock
        {
            FontSize = 11,
            Opacity = 0.55,
            IsVisible = false,
            VerticalAlignment = VerticalAlignment.Center
        };

        var ok = new Button
        {
            Content = "Продолжить",
            MinWidth = 96,
            IsDefault = true,
            Classes = { "accent" }
        };
        var cancel = new Button { Content = "Отмена", MinWidth = 88, IsCancel = true };
        ok.Click += (_, _) => Complete(CurrentValue());
        cancel.Click += (_, _) => CancelPrompt("Ввод отменён пользователем.");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel }
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto")
        };
        footer.Children.Add(_timeoutText);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children = { caption, input, footer }
        };

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            if (--_remaining <= 0)
            {
                CancelPrompt("Время ожидания ввода истекло.");
                return;
            }

            UpdateTimeoutStatus();
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
        Closed += (_, _) => CancelPrompt("Ввод отменён пользователем.", closeWindow: false);
    }

    private string CurrentValue() =>
        _combo != null ? _combo.SelectedItem?.ToString() ?? "" : _text?.Text ?? "";

    private void ResetCountdown()
    {
        _remaining = TimeoutSeconds;
        UpdateTimeoutStatus();
    }

    private void UpdateTimeoutStatus()
    {
        _timeoutText.IsVisible = _remaining <= VisibleCountdownSeconds;
        _timeoutText.Text = _timeoutText.IsVisible
            ? $"Закроется через {_remaining} с"
            : "";
    }

    private void Complete(string value)
    {
        if (_completed) return;

        _completed = true;
        _countdown.Stop();
        _cancellationRegistration.Dispose();
        _result.TrySetResult(value);
        Close();
    }

    private void CancelPrompt(string reason, bool closeWindow = true)
    {
        if (_completed) return;

        _completed = true;
        _countdown.Stop();
        _cancellationRegistration.Dispose();
        _result.TrySetException(new OperationCanceledException(reason));
        if (closeWindow)
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
