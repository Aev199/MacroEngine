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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(340, 112);
        Icon = AppIcon.Get();

        var lbl = new Label
        {
            Text = label,
            AutoSize = true,
            Location = new Point(12, 14),
            MaximumSize = new Size(316, 0)
        };

        Control input;
        if (choices != null)
        {
            _combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(12, 46),
                Width = 316
            };
            _combo.Items.AddRange(choices);
            if (_combo.Items.Count > 0) _combo.SelectedIndex = 0;
            input = _combo;
        }
        else
        {
            _text = new TextBox { Location = new Point(12, 46), Width = 316 };
            input = _text;
        }

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(172, 78), Width = 75 };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(253, 78), Width = 75 };

        Controls.AddRange(new Control[] { lbl, input, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;

        FormClosing += (_, _) =>
        {
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
