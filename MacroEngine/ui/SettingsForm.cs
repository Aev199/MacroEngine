using MacroEngine.Core;
using System.Data;

namespace MacroEngine.UI;

/// <summary>
/// Visual editor for triggers.json.
///
/// Trigger activation types:
///   Текст   — typed text trigger (stored as TriggerEntry.Trigger)
///   Шорткат — direct hotkey (stored as TriggerEntry.Hotkey)
///   Лидер   — hotkey activates leader mode, then text completes it
///             (stored as TriggerEntry.Leader + TriggerEntry.Trigger)
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TriggerConfig _config;
    private readonly List<TriggerEntry> _triggers;
    private readonly DataGridView _grid;
    private readonly Button _btnAdd;
    private readonly Button _btnDelete;
    private readonly Button _btnSave;
    private readonly Button _btnClose;
    private readonly Label _lblHint;
    private readonly FlowLayoutPanel _filterPanel;

    private string _filterContext = "Все";
    private bool _dirty;
    private bool _loading;

    // ── Column names ────────────────────────────────────────────────
    private const string ColType    = "Type";
    private const string ColTrigger = "Trigger";
    private const string ColHotkey  = "Hotkey";    // stores both hotkey (Шорткат) and leader (Лидер)
    private const string ColValue   = "Value";
    private const string ColContext = "Context";
    private const string ColAction  = "Action";

    private static readonly Color HotkeyActiveColor = Color.FromArgb(0, 100, 200);

    public SettingsForm(TriggerConfig config)
    {
        _config = config;
        _triggers = config.Load();

        Text = "MacroEngine — Редактор триггеров";
        Size = new Size(860, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        Icon = AppIcon.Get();
        Padding = new Padding(10);

        // ── Filter tabs ────────────────────────────────────────────
        _filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 4),
            AutoSize = true
        };

        // ── Hint ──────────────────────────────────────────────────
        _lblHint = new Label
        {
            Text = "Тип: Текст — набранный триггер | Шорткат — прямое сочетание | " +
                   "Лидер — удерживаемый аккорд (Ctrl/Alt) + клавиши «остатка» из поля «Триггер» (напр. gm). " +
                   "Контекст: * = везде, acad = AutoCAD, !browser = не в браузере.",
            AutoSize = true,
            ForeColor = Color.Gray,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 4)
        };

        // ── Grid ──────────────────────────────────────────────────
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackgroundColor = SystemColors.Window
        };

        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = ColType,
            HeaderText = "Тип",
            FillWeight = 11,
            MinimumWidth = 80,
            DataSource = new[] { "Текст", "Шорткат", "Лидер" },
            FlatStyle = FlatStyle.Flat
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColTrigger,
            HeaderText = "Триггер",
            FillWeight = 12,
            MinimumWidth = 65
        });

        // Always ReadOnly — opened via mouse click for Шорткат/Лидер rows.
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColHotkey,
            HeaderText = "Шорткат / Лидер",
            FillWeight = 18,
            MinimumWidth = 120,
            ReadOnly = true
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColValue,
            HeaderText = "Значение",
            FillWeight = 33,
            MinimumWidth = 100
        });

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColContext,
            HeaderText = "Контекст",
            FillWeight = 14,
            MinimumWidth = 70
        });

        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = ColAction,
            HeaderText = "Действие",
            FillWeight = 11,
            MinimumWidth = 70,
            DataSource = new[] { "text", "richtext", "script", "lisp" },
            FlatStyle = FlatStyle.Flat
        });

        _grid.CellValueChanged            += OnCellValueChanged;
        _grid.CurrentCellDirtyStateChanged += OnCurrentCellDirty;
        _grid.CellMouseClick              += OnCellMouseClick;
        _grid.CellMouseEnter              += OnCellMouseEnter;
        _grid.CellMouseLeave              += (_, _) => _grid.Cursor = Cursors.Default;
        _grid.CellFormatting              += OnCellFormatting;
        _grid.DataError                   += (_, e) => { /* suppress combo box type errors */ };

        // ── Buttons ───────────────────────────────────────────────
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0),
            AutoSize = true
        };

        _btnAdd    = new Button { Text = "➕ Добавить",  Width = 110 };
        _btnDelete = new Button { Text = "🗑 Удалить",   Width = 110 };
        _btnSave   = new Button { Text = "💾 Сохранить", Width = 110, Enabled = false };
        _btnClose  = new Button { Text = "Закрыть",      Width = 90 };

        _btnAdd.Click    += OnAdd;
        _btnDelete.Click += OnDelete;
        _btnSave.Click   += OnSave;
        _btnClose.Click  += (_, _) => Close();

        buttonPanel.Controls.AddRange(new Control[] { _btnAdd, _btnDelete, _btnSave, _btnClose });

        // ── Layout ────────────────────────────────────────────────
        var mainPanel = new Panel { Dock = DockStyle.Fill };
        mainPanel.Controls.Add(_grid);
        mainPanel.Controls.Add(_lblHint);
        mainPanel.Controls.Add(_filterPanel);

        Controls.Add(mainPanel);
        Controls.Add(buttonPanel);

        PopulateGrid();
        PopulateFilters();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Grid population
    // ═══════════════════════════════════════════════════════════════

    private void PopulateGrid()
    {
        _loading = true;
        try
        {
            _grid.Rows.Clear();
            foreach (var t in _triggers)
            {
                string type   = DeriveType(t);
                string hotkey = t.Hotkey ?? t.Leader ?? "";

                int idx = _grid.Rows.Add(type, t.Trigger, hotkey, t.Value, t.Context, t.Action);
                ApplyRowTypeStyles(_grid.Rows[idx]);
            }
            _dirty = false;
            _btnSave.Enabled = false;
        }
        finally
        {
            _loading = false;
        }
        ApplyFilter();
    }

    private static string DeriveType(TriggerEntry t)
    {
        if (!string.IsNullOrWhiteSpace(t.Hotkey)) return "Шорткат";
        if (!string.IsNullOrWhiteSpace(t.Leader)) return "Лидер";
        return "Текст";
    }

    // ═══════════════════════════════════════════════════════════════
    //  Row type styling
    // ═══════════════════════════════════════════════════════════════

    private void ApplyRowTypeStyles(DataGridViewRow row)
    {
        string type = row.Cells[ColType].Value?.ToString() ?? "Текст";

        bool triggerEditable = type != "Шорткат";
        bool hotkeyEditable  = type != "Текст";

        var triggerCell = row.Cells[ColTrigger];
        var hotkeyCell  = row.Cells[ColHotkey];

        triggerCell.ReadOnly = !triggerEditable;
        triggerCell.Style.BackColor = triggerEditable ? SystemColors.Window : SystemColors.ControlLight;
        triggerCell.Style.ForeColor = triggerEditable ? SystemColors.ControlText : SystemColors.GrayText;

        hotkeyCell.Style.BackColor = hotkeyEditable ? SystemColors.Window : SystemColors.ControlLight;
        hotkeyCell.Style.ForeColor = hotkeyEditable ? HotkeyActiveColor : SystemColors.GrayText;
        hotkeyCell.ToolTipText     = hotkeyEditable ? "Нажмите для записи сочетания клавиш" : "";
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cell formatting (display-only transformations)
    // ═══════════════════════════════════════════════════════════════

    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != ColHotkey) return;

        string type = _grid.Rows[e.RowIndex].Cells[ColType].Value?.ToString() ?? "Текст";
        if (type == "Текст") return;

        string val = e.Value?.ToString() ?? "";
        e.Value = val.Length > 0 ? "⌨  " + val : "⌨  нажмите для записи";
        e.FormattingApplied = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Context filters
    // ═══════════════════════════════════════════════════════════════

    private void PopulateFilters()
    {
        _filterPanel.Controls.Clear();

        var counts = new Dictionary<string, int>();
        foreach (var t in _triggers)
        {
            foreach (var part in t.Context.Split(',', StringSplitOptions.TrimEntries))
            {
                string key = part == "*" ? "Глобальные" : part;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        AddFilterButton("Все", _triggers.Count);
        if (counts.TryGetValue("Глобальные", out int g)) { AddFilterButton("Глобальные", g); counts.Remove("Глобальные"); }
        foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            AddFilterButton(kv.Key, kv.Value);
    }

    private void AddFilterButton(string label, int count)
    {
        var btn = new Button
        {
            Text = $"{label} ({count})",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = label == _filterContext ? SystemColors.Highlight : SystemColors.Control,
            ForeColor = label == _filterContext ? Color.White : SystemColors.ControlText,
            Margin = new Padding(0, 0, 4, 0)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (_, _) =>
        {
            _filterContext = label;
            ApplyFilter();
            foreach (Button b in _filterPanel.Controls)
            {
                bool active = b.Text.StartsWith(label + " (");
                b.BackColor = active ? SystemColors.Highlight : SystemColors.Control;
                b.ForeColor = active ? Color.White : SystemColors.ControlText;
            }
        };
        _filterPanel.Controls.Add(btn);
    }

    private void ApplyFilter()
    {
        for (int i = 0; i < _grid.RowCount; i++)
        {
            var row = _grid.Rows[i];
            string ctx = row.Cells[ColContext].Value?.ToString() ?? "*";

            bool visible = _filterContext == "Все"
                || (_filterContext == "Глобальные" && (ctx == "*" || ctx.StartsWith("*,")))
                || ctx.Split(',', StringSplitOptions.TrimEntries).Any(c => c.Trim() == _filterContext);

            if (visible && _filterContext == "Все")
            {
                row.DefaultCellStyle.BackColor = ctx == "*" || ctx.StartsWith("*,")
                    ? Color.FromArgb(230, 255, 230)
                    : ctx.Contains("winword") ? Color.FromArgb(220, 230, 255)
                    : ctx.Contains("acad")    ? Color.FromArgb(255, 240, 220)
                    : SystemColors.Window;
            }
            else
            {
                row.DefaultCellStyle.BackColor = SystemColors.Window;
            }

            row.Visible = visible;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Edit tracking
    // ═══════════════════════════════════════════════════════════════

    private void OnCurrentCellDirty(object? sender, EventArgs e)
    {
        if (_grid.IsCurrentCellDirty)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;

        if (_grid.Columns[e.ColumnIndex].Name == ColType)
            ApplyRowTypeStyles(_grid.Rows[e.RowIndex]);

        MarkDirty();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _btnSave.Enabled = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Mouse handling
    // ═══════════════════════════════════════════════════════════════

    private void OnCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0 || e.Button != MouseButtons.Left) return;
        if (_grid.Columns[e.ColumnIndex].Name != ColHotkey) return;

        var row  = _grid.Rows[e.RowIndex];
        string type = row.Cells[ColType].Value?.ToString() ?? "Текст";
        if (type == "Текст") return;

        using var recorder = new HotkeyRecorderForm(leaderMode: type == "Лидер");
        if (recorder.ShowDialog(this) == DialogResult.OK && recorder.CapturedCombo.Length > 0)
        {
            row.Cells[ColHotkey].Value = recorder.CapturedCombo;
            MarkDirty();
        }
    }

    private void OnCellMouseEnter(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != ColHotkey) return;
        string type = _grid.Rows[e.RowIndex].Cells[ColType].Value?.ToString() ?? "Текст";
        _grid.Cursor = type != "Текст" ? Cursors.Hand : Cursors.Default;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Add / Delete
    // ═══════════════════════════════════════════════════════════════

    private void OnAdd(object? sender, EventArgs e)
    {
        _loading = true;
        try
        {
            int idx = _grid.Rows.Add("Текст", "!new", "", "текст подстановки", "*", "text");
            ApplyRowTypeStyles(_grid.Rows[idx]);
        }
        finally { _loading = false; }

        MarkDirty();
        _grid.FirstDisplayedScrollingRowIndex = _grid.RowCount - 1;
        _grid.CurrentCell = _grid.Rows[^1].Cells[ColTrigger];
        _grid.BeginEdit(true);
    }

    private void OnDelete(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0) return;
        int idx = _grid.SelectedRows[0].Index;
        if (idx < 0 || idx >= _grid.RowCount) return;

        string trigger = _grid.Rows[idx].Cells[ColTrigger].Value?.ToString() ?? "?";
        var result = MessageBox.Show(
            $"Удалить триггер «{trigger}»?",
            "MacroEngine — Подтверждение",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _grid.Rows.RemoveAt(idx);
            MarkDirty();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Save
    // ═══════════════════════════════════════════════════════════════

    private void OnSave(object? sender, EventArgs e)
    {
        var list = new List<TriggerEntry>();

        for (int i = 0; i < _grid.RowCount; i++)
        {
            var row    = _grid.Rows[i];
            string type    = row.Cells[ColType].Value?.ToString()?.Trim()    ?? "Текст";
            string trigger = row.Cells[ColTrigger].Value?.ToString()?.Trim() ?? "";
            string hotkey  = row.Cells[ColHotkey].Value?.ToString()?.Trim()  ?? "";
            string value   = row.Cells[ColValue].Value?.ToString()            ?? "";
            string context = row.Cells[ColContext].Value?.ToString()?.Trim()  ?? "*";
            string action  = row.Cells[ColAction].Value?.ToString()?.Trim()   ?? "text";

            if (type == "Текст" && string.IsNullOrEmpty(trigger))
            {
                MessageBox.Show($"Строка {i + 1}: поле «Триггер» не может быть пустым.",
                    "MacroEngine — Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (type is "Шорткат" or "Лидер" && string.IsNullOrEmpty(hotkey))
            {
                MessageBox.Show($"Строка {i + 1}: сочетание клавиш не записано. Нажмите на ячейку «Шорткат / Лидер».",
                    "MacroEngine — Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (type == "Лидер" && string.IsNullOrEmpty(trigger))
            {
                MessageBox.Show($"Строка {i + 1}: для лидера в поле «Триггер» укажите «остаток» — клавиши, которые набираются при зажатом аккорде (напр. gm).",
                    "MacroEngine — Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // System hotkeys have the highest priority and cannot be assigned.
            if (type == "Шорткат" && SystemHotkeys.IsSystem(hotkey))
            {
                MessageBox.Show($"Строка {i + 1}: «{hotkey}» — системное сочетание, его нельзя назначить. Выберите другое.",
                    "MacroEngine — Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (type == "Лидер" && SystemHotkeys.IsSystem($"{hotkey}+{trigger}"))
            {
                MessageBox.Show($"Строка {i + 1}: аккорд «{hotkey}» + «{trigger}» образует системное сочетание, его нельзя назначить.",
                    "MacroEngine — Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            list.Add(new TriggerEntry
            {
                Trigger = type == "Шорткат" ? "" : trigger,
                Hotkey  = type == "Шорткат" ? hotkey : null,
                Leader  = type == "Лидер"   ? hotkey : null,
                Value   = value,
                Context = context,
                Action  = action
            });
        }

        _config.Save(list);
        _triggers.Clear();
        _triggers.AddRange(list);

        _dirty = false;
        _btnSave.Enabled = false;

        MessageBox.Show($"Сохранено триггеров: {list.Count}",
            "MacroEngine", MessageBoxButtons.OK, MessageBoxIcon.Information);

        PopulateFilters();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Close guard
    // ═══════════════════════════════════════════════════════════════

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty)
        {
            var result = MessageBox.Show(
                "Есть несохранённые изменения. Сохранить перед закрытием?",
                "MacroEngine",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
                OnSave(this, EventArgs.Empty);
            else if (result == DialogResult.Cancel)
                e.Cancel = true;
        }
        KeyInterceptor.IsRecordingHotkey = false;
        base.OnFormClosing(e);
    }
}