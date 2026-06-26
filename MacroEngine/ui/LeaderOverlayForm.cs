namespace MacroEngine.UI;

internal sealed class LeaderOverlayForm : Form
{
    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _hideTimer;

    public LeaderOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 30);
        Opacity = 0.88;
        Size = new Size(240, 44);

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Right - Width - 16, screen.Bottom - Height - 16);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 220, 255)
        };
        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); HideOverlay(); };
    }

    public void ShowLeader(string mods, string seq)
    {
        _label.Text = seq.Length > 0 ? $"{mods} → {seq}…" : $"{mods} → …";
        _label.ForeColor = Color.FromArgb(200, 220, 255);
        _hideTimer.Stop();
        _hideTimer.Interval = 1500;
        _hideTimer.Start();
        if (!Visible) Show();
    }

    public void ShowMatched(string mods, string seq)
    {
        _label.Text = $"✓ {mods} → {seq}";
        _label.ForeColor = Color.FromArgb(100, 240, 140);
        _hideTimer.Stop();
        _hideTimer.Interval = 600;
        _hideTimer.Start();
        if (!Visible) Show();
    }

    public void HideOverlay()
    {
        _hideTimer.Stop();
        _hideTimer.Interval = 1500;
        if (Visible) Hide();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080 | 0x00000020 | 0x08000000;
            return cp;
        }
    }
}
