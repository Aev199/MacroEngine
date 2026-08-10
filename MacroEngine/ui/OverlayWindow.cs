using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Compact click-through status HUD in the bottom-right corner of the primary screen.
/// It is intentionally quiet: one line, little color and a short lifetime.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private static readonly Color NeutralText = Color.FromRgb(218, 218, 218);
    private static readonly Color MatchedText = Color.FromRgb(139, 211, 194);

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
        MaxWidth = 430;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        _text = new TextBlock
        {
            FontSize = 13,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            FontWeight = FontWeight.Medium,
            Foreground = new SolidColorBrush(NeutralText),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 390
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(238, 24, 24, 27)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 6),
            MinWidth = 140,
            Child = _text
        };

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            HideOverlay();
        };

        Opened += (_, _) => ApplyClickThroughStyles();
    }

    public void ShowLeader(string mods, string seq) =>
        ShowText(seq.Length > 0 ? $"{mods}  {seq}..." : $"{mods}  ...", NeutralText, 1100);

    public void ShowMatched(string mods, string seq) =>
        ShowText($"{mods}  {seq}", MatchedText, 650);

    public void ShowToast(string message, int ms = 1800) =>
        ShowText(message, NeutralText, ms);

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
        _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(250, hideAfterMs));
        _hideTimer.Start();

        if (!IsVisible) Show();
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var primary = Screens.Primary;
        var workingArea = primary?.WorkingArea;
        if (primary is null || workingArea is null) return;

        UpdateLayout();
        var size = PixelSize.FromSize(ClientSize, primary.Scaling);
        Position = new PixelPoint(
            workingArea.Value.Right - size.Width - 12,
            workingArea.Value.Bottom - size.Height - 12);
    }

    private void ApplyClickThroughStyles()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;

        long exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TRANSPARENT
            | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
    }
}
