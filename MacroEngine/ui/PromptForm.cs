namespace MacroEngine.UI;

/// <summary>
/// Tiny modal prompt used to resolve {input:…} and {choice:…} tokens during
/// expansion. Shown from the expander's STA worker thread via ShowDialog.
/// </summary>
internal sealed class PromptForm : Form
{
    private readonly TextBox? _text;
    private readonly ComboBox? _combo;

    public string Value { get; private set; } = "";

    private PromptForm(string label, string[]? choices)
    {
        Text = "MacroEngine";
        Theme.ApplyForm(this);
        BackColor = Theme.PanelBg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        int w = Theme.S(360), pad = Theme.S(14);

        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            Font = Theme.LabelBold,
            ForeColor = Theme.TextMuted,
            Location = new Point(pad, pad),
            MaximumSize = new Size(w - pad * 2, 0)
        };
        int y = pad + lbl.PreferredHeight + Theme.S(6);

        Control input;
        if (choices != null)
        {
            _combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(pad, y),
                Width = w - pad * 2
            };
            _combo.Items.AddRange(choices);
            if (_combo.Items.Count > 0) _combo.SelectedIndex = 0;
            input = _combo;
        }
        else
        {
            var box = new InputBox { Location = new Point(pad, y), Width = w - pad * 2 };
            _text = box.Box;
            input = box;
        }

        var cancel = new GbButton("Отмена") { DialogResult = DialogResult.Cancel, Margin = Padding.Empty };
        var ok = new GbButton("OK", ButtonKind.Primary) { DialogResult = DialogResult.OK, Margin = Padding.Empty };
        int by = y + input.Height + Theme.S(14);
        cancel.Location = new Point(w - pad - cancel.PreferredSize.Width, by);
        ok.Location = new Point(cancel.Left - Theme.S(6) - Math.Max(ok.PreferredSize.Width, Theme.S(64)), by);
        ok.MinimumSize = new Size(Theme.S(64), ok.MinimumSize.Height);
        ClientSize = new Size(w, by + cancel.PreferredSize.Height + pad);

        Controls.AddRange(new Control[] { lbl, input, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;

        int remaining = 60;
        Text = "MacroEngine (60с)";
        var countdown = new System.Windows.Forms.Timer { Interval = 1000 };
        countdown.Tick += (_, _) =>
        {
            if (--remaining <= 0) { countdown.Stop(); DialogResult = DialogResult.Cancel; Close(); return; }
            Text = $"MacroEngine ({remaining}с)";
        };
        Activated += (_, _) => remaining = 60;
        countdown.Start();

        FormClosing += (_, _) =>
        {
            countdown.Stop();
            Value = DialogResult == DialogResult.OK
                ? (_combo != null ? _combo.SelectedItem?.ToString() ?? "" : _text?.Text ?? "")
                : "";
        };
    }

    /// <summary>Ask for free text. Returns "" if cancelled.</summary>
    public static string AskText(string label) => Run(new PromptForm(label, null));

    /// <summary>Ask to pick one of <paramref name="choices"/>. Returns "" if cancelled.</summary>
    public static string AskChoice(string label, string[] choices) => Run(new PromptForm(label, choices));

    private static string Run(PromptForm f)
    {
        using (f)
        {
            f.Shown += (_, _) => { f.Activate(); f.BringToFront(); };
            return f.ShowDialog() == DialogResult.OK ? f.Value : "";
        }
    }
}
