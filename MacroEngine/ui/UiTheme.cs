using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace MacroEngine.UI;

/// <summary>
/// Visual language shared with the GeoBase / plx-construct / SoilStiffness UIs
/// (web/src/styles/tokens.css, docs/migration/DESIGN_SPEC.md): light theme only,
/// blue-grey Material palette, compact "engineering" density, flat surfaces,
/// underline tabs, notes with a coloured left bar, snackbars instead of modal
/// confirmations. Fluent stays the control base; its light palette is remapped
/// onto these tokens and a few style classes add the GeoBase patterns.
/// </summary>
internal static class UiTheme
{
    // ── Surfaces ────────────────────────────────────────────────────
    public static readonly Color PageBg      = Color.Parse("#FAFAFA");
    public static readonly Color PanelBg     = Color.Parse("#FFFFFF");
    public static readonly Color SectionBg   = Color.Parse("#ECEFF1");
    public static readonly Color Border      = Color.Parse("#CFD8DC");
    public static readonly Color BorderHover = Color.Parse("#B0BEC5");
    public static readonly Color HoverBg     = Color.Parse("#F5F7F8");
    public static readonly Color Divider     = Color.Parse("#ECEFF1");

    // ── Accent ──────────────────────────────────────────────────────
    public static readonly Color Accent       = Color.Parse("#1976D2");
    public static readonly Color AccentBtn    = Color.Parse("#1E88E5");
    public static readonly Color AccentChipBg = Color.Parse("#BBDEFB");
    public static readonly Color AccentSoft   = Color.Parse("#E3F2FD");
    public static readonly Color AccentBorder = Color.Parse("#90CAF9");

    // ── Text ────────────────────────────────────────────────────────
    public static readonly Color Text      = Color.Parse("#263238");
    public static readonly Color TextMuted = Color.Parse("#455A64");
    public static readonly Color TextGrey  = Color.Parse("#616161");

    // ── Status ──────────────────────────────────────────────────────
    public static readonly Color Success     = Color.Parse("#388E3C");
    public static readonly Color SuccessBg   = Color.Parse("#E8F5E9");
    public static readonly Color Warning     = Color.Parse("#F57C00");
    public static readonly Color WarningBg   = Color.Parse("#FFF3E0");
    public static readonly Color Error       = Color.Parse("#D32F2F");
    public static readonly Color ErrorBg     = Color.Parse("#FFEBEE");
    public static readonly Color ErrorBorder = Color.Parse("#FFCDD2");
    public static readonly Color ErrorSnack  = Color.Parse("#EF5350");

    public static readonly FontFamily Font = new("Roboto, Segoe UI, Inter, sans-serif");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, DejaVu Sans Mono, monospace");

    // Font sizes in px, as in the spec.
    public const double FsTitle = 16, FsPanel = 15, FsBody = 13, FsLabel = 12, FsCaption = 11;

    public static IBrush B(Color c) => new SolidColorBrush(c);

    /// <summary>Install Fluent (light) with the GeoBase palette and style classes.</summary>
    public static void Apply(Application app)
    {
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources
        {
            Accent = AccentBtn,
            RegionColor = PageBg,
            ErrorText = Error,
            BaseHigh = Text,
            BaseMediumHigh = TextMuted,
            BaseMedium = TextGrey,
            BaseMediumLow = BorderHover,
            BaseLow = Border,
            AltHigh = PanelBg,
            ChromeLow = HoverBg,
            ChromeMediumLow = HoverBg,
            ChromeMedium = SectionBg,
            ChromeHigh = Border,
            ListLow = HoverBg,
            ListMedium = AccentSoft
        };
        app.Styles.Add(fluent);
        app.Styles.Add(new StyleInclude(new Uri("avares://MacroEngine"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });
        app.RequestedThemeVariant = ThemeVariant.Light;

        var r = app.Resources;
        r["ControlCornerRadius"] = new CornerRadius(4);
        r["OverlayCornerRadius"] = new CornerRadius(6);
        r["ContentControlThemeFontFamily"] = Font;
        r["ControlContentThemeFontSize"] = FsBody;
        r["TextControlBorderBrush"] = B(Border);
        r["TextControlBorderBrushPointerOver"] = B(BorderHover);
        r["TextControlBorderBrushFocused"] = B(Accent);
        r["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        r["TextControlBackgroundFocused"] = B(PanelBg);
        r["ListBoxItemBackgroundSelected"] = B(AccentSoft);
        r["ListBoxItemBackgroundSelectedPointerOver"] = B(AccentSoft);
        r["ListBoxItemBackgroundSelectedPressed"] = B(AccentChipBg);
        r["ListBoxItemBackgroundPointerOver"] = B(HoverBg);
        r["ListBoxItemForegroundSelected"] = B(Text);
        r["ComboBoxBorderBrush"] = B(Border);
        r["ComboBoxBorderBrushPointerOver"] = B(BorderHover);

        app.Styles.Add(BuildStyles());
    }

    private static Styles BuildStyles()
    {
        var s = new Styles();

        s.Add(Set<Window>(x => x.OfType<Window>(),
            (TopLevel.FontFamilyProperty, Font), (TopLevel.FontSizeProperty, FsBody),
            (TemplatedControl.BackgroundProperty, B(PageBg)), (TemplatedControl.ForegroundProperty, B(Text))));

        // ── Buttons (.gb-btn) ───────────────────────────────────────
        s.Add(Set<Button>(x => x.OfType<Button>(),
            (TemplatedControl.FontSizeProperty, FsLabel), (TemplatedControl.PaddingProperty, new Thickness(11, 6)),
            (Layoutable.MinHeightProperty, 30.0), (TemplatedControl.BackgroundProperty, B(PanelBg)),
            (TemplatedControl.BorderBrushProperty, B(Border)), (TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            (Avalonia.Input.InputElement.CursorProperty, new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand))));
        AddButtonStates(s, x => x.OfType<Button>(), PanelBg, Text, Border, HoverBg, BorderHover, Divider);
        AddButtonStates(s, x => x.OfType<Button>().Class("primary"), AccentBtn, Colors.White, AccentBtn, Accent, Accent, Accent);
        AddButtonStates(s, x => x.OfType<Button>().Class("danger"), PanelBg, Error, ErrorBorder, ErrorBg, ErrorBorder, ErrorBorder);
        AddButtonStates(s, x => x.OfType<Button>().Class("ghost"), Colors.Transparent, TextMuted, Colors.Transparent, HoverBg, Colors.Transparent, Divider);
        AddButtonStates(s, x => x.OfType<Button>().Class("link"), Colors.Transparent, AccentBtn, Colors.Transparent, AccentSoft, Colors.Transparent, AccentChipBg);
        AddButtonStates(s, x => x.OfType<Button>().Class("onDark"), Colors.Transparent, Colors.White, Colors.Transparent,
            Color.FromArgb(40, 255, 255, 255), Colors.Transparent, Color.FromArgb(70, 255, 255, 255));
        s.Add(Set<Button>(x => x.OfType<Button>().Class("primary").Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            (ContentPresenter.BackgroundProperty, B(BorderHover)), (ContentPresenter.BorderBrushProperty, B(BorderHover)),
            (ContentPresenter.ForegroundProperty, B(Colors.White))));
        s.Add(Set<Button>(x => x.OfType<Button>().Class("sm"),
            (TemplatedControl.FontSizeProperty, FsCaption), (TemplatedControl.PaddingProperty, new Thickness(8, 4)),
            (Layoutable.MinHeightProperty, 26.0)));

        // ── Segmented toggles ───────────────────────────────────────
        s.Add(Set<ToggleButton>(x => x.OfType<ToggleButton>().Class("seg"),
            (TemplatedControl.FontSizeProperty, FsLabel), (TemplatedControl.PaddingProperty, new Thickness(12, 6)),
            (Layoutable.MinHeightProperty, 30.0), (TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            (Avalonia.Input.InputElement.CursorProperty, new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand))));
        AddButtonStates(s, x => x.OfType<ToggleButton>().Class("seg"), PanelBg, Text, Border, HoverBg, BorderHover, Divider);
        AddButtonStates(s, x => x.OfType<ToggleButton>().Class("seg").Class(":checked"), AccentSoft, Accent, AccentBorder, AccentSoft, AccentBorder, AccentChipBg);

        // ── Underline tabs (.gb-subtab / .tab--active) ─────────────
        s.Add(Set<ToggleButton>(x => x.OfType<ToggleButton>().Class("tab"),
            (TemplatedControl.FontSizeProperty, FsLabel), (TemplatedControl.PaddingProperty, new Thickness(12, 9, 12, 8)),
            (TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 2)), (TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
            (Avalonia.Input.InputElement.CursorProperty, new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand))));
        AddButtonStates(s, x => x.OfType<ToggleButton>().Class("tab"), Colors.Transparent, TextMuted, Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent, hoverText: Text);
        AddButtonStates(s, x => x.OfType<ToggleButton>().Class("tab").Class(":checked"), Colors.Transparent, Text, Accent, Colors.Transparent, Accent, Colors.Transparent);
        s.Add(Set<ToggleButton>(x => x.OfType<ToggleButton>().Class("tab").Class(":checked"),
            (TemplatedControl.FontWeightProperty, FontWeight.SemiBold)));

        // ── Lists: rows draw their own padding and selection bar ───
        s.Add(Set<ListBoxItem>(x => x.OfType<ListBox>().Class("rows").Descendant().OfType<ListBoxItem>(),
            (TemplatedControl.PaddingProperty, new Thickness(0)), (Layoutable.MinHeightProperty, 0.0)));
        foreach (string state in new[] { ":selected", ":selected:pointerover", ":selected:focus", ":selected:pressed" })
        {
            s.Add(Set<ListBoxItem>(x => Pseudo(x.OfType<ListBox>().Class("rows").Descendant().OfType<ListBoxItem>(), state)
                    .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                (ContentPresenter.BackgroundProperty, B(AccentSoft)), (ContentPresenter.ForegroundProperty, B(Text))));
        }
        s.Add(Set<Avalonia.Controls.Border>(x => x.OfType<ListBoxItem>().Class(":selected").Descendant().OfType<Avalonia.Controls.Border>().Class("selbar"),
            (Avalonia.Controls.Border.BackgroundProperty, B(Accent))));
        s.Add(Set<ListBox>(x => x.OfType<ListBox>().Class("rows"),
            (TemplatedControl.BackgroundProperty, B(PanelBg)), (TemplatedControl.BorderThicknessProperty, new Thickness(0))));

        // ── Inputs ─────────────────────────────────────────────────
        s.Add(Set<TextBox>(x => x.OfType<TextBox>(),
            (TemplatedControl.FontSizeProperty, FsBody), (Layoutable.MinHeightProperty, 30.0),
            (TemplatedControl.PaddingProperty, new Thickness(8, 5))));
        s.Add(Set<ComboBox>(x => x.OfType<ComboBox>(),
            (TemplatedControl.FontSizeProperty, FsBody), (Layoutable.MinHeightProperty, 30.0)));
        return s;
    }

    private static Selector Pseudo(Selector sel, string states)
    {
        foreach (string st in states.Split(':', StringSplitOptions.RemoveEmptyEntries))
            sel = sel.Class(":" + st);
        return sel;
    }

    private static Style Set<T>(Func<Selector?, Selector> selector, params (AvaloniaProperty Prop, object Value)[] setters)
    {
        var style = new Style(selector);
        foreach (var (p, v) in setters) style.Setters.Add(new Setter(p, v));
        return style;
    }

    /// <summary>Normal / hover / pressed colours for Fluent's button template.</summary>
    private static void AddButtonStates(Styles s, Func<Selector?, Selector> sel, Color bg, Color fg, Color border,
        Color hoverBg, Color hoverBorder, Color pressedBg, Color? hoverText = null)
    {
        Selector Presenter(Selector? x, string? pseudo)
        {
            var b = sel(x);
            if (pseudo != null) b = b.Class(pseudo);
            return b.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
        }
        s.Add(Set<Button>(x => sel(x), (TemplatedControl.BackgroundProperty, B(bg)), (TemplatedControl.ForegroundProperty, B(fg)),
            (TemplatedControl.BorderBrushProperty, B(border))));
        // Fluent paints :checked (and some other states) straight onto the presenter,
        // so the resting colours are pinned there as well.
        s.Add(Set<Button>(x => Presenter(x, null), (ContentPresenter.BackgroundProperty, B(bg)),
            (ContentPresenter.BorderBrushProperty, B(border)), (ContentPresenter.ForegroundProperty, B(fg))));
        s.Add(Set<Button>(x => Presenter(x, ":pointerover"), (ContentPresenter.BackgroundProperty, B(hoverBg)),
            (ContentPresenter.BorderBrushProperty, B(hoverBorder)), (ContentPresenter.ForegroundProperty, B(hoverText ?? fg))));
        s.Add(Set<Button>(x => Presenter(x, ":pressed"), (ContentPresenter.BackgroundProperty, B(pressedBg)),
            (ContentPresenter.BorderBrushProperty, B(hoverBorder)), (ContentPresenter.ForegroundProperty, B(fg))));
    }

    // ═══════════════════════════════════════════════════════════════
    //  Building blocks
    // ═══════════════════════════════════════════════════════════════

    public static Button Btn(string text, string? kind = null, bool small = false)
    {
        var b = new Button { Content = text };
        if (kind != null) b.Classes.Add(kind);
        if (small) b.Classes.Add("sm");
        return b;
    }

    /// <summary>Muted bold caption above a field ("Набранный текст", "Сочетание"…).</summary>
    public static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        FontSize = FsLabel,
        FontWeight = FontWeight.SemiBold,
        Foreground = B(TextMuted),
        Margin = new Thickness(0, 8, 0, 4)
    };

    public static TextBlock Caption(string text, Color? color = null) => new()
    {
        Text = text,
        FontSize = FsCaption,
        Foreground = B(color ?? TextMuted),
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>Section title with a divider above it.</summary>
    public static Control SectionHeader(string text) => new Border
    {
        BorderBrush = B(Divider),
        BorderThickness = new Thickness(0, 1, 0, 0),
        Margin = new Thickness(0, 14, 0, 4),
        Padding = new Thickness(0, 12, 0, 0),
        Child = new TextBlock { Text = text, FontSize = FsBody, FontWeight = FontWeight.SemiBold, Foreground = B(Text) }
    };

    /// <summary>White panel with a 1px border (.gb-panel).</summary>
    public static Border Panel(Control child) => new()
    {
        Background = B(PanelBg),
        BorderBrush = B(Border),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        ClipToBounds = true,
        Child = child
    };

    public static ToggleButton Seg(string text) { var t = new ToggleButton { Content = text }; t.Classes.Add("seg"); return t; }

    public static Color ToneColor(Tone tone) => tone switch
    {
        Tone.Success => Success,
        Tone.Warning => Warning,
        Tone.Error   => Error,
        _            => Border
    };
}

internal enum Tone { Neutral, Success, Warning, Error }

/// <summary>Text with a 2px tone bar on the left (.gb-note).</summary>
internal sealed class Note : Border
{
    private readonly TextBlock _text;

    public Note(string text = "", Tone tone = Tone.Neutral)
    {
        _text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = UiTheme.FsLabel, LineHeight = 17 };
        Child = _text;
        Padding = new Thickness(10, 5, 4, 5);
        Margin = new Thickness(0, 4, 0, 2);
        BorderThickness = new Thickness(2, 0, 0, 0);
        Set(text, tone);
    }

    public void Set(string text, Tone tone)
    {
        _text.Text = text;
        _text.Foreground = UiTheme.B(tone == Tone.Neutral ? UiTheme.TextMuted : UiTheme.Text);
        BorderBrush = UiTheme.B(UiTheme.ToneColor(tone));
        IsVisible = text.Length > 0;
    }
}

/// <summary>Program filter chip with a remove "×".</summary>
internal sealed class Chip : Border
{
    public event EventHandler? Removed;

    public Chip(string text, bool exclude)
    {
        var fore = exclude ? UiTheme.Error : UiTheme.Accent;
        Background = UiTheme.B(exclude ? UiTheme.ErrorBg : UiTheme.AccentSoft);
        BorderBrush = UiTheme.B(exclude ? UiTheme.ErrorBorder : UiTheme.AccentBorder);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        Margin = new Thickness(0, 0, 6, 6);
        Padding = new Thickness(8, 0, 2, 0);
        Height = 28;
        var x = new Button
        {
            Content = "×", Padding = new Thickness(6, 0), MinHeight = 0, MinWidth = 0,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = UiTheme.B(fore), FontSize = 14, VerticalAlignment = VerticalAlignment.Center
        };
        x.Classes.Add("ghost");
        ToolTip.SetTip(x, "Убрать");
        x.Click += (_, _) => Removed?.Invoke(this, EventArgs.Empty);
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = text, FontSize = UiTheme.FsLabel, Foreground = UiTheme.B(fore), VerticalAlignment = VerticalAlignment.Center },
                x
            }
        };
    }
}

/// <summary>Bottom toast (auto-hide ~4 s) with an optional action ("Вернуть").</summary>
internal sealed class Snackbar : Border
{
    private readonly TextBlock _text = new() { Foreground = Brushes.White, FontSize = UiTheme.FsLabel, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _action = new() { FontSize = UiTheme.FsLabel, FontWeight = FontWeight.Bold, Margin = new Thickness(16, 0, 0, 0), Padding = new Thickness(6, 2), MinHeight = 0 };
    private readonly DispatcherTimer _timer = new();
    private Action? _onAction;

    public Snackbar()
    {
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(40, 0, 40, 18);
        Padding = new Thickness(14, 8);
        MinHeight = 36;
        CornerRadius = new CornerRadius(4);
        BoxShadow = BoxShadows.Parse("0 6 18 -12 #A6000000");
        _action.Classes.Add("onDark");
        _action.Click += (_, _) => { var a = _onAction; HideNow(); a?.Invoke(); };
        Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _text, _action } };
        _timer.Tick += (_, _) => HideNow();
    }

    public void Show(string message, Tone tone = Tone.Neutral, string? actionText = null, Action? onAction = null)
    {
        Background = UiTheme.B(tone switch
        {
            Tone.Success => UiTheme.Success,
            Tone.Error   => UiTheme.ErrorSnack,
            Tone.Warning => UiTheme.Warning,
            _            => UiTheme.Text
        });
        _text.Text = message;
        _onAction = onAction;
        _action.IsVisible = actionText != null;
        _action.Content = actionText;
        IsVisible = true;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(tone == Tone.Error || actionText != null ? 6000 : 4000);
        _timer.Start();
    }

    private void HideNow() { _timer.Stop(); IsVisible = false; _onAction = null; }
}
