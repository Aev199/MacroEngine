using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MacroEngine.UI;

/// <summary>
/// Design tokens shared with the GeoBase / plx-construct / SoilStiffness web UIs
/// (web/src/styles/tokens.css, docs/migration/DESIGN_SPEC.md): light theme only,
/// blue-grey Material palette, compact "engineering" density, flat surfaces.
/// Font sizes in the spec are CSS px; WinForms wants points (px × 0.75).
/// </summary>
internal static class Theme
{
    // ── Surfaces ────────────────────────────────────────────────────
    public static readonly Color PageBg     = Hex(0xFAFAFA);
    public static readonly Color PanelBg    = Hex(0xFFFFFF);
    public static readonly Color SectionBg  = Hex(0xECEFF1);
    public static readonly Color Border     = Hex(0xCFD8DC);
    public static readonly Color BorderHover = Hex(0xB0BEC5);
    public static readonly Color HoverBg    = Hex(0xF5F7F8);
    public static readonly Color Divider    = Hex(0xECEFF1);

    // ── Accent ──────────────────────────────────────────────────────
    public static readonly Color Accent       = Hex(0x1976D2);
    public static readonly Color AccentBtn    = Hex(0x1E88E5);
    public static readonly Color AccentChipBg = Hex(0xBBDEFB);
    public static readonly Color AccentSoft   = Hex(0xE3F2FD);
    public static readonly Color AccentBorder = Hex(0x90CAF9);

    // ── Text ────────────────────────────────────────────────────────
    public static readonly Color Text      = Hex(0x263238);
    public static readonly Color TextMuted = Hex(0x455A64);
    public static readonly Color TextGrey  = Hex(0x616161);

    // ── Status ──────────────────────────────────────────────────────
    public static readonly Color Success       = Hex(0x388E3C);
    public static readonly Color SuccessBg     = Hex(0xE8F5E9);
    public static readonly Color Warning       = Hex(0xF57C00);
    public static readonly Color WarningBg     = Hex(0xFFF3E0);
    public static readonly Color WarningBorder = Hex(0xFFCC80);
    public static readonly Color Error         = Hex(0xD32F2F);
    public static readonly Color ErrorBg       = Hex(0xFFEBEE);
    public static readonly Color ErrorBorder   = Hex(0xFFCDD2);
    public static readonly Color ErrorSnack    = Hex(0xEF5350);

    // ── Typography (--gb-font: Roboto, then Segoe UI) ──────────────
    private static readonly string Family = PickFamily("Roboto", "Segoe UI");

    public static readonly Font Title    = new(Family, 12f,    FontStyle.Bold);    // 16px
    public static readonly Font PanelTitle = new(Family, 11.25f, FontStyle.Bold);  // 15px
    public static readonly Font Body     = new(Family, 9.75f);                     // 13px
    public static readonly Font BodyBold = new(Family, 9.75f,  FontStyle.Bold);
    public static readonly Font Label    = new(Family, 9f);                        // 12px
    public static readonly Font LabelBold = new(Family, 9f,    FontStyle.Bold);
    public static readonly Font Caption  = new(Family, 8.25f);                     // 11px
    public static readonly Font Mono     = new(PickFamily("Cascadia Mono", "Consolas"), 9.75f);

    // ── DPI ─────────────────────────────────────────────────────────
    // Forms are built in code with AutoScaleMode.None, so pixel constants are
    // scaled here once; sizes measured from text are already in device pixels.
    private static readonly float Dpi = ReadDpi();

    public static int S(int px) => (int)Math.Round(px * Dpi / 96f);

    private static float ReadDpi()
    {
        try { using var g = Graphics.FromHwnd(IntPtr.Zero); return g.DpiX; }
        catch { return 96f; }
    }

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private static string PickFamily(params string[] names)
    {
        using var installed = new InstalledFontCollection();
        foreach (var n in names)
            if (installed.Families.Any(f => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                return n;
        return SystemFonts.MessageBoxFont?.FontFamily.Name ?? "Segoe UI";
    }

    /// <summary>Apply page defaults to a top-level form.</summary>
    public static void ApplyForm(Form f)
    {
        f.Font = Body;
        f.BackColor = PageBg;
        f.ForeColor = Text;
        f.AutoScaleMode = AutoScaleMode.None;
        f.Icon = AppIcon.Get();
    }

    /// <summary>Muted caption label for a field ("Набранный текст", "Сочетание"…).</summary>
    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = LabelBold,
        ForeColor = TextMuted,
        Margin = new Padding(0, S(8), 0, S(3))
    };

    public static void DrawBorder(Graphics g, Rectangle r, Color c)
    {
        using var pen = new Pen(c);
        g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Buttons (.gb-btn / .primary / .danger / .sm)
// ═══════════════════════════════════════════════════════════════════

internal enum ButtonKind { Secondary, Primary, Danger, Ghost }

internal sealed class GbButton : Button
{
    private ButtonKind _kind;
    private bool _toggled;

    public GbButton(string text, ButtonKind kind = ButtonKind.Secondary, bool small = false)
    {
        Text = text;
        FlatStyle = FlatStyle.Flat;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = small ? Theme.Caption : Theme.Label;
        Padding = small ? new Padding(Theme.S(4), 1, Theme.S(4), 1) : new Padding(Theme.S(7), Theme.S(3), Theme.S(7), Theme.S(3));
        MinimumSize = new Size(0, Theme.S(small ? 26 : 30));
        Margin = new Padding(0, 0, Theme.S(6), 0);
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        Kind = kind;
    }

    public ButtonKind Kind
    {
        get => _kind;
        set { _kind = value; ApplyColors(); }
    }

    /// <summary>Selected state for segmented controls (accent-soft fill, accent text).</summary>
    public bool Toggled
    {
        get => _toggled;
        set { _toggled = value; ApplyColors(); }
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        ApplyColors();
    }

    private void ApplyColors()
    {
        var fa = FlatAppearance;
        fa.BorderSize = 1;
        if (_toggled)
        {
            BackColor = Theme.AccentSoft; ForeColor = Theme.Accent;
            fa.BorderColor = Theme.AccentBorder;
            fa.MouseOverBackColor = Theme.AccentSoft; fa.MouseDownBackColor = Theme.AccentChipBg;
            return;
        }
        switch (_kind)
        {
            case ButtonKind.Primary:
                BackColor = Enabled ? Theme.AccentBtn : Theme.BorderHover;
                ForeColor = Color.White;
                fa.BorderColor = BackColor;
                fa.MouseOverBackColor = Theme.Accent; fa.MouseDownBackColor = Theme.Accent;
                break;
            case ButtonKind.Danger:
                BackColor = Theme.PanelBg; ForeColor = Theme.Error;
                fa.BorderColor = Theme.ErrorBorder;
                fa.MouseOverBackColor = Theme.ErrorBg; fa.MouseDownBackColor = Theme.ErrorBorder;
                break;
            case ButtonKind.Ghost:
                BackColor = Theme.PanelBg; ForeColor = Theme.TextMuted;
                fa.BorderSize = 0;
                fa.MouseOverBackColor = Theme.HoverBg; fa.MouseDownBackColor = Theme.Divider;
                break;
            default:
                BackColor = Theme.PanelBg; ForeColor = Theme.Text;
                fa.BorderColor = Theme.Border;
                fa.MouseOverBackColor = Theme.HoverBg; fa.MouseDownBackColor = Theme.Divider;
                break;
        }
    }
}

/// <summary>A row of mutually exclusive toggle buttons ("Набираю текст | Нажимаю сочетание | …").</summary>
internal sealed class Segmented : FlowLayoutPanel
{
    private readonly List<GbButton> _buttons = new();
    private int _selected = -1;

    public event EventHandler? SelectedChanged;

    public Segmented(params string[] options)
    {
        AutoSize = true;
        WrapContents = true;
        Margin = new Padding(0, 0, 0, Theme.S(2));
        for (int i = 0; i < options.Length; i++)
        {
            int idx = i;
            var b = new GbButton(options[i]) { Margin = new Padding(0, 0, Theme.S(4), Theme.S(4)) };
            b.Click += (_, _) => { if (_selected != idx) { SelectedIndex = idx; SelectedChanged?.Invoke(this, EventArgs.Empty); } };
            _buttons.Add(b);
            Controls.Add(b);
        }
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            _selected = value;
            for (int i = 0; i < _buttons.Count; i++) _buttons[i].Toggled = i == value;
        }
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Underline tabs (.gb-subtab / .tab--active)
// ═══════════════════════════════════════════════════════════════════

internal sealed class TabStrip : Control
{
    private readonly List<string> _items = new();
    private readonly List<Rectangle> _bounds = new();
    private int _selected;
    private int _hover = -1;

    public event EventHandler? SelectedChanged;

    public TabStrip(params string[] items)
    {
        _items.AddRange(items);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = Theme.S(38);
        BackColor = Theme.PanelBg;
        Font = Theme.Label;
        Cursor = Cursors.Hand;
    }

    public int SelectedIndex
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
    }

    public void SetText(int index, string text) { _items[index] = text; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        _bounds.Clear();
        int x = Theme.S(12);
        for (int i = 0; i < _items.Count; i++)
        {
            bool active = i == _selected;
            var font = active ? Theme.LabelBold : Theme.Label;
            var size = TextRenderer.MeasureText(g, _items[i], font);
            var r = new Rectangle(x, 0, size.Width + Theme.S(20), Height);
            _bounds.Add(r);
            var color = active || i == _hover ? Theme.Text : Theme.TextMuted;
            TextRenderer.DrawText(g, _items[i], font, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (active)
                using (var b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, r.X, Height - Theme.S(3), r.Width, Theme.S(2));
            x = r.Right;
        }
        using var pen = new Pen(Theme.Border);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = _bounds.FindIndex(r => r.Contains(e.Location));
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = _bounds.FindIndex(r => r.Contains(e.Location));
        if (i >= 0) SelectedIndex = i;
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Note (.gb-note — text with a 2px tone bar on the left)
// ═══════════════════════════════════════════════════════════════════

internal enum Tone { Neutral, Success, Warning, Error }

internal sealed class Note : Control
{
    private Tone _tone;

    public Note(string text = "", Tone tone = Tone.Neutral)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Label;
        Margin = new Padding(0, Theme.S(4), 0, Theme.S(4));
        _tone = tone;
        Text = text;
    }

    public Tone Tone { get => _tone; set { _tone = value; Invalidate(); } }

    public void Set(string text, Tone tone) { _tone = tone; Text = text; Visible = text.Length > 0; Invalidate(); }

    private static readonly int PadL = Theme.S(10), PadV = Theme.S(5);

    public override Size GetPreferredSize(Size proposed)
    {
        int w = proposed.Width > 0 && proposed.Width < 10000 ? proposed.Width : Math.Max(Width, 200);
        // During the first layout passes the parent may hand us a near-zero width;
        // measuring at that width wraps every letter and reserves a huge row.
        w = Math.Max(w, Theme.S(160));
        if (Text.Length == 0) return new Size(w, 0);
        var sz = TextRenderer.MeasureText(Text, Font, new Size(w - PadL - 4, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        return new Size(w, sz.Height + PadV * 2);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitHeight(); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); FitHeight(); }

    private void FitHeight()
    {
        int h = GetPreferredSize(new Size(Width, 0)).Height;
        if (Height != h) Height = h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.PanelBg);
        var bar = _tone switch
        {
            Tone.Success => Theme.Success,
            Tone.Warning => Theme.Warning,
            Tone.Error   => Theme.Error,
            _            => Theme.Border
        };
        using (var b = new SolidBrush(bar)) g.FillRectangle(b, 0, 0, Theme.S(2), Height);
        var color = _tone == Tone.Neutral ? Theme.TextMuted : Theme.Text;
        TextRenderer.DrawText(g, Text, Font, new Rectangle(PadL, PadV, Width - PadL - 4, Height - PadV * 2),
            color, TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Layout helpers
// ═══════════════════════════════════════════════════════════════════

/// <summary>Single-column auto-height stack; hidden children collapse their row.</summary>
internal sealed class Stack : TableLayoutPanel
{
    public Stack()
    {
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
    }

    /// <summary>Append a row. <paramref name="stretch"/> = fill the width, else keep natural width.</summary>
    public T Add<T>(T c, bool stretch = true) where T : Control
    {
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (c.Dock == DockStyle.None)
            c.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top : AnchorStyles.Left | AnchorStyles.Top;
        Controls.Add(c, 0, RowCount);
        RowCount++;
        return c;
    }
}

/// <summary>Section title with a divider above it (.gb-section header, 12px/600).</summary>
internal sealed class SectionHeader : Control
{
    public SectionHeader(string text)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Text = text;
        Font = Theme.BodyBold;
        Height = Theme.S(40);
        Margin = new Padding(0, Theme.S(6), 0, Theme.S(2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.PanelBg);
        using (var pen = new Pen(Theme.Divider)) g.DrawLine(pen, 0, Theme.S(4), Width, Theme.S(4));
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, Theme.S(8), Width, Height - Theme.S(8)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Panel with 1px border (.gb-panel)
// ═══════════════════════════════════════════════════════════════════

internal class BorderPanel : Panel
{
    public Color BorderColor { get; set; } = Theme.Border;

    public BorderPanel()
    {
        BackColor = Theme.PanelBg;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Theme.DrawBorder(e.Graphics, ClientRectangle, BorderColor);
    }
}

/// <summary>
/// TextBox inside a flat 1px frame that turns accent on focus — WinForms can't
/// recolour a native TextBox border, so the frame is drawn by the wrapper.
/// </summary>
internal sealed class InputBox : BorderPanel
{
    public TextBox Box { get; }

    public InputBox(bool multiline = false, bool mono = false)
    {
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Font = mono ? Theme.Mono : Theme.Body,
            BackColor = Theme.PanelBg,
            ForeColor = Theme.Text,
            Multiline = multiline
        };
        if (multiline)
        {
            Box.AcceptsReturn = true;
            Box.AcceptsTab = true;
            Box.ScrollBars = ScrollBars.Vertical;
            Box.WordWrap = true;
        }
        Padding = multiline ? new Padding(Theme.S(6), Theme.S(5), 2, 2) : new Padding(Theme.S(7), Theme.S(6), Theme.S(6), Theme.S(4));
        Height = multiline ? Theme.S(120) : Box.PreferredHeight + Padding.Vertical + 2;
        Margin = new Padding(0, 0, 0, Theme.S(2));
        Controls.Add(Box);
        Box.GotFocus  += (_, _) => { BorderColor = Theme.Accent; Invalidate(); };
        Box.LostFocus += (_, _) => { BorderColor = Theme.Border; Invalidate(); };
        Cursor = Cursors.IBeam;
        Click += (_, _) => Box.Focus();
    }

    public override string Text
    {
        get => Box.Text;
#pragma warning disable CS8765
        set => Box.Text = value;
#pragma warning restore CS8765
    }

    public string Placeholder { set => Box.PlaceholderText = value; }
}

// ═══════════════════════════════════════════════════════════════════
//  Chip with a remove "×" (program filters in "Где работает")
// ═══════════════════════════════════════════════════════════════════

internal sealed class Chip : Control
{
    private readonly bool _exclude;
    private Rectangle _x;
    public event EventHandler? Removed;

    public Chip(string text, bool exclude)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        _exclude = exclude;
        Text = text;
        Font = Theme.Label;
        var sz = TextRenderer.MeasureText(text, Font);
        Size = new Size(sz.Width + Theme.S(34), Theme.S(26));
        Margin = new Padding(0, 0, Theme.S(4), Theme.S(4));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.PanelBg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var fill   = _exclude ? Theme.ErrorBg : Theme.AccentSoft;
        var border = _exclude ? Theme.ErrorBorder : Theme.AccentBorder;
        var fore   = _exclude ? Theme.Error : Theme.Accent;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Rounded(r, 4))
        using (var b = new SolidBrush(fill))
        using (var p = new Pen(border))
        {
            g.FillPath(b, path);
            g.DrawPath(p, path);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.S(8), 0, Width - Theme.S(30), Height), fore,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        _x = new Rectangle(Width - Theme.S(22), 0, Theme.S(20), Height);
        TextRenderer.DrawText(g, "×", Theme.Body, _x, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnMouseMove(MouseEventArgs e) =>
        Cursor = _x.Contains(e.Location) ? Cursors.Hand : Cursors.Default;

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (_x.Contains(e.Location)) Removed?.Invoke(this, EventArgs.Empty);
    }

    private static GraphicsPath Rounded(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Snackbar (bottom toast, auto-hide ~4 s, optional action link)
// ═══════════════════════════════════════════════════════════════════

internal sealed class Snackbar : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 4000 };
    private string _actionText = "";
    private Action? _onAction;
    private Rectangle _actionRect;

    public Snackbar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        Visible = false;
        Font = Theme.Label;
        Height = Theme.S(36);
        _timer.Tick += (_, _) => HideNow();
    }

    public void Show(string message, Tone tone = Tone.Neutral, string? actionText = null, Action? onAction = null)
    {
        BackColor = tone switch
        {
            Tone.Success => Theme.Success,
            Tone.Error   => Theme.ErrorSnack,
            Tone.Warning => Theme.Warning,
            _            => Theme.Text
        };
        Text = message;
        _actionText = actionText ?? "";
        _onAction = onAction;

        int pad = Theme.S(14);
        int textW = TextRenderer.MeasureText(message, Font).Width;
        int actW = _actionText.Length > 0 ? TextRenderer.MeasureText(_actionText, Theme.LabelBold).Width + Theme.S(24) : 0;
        Width = Math.Min(textW + actW + pad * 2, (Parent?.ClientSize.Width ?? 600) - Theme.S(40));
        _actionRect = actW > 0 ? new Rectangle(Width - pad - actW + Theme.S(12), 0, actW, Height) : Rectangle.Empty;
        Cursor = Cursors.Default;
        Reposition();
        Visible = true;
        BringToFront();
        Invalidate();
        _timer.Stop();
        _timer.Interval = tone == Tone.Error || _actionText.Length > 0 ? 6000 : 4000;
        _timer.Start();
    }

    public void Reposition()
    {
        if (Parent == null) return;
        Location = new Point((Parent.ClientSize.Width - Width) / 2, Parent.ClientSize.Height - Height - Theme.S(16));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int pad = Theme.S(14);
        int textRight = _actionRect.IsEmpty ? Width - pad : _actionRect.X - Theme.S(12);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(pad, 0, textRight - pad, Height), Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (!_actionRect.IsEmpty)
            TextRenderer.DrawText(g, _actionText, Theme.LabelBold, _actionRect, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    protected override void OnMouseMove(MouseEventArgs e) =>
        Cursor = _actionRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (!_actionRect.Contains(e.Location)) return;
        var a = _onAction;
        HideNow();
        a?.Invoke();
    }

    private void HideNow() { _timer.Stop(); Visible = false; _onAction = null; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Light menu renderer so the tray menu matches the palette.</summary>
internal sealed class GbMenuRenderer : ToolStripProfessionalRenderer
{
    public GbMenuRenderer() : base(new Colors()) { RoundedEdges = false; }

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.PanelBg;
        public override Color ImageMarginGradientBegin => Theme.PanelBg;
        public override Color ImageMarginGradientMiddle => Theme.PanelBg;
        public override Color ImageMarginGradientEnd => Theme.PanelBg;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.AccentBorder;
        public override Color MenuItemSelected => Theme.AccentSoft;
        public override Color SeparatorDark => Theme.Divider;
        public override Color SeparatorLight => Theme.PanelBg;
        public override Color CheckBackground => Theme.AccentSoft;
        public override Color CheckSelectedBackground => Theme.AccentChipBg;
        public override Color CheckPressedBackground => Theme.AccentChipBg;
    }
}
