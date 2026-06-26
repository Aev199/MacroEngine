namespace MacroEngine.UI;

internal sealed class ValueEditorForm : Form
{
    private readonly TextBox _editor;
    public string Value { get; private set; } = "";

    public ValueEditorForm(string currentValue, string triggerName)
    {
        Text = $"Редактор значения — {triggerName}";
        Size = new Size(500, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        Icon = AppIcon.Get();
        Padding = new Padding(8);

        _editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 10f),
            Text = currentValue
        };

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 36,
            ForeColor = Color.Gray,
            Text = "Токены: {date} {time} {clipboard} {cursor} {input:подпись} {choice:a|b|c}. Перенос строки: Enter."
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        var btnOk = new Button { Text = "OK", Width = 80, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Отмена", Width = 80, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange(new Control[] { btnCancel, btnOk });

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(_editor);
        Controls.Add(hint);
        Controls.Add(buttons);

        FormClosing += (_, _) =>
        {
            if (DialogResult == DialogResult.OK)
                Value = _editor.Text.Replace("\r\n", "\n");
        };
    }
}
