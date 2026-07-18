using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Click-through HUD in the bottom-right corner of the primary screen.
/// Shows leader-sequence progress, matched confirmations, and short toasts.
/// Never takes focus; hides itself on a timer.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly Color AccentBlue  = Color.FromRgb(160, 200, 255);
    private static readonly Color AccentGreen = Color.FromRgb(100, 240, 140);

    private readonly TextBlock _text;
    private readonly DispatcherTimer _hideTimer;

    public OverlayWindow()
    {
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        _text = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(AccentBlue),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(225, 30, 30, 34)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 10),
            MinWidth = 220,
            Child = _text
        };

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); HideOverlay(); };

        Opened += (_, _) => ApplyClickThroughStyles();
    }

    /// <summary>Leader chord engaged — show accumulated sequence.</summary>
    public void ShowLeader(string mods, string seq) =>
        ShowText(seq.Length > 0 ? $"{mods} → {seq}…" : $"{mods} → …", AccentBlue, 1500);

    /// <summary>Leader trigger fired.</summary>
    public void ShowMatched(string mods, string seq) =>
        ShowText($"✓ {mods} → {seq}", AccentGreen, 600);

    /// <summary>General-purpose toast (config reloaded, first run, …).</summary>
    public void ShowToast(string message, int ms = 2500) =>
        ShowText(message, AccentBlue, ms);

    public void HideOverlay()
    {
        _hideTimer.Stop();
        if (IsVisible) Hide();
    }

    private void ShowText(string text, Color color, int hideAfterMs)
    {
        _text.Text = text;
        _text.Foreground = new SolidColorBrush(color);

        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(hideAfterMs);
        _hideTimer.Start();

        if (!IsVisible) Show();
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var screen = Screens.Primary?.WorkingArea;
        if (screen is null) return;

        // FrameSize is unknown until layout — measure the content instead.
        UpdateLayout();
        var size = PixelSize.FromSize(ClientSize, Screens.Primary!.Scaling);
        Position = new PixelPoint(
            screen.Value.Right - size.Width - 16,
            screen.Value.Bottom - size.Height - 16);
    }

    /// <summary>WS_EX_TRANSPARENT + LAYERED = clicks pass through; TOOLWINDOW hides from Alt+Tab.</summary>
    private void ApplyClickThroughStyles()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        var ex = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TRANSPARENT
            | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
    }
}
