using MacroEngine.Core;
using System.Data;

namespace MacroEngine.UI;

/// <summary>
/// Visual editor for triggers.json.
/// Opens from the tray menu — allows add/edit/delete of triggers
/// without manually editing the JSON file.
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
    private string _filterContext = "Все"; // current filter

    private bool _dirty;

    public SettingsForm(TriggerConfig config)
    {
        _config = config;
        _triggers = config.Load();

        Text = "MacroEngine — Редактор триггеров";
        Size = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        Icon = AppIcon.Get();
        Padding = new Padding(10);

        // ── Filter tabs ────────────────────────────────────────
        _filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 4),
            AutoSize = true
        };

        // ── Hint label ──────────────────────────────────────────
        _lblHint = new Label
        {
            Text = "Сочетание / Лидер / text/richtext/script/lisp. Контекст: * = везде, acad = AutoCAD, !browser = не в браузере.",
            AutoSize = true,
            ForeColor = Color.Gray,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 4)
        };

        // ── Data grid ───────────────────────────────────────────
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

        // Columns
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Trigger",
            HeaderText = "Триггер",
            FillWeight = 12,
            MinimumWidth = 55
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Hotkey",
            HeaderText = "Сочетание",
            FillWeight = 12,
            MinimumWidth = 70
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Leader",
            HeaderText = "Лидер",
            FillWeight = 10,
            MinimumWidth = 65
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            HeaderText = "Значение",
            FillWeight = 35,
            MinimumWidth = 100
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Context",
            HeaderText = "Контекст",
            FillWeight = 20,
            MinimumWidth = 80
        });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Action",
            HeaderText = "Действие",
            FillWeight = 12,
            MinimumWidth = 70,
            DataSource = new[] { "text", "richtext", "script", "lisp" },
            FlatStyle = FlatStyle.Flat
        });

        _grid.CellValueChanged += OnCellValueChanged;
        _grid.CurrentCellDirtyStateChanged += OnCurrentCellDirty;
        _grid.CellMouseClick += OnCellMouseClick;
        _grid.DataError += (_, e) => { /* suppress combo box errors */ };

        // ── Buttons panel ───────────────────────────────────────
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0),
            AutoSize = true
        };

        _btnAdd = new Button { Text = "➕ Добавить", Width = 110 };
        _btnAdd.Click += OnAdd;

        _btnDelete = new Button { Text = "🗑 Удалить", Width = 110 };
        _btnDelete.Click += OnDelete;

        _btnSave = new Button { Text = "💾 Сохранить", Width = 110, Enabled = false };
        _btnSave.Click += OnSave;

        _btnClose = new Button { Text = "Закрыть", Width = 90 };
        _btnClose.Click += (_, _) => Close();

        buttonPanel.Controls.AddRange(new Control[] { _btnAdd, _btnDelete, _btnSave, _btnClose });

        // ── Layout ──────────────────────────────────────────────
        var mainPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
        mainPanel.Controls.Add(_grid);
        mainPanel.Controls.Add(_lblHint);
        mainPanel.Controls.Add(_filterPanel);

        Controls.Add(mainPanel);
        Controls.Add(buttonPanel);

        // ── Populate grid + filters ─────────────────────────────
        PopulateGrid();
        PopulateFilters();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Grid population
    // ═══════════════════════════════════════════════════════════════

    private void PopulateGrid()
    {
        _grid.Rows.Clear();
        foreach (var t in _triggers)
        {
            _grid.Rows.Add(t.Trigger, t.Hotkey ?? "", t.Leader ?? "", t.Value, t.Context, t.Action);
        }
        _dirty = false;
        _btnSave.Enabled = false;
        ApplyFilter();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Context filters
    // ═══════════════════════════════════════════════════════════════

    private void PopulateFilters()
    {
        _filterPanel.Controls.Clear();

        // Collect unique context patterns
        var contexts = new Dictionary<string, int>();
        foreach (var t in _triggers)
        {
            // Split comma-separated contexts
            foreach (var ctx in t.Context.Split(',', StringSplitOptions.TrimEntries))
            {
                string key = ctx.StartsWith('!') ? ctx : ctx == "*" ? "Глобальные" : ctx;
                contexts.TryGetValue(key, out int c);
                contexts[key] = c + 1;
            }
        }

        // Build filter buttons
        AddFilterButton("Все", _triggers.Count);
        if (contexts.TryGetValue("Глобальные", out int globalCount))
        {
            AddFilterButton("Глобальные", globalCount);
            contexts.Remove("Глобальные");
        }
        foreach (var kv in contexts.OrderByDescending(kv => kv.Value))
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
            // Refresh button colors
            foreach (Button b in _filterPanel.Controls)
            {
                bool active = b.Text.StartsWith(label);
                b.BackColor = active ? SystemColors.Highlight : SystemColors.Control;
                b.ForeColor = active ? Color.White : SystemColors.ControlText;
            }
        };
        _filterPanel.Controls.Add(btn);
    }

    private void ApplyFilter()
    {
        string filter = _filterContext;

        for (int i = _grid.RowCount - 1; i >= 0; i--)
        {
            var row = _grid.Rows[i];
            string ctx = row.Cells["Context"].Value?.ToString() ?? "*";

            bool visible = filter == "Все"
                || filter == "Глобальные" && (ctx == "*" || ctx == "*, !browser")
                || ctx.Split(',', StringSplitOptions.TrimEntries).Any(c => c.Trim() == filter);

            // Color-code rows by context type
            if (visible && filter == "Все")
            {
                row.DefaultCellStyle.BackColor = ctx == "*" || ctx.StartsWith("*,")
                    ? Color.FromArgb(230, 255, 230)  // green tint = global
                    : ctx.Contains("winword") ? Color.FromArgb(220, 230, 255)  // blue = Word
                    : ctx.Contains("acad") ? Color.FromArgb(255, 240, 220)     // orange = CAD
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
        // Commit combo box changes immediately
        if (_grid.IsCurrentCellDirty)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        MarkDirty();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _btnSave.Enabled = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Add / Delete
    // ═══════════════════════════════════════════════════════════════

    private void OnAdd(object? sender, EventArgs e)
    {
        _grid.Rows.Add("!new", "", "", "replace text", "*", "text");
        MarkDirty();
        // Scroll to new row
        _grid.FirstDisplayedScrollingRowIndex = _grid.RowCount - 1;
        _grid.CurrentCell = _grid.Rows[^1].Cells[0];
        _grid.BeginEdit(true);
    }

    private void OnDelete(object? sender, EventArgs e)
    {
        if (_grid.SelectedRows.Count == 0) return;
        int idx = _grid.SelectedRows[0].Index;
        if (idx < 0 || idx >= _grid.RowCount) return;

        var row = _grid.Rows[idx];
        string trigger = row.Cells["Trigger"].Value?.ToString() ?? "?";

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
        // Validate
        for (int i = 0; i < _grid.RowCount; i++)
        {
            var row = _grid.Rows[i];
            string trigger = row.Cells["Trigger"].Value?.ToString()?.Trim() ?? "";
            string value = row.Cells["Value"].Value?.ToString() ?? "";

            if (string.IsNullOrEmpty(trigger))
            {
                MessageBox.Show(
                    $"Строка {i + 1}: поле «Триггер» не может быть пустым.",
                    "MacroEngine — Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
        }

        // Build trigger list from grid
        var list = new List<TriggerEntry>();
        for (int i = 0; i < _grid.RowCount; i++)
        {
            var row = _grid.Rows[i];
            list.Add(new TriggerEntry
            {
                Trigger = row.Cells["Trigger"].Value?.ToString()?.Trim() ?? "",
                Hotkey = row.Cells["Hotkey"].Value?.ToString()?.Trim() ?? null,
                Leader = row.Cells["Leader"].Value?.ToString()?.Trim() ?? null,
                Value = row.Cells["Value"].Value?.ToString() ?? "",
                Context = row.Cells["Context"].Value?.ToString()?.Trim() ?? "*",
                Action = row.Cells["Action"].Value?.ToString()?.Trim() ?? "text"
            });
        }

        // Save to file (TrigerConfig.Save fires ConfigChanged → InputBuffer reloads)
        _config.Save(list);
        _triggers.Clear();
        _triggers.AddRange(list);

        _dirty = false;
        _btnSave.Enabled = false;

        MessageBox.Show(
            $"Сохранено триггеров: {list.Count}",
            "MacroEngine",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Close guard
    // ═══════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════
    //  Hotkey recording
    // ═══════════════════════════════════════════════════════════════

    private void OnCellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0) return;
        string colName = _grid.Columns[e.ColumnIndex].Name;
        if (colName != "Hotkey" && colName != "Leader") return;
        if (e.Button != MouseButtons.Left) return;

        using var recorder = new HotkeyRecorderForm();
        if (recorder.ShowDialog(this) == DialogResult.OK && recorder.CapturedCombo.Length > 0)
        {
            _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = recorder.CapturedCombo;
            MarkDirty();
        }
    }

    private void OnHotkeyRecording(string partial)
    {
        if (InvokeRequired) { BeginInvoke(() => OnHotkeyRecording(partial)); return; }
        var cell = _grid.CurrentCell;
        if (cell != null && _grid.Columns[cell.ColumnIndex].Name == "Hotkey")
            cell.Value = partial;
    }

    private void OnHotkeyRecorded(string combo)
    {
        if (InvokeRequired) { BeginInvoke(() => OnHotkeyRecorded(combo)); return; }

        var cell = _grid.CurrentCell;
        if (cell != null && _grid.Columns[cell.ColumnIndex].Name == "Hotkey")
        {
            if (combo.Length > 0)
            {
                cell.Value = combo;
                MarkDirty();
            }
            else
            {
                cell.Value = ""; // Cancelled — clear
            }
        }
        KeyInterceptor.IsRecordingHotkey = false;
    }

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

    private static void LogDebug(string msg)
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macroengine.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
    }
}
