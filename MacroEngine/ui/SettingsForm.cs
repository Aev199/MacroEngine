using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>
/// Visual editor with two tabs:
///   «Триггеры» — the trigger table (text / shortcut / leader, contexts, actions).
///   «Макросы»  — named, reusable macros (name + step script). A trigger with
///                action "macro" references a macro by name via a dropdown.
///
/// Trigger activation types:
///   Текст   — typed text trigger (stored as TriggerEntry.Trigger)
///   Шорткат — direct hotkey (stored as TriggerEntry.Hotkey)
///   Лидер   — held modifier chord + typed rest (TriggerEntry.Leader + .Trigger)
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly List<TriggerEntry> _triggers;
    private readonly List<MacroDef> _macroDefs;

    // ── Trigger tab ─────────────────────────────────────────────────
    private readonly DataGridView _grid;
    private readonly Button _btnSave;
    private readonly Label _lblHint;
    private readonly FlowLayoutPanel _filterPanel;

    private string _filterContext = "Все";
    private bool _dirty;
    private bool _loading;

    // ── Macro tab ───────────────────────────────────────────────────
    private readonly ListBox _macroList;
    private readonly TextBox _macroName;
    private readonly TextBox _macroSteps;
    private readonly Button _macroBtnSave;
    private int _macroCurrent = -1;
    private bool _macroLoading;
    private bool _macroDirty;

    // ── Column names ────────────────────────────────────────────────
    private const string ColType    = "Type";
    private const string ColTrigger = "Trigger";
    private const string ColHotkey  = "Hotkey";    // stores both hotkey (Шорткат) and leader (Лидер)
    private const string ColValue   = "Value";
    private const string ColContext = "Context";
    private const string ColAction  = "Action";

    private static readonly Color HotkeyActiveColor = Color.FromArgb(0, 100, 200);

    public SettingsForm(TriggerConfig config, MacroLibrary macros)
    {
        _config = config;
        _macros = macros;
        _triggers = config.Load();
        _macroDefs = macros.Load();

        Text = "MacroEngine — Редактор";
        Size = new Size(880, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        Icon = AppIcon.Get();
        Padding = new Padding(10);

        // Controls created here, laid out by the Build* helpers below.
        _filterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 4),
            AutoSize = true
        };
        _lblHint = new Label
        {
            Text = "Тип: Текст — набранный триггер | Шорткат — прямое сочетание | " +
                   "Лидер — удерживаемый аккорд (Ctrl/Alt) + клавиши «остатка» из поля «Триггер» (напр. gm). " +
                   "Контекст: * = везде, acad = AutoCAD, !browser = не в браузере. " +
                   "Токены: {date} {time} {datetime:HH:mm} {clipboard} {input:подпись} {choice:a|b|c} {cursor}. " +
                   "Действие «macro»: выберите макрос из списка (создаются на вкладке «Макросы»).",
            AutoSize = true,
            ForeColor = Color.Gray,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 4)
        };
        _grid = BuildGrid();
        _btnSave = new Button { Text = "💾 Сохранить", Width = 110, Enabled = false };

        _macroList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _macroName = new TextBox { Dock = DockStyle.Fill };
        _macroSteps = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font(FontFamily.GenericMonospace, 9.5f)
        };
        _macroBtnSave = new Button { Text = "💾 Сохранить макросы", Width = 170, Enabled = false };

        // ── Tabs ──────────────────────────────────────────────────
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tabTriggers = new TabPage("Триггеры");
        var tabMacros   = new TabPage("Макросы");
        BuildTriggersTab(tabTriggers);
        BuildMacrosTab(tabMacros);
        tabs.TabPages.Add(tabTriggers);
        tabs.TabPages.Add(tabMacros);
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab == tabTriggers) RefreshMacroValueCells();
        };

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0),
            AutoSize = true
        };
        var btnClose = new Button { Text = "Закрыть", Width = 90 };
        btnClose.Click += (_, _) => Close();
        bottom.Controls.Add(btnClose);

        Controls.Add(tabs);
        Controls.Add(bottom);

        PopulateGrid();
        PopulateFilters();
        PopulateMacroList();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Trigger tab construction
    // ═══════════════════════════════════════════════════════════════

    private DataGridView BuildGrid()
    {
        var grid = new DataGridView
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

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = ColType,
            HeaderText = "Тип",
            FillWeight = 11,
            MinimumWidth = 80,
            DataSource = new[] { "Текст", "Шорткат", "Лидер" },
            FlatStyle = FlatStyle.Flat
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColTrigger,
            HeaderText = "Триггер",
            FillWeight = 12,
            MinimumWidth = 65
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColHotkey,
            HeaderText = "Шорткат / Лидер",
            FillWeight = 18,
            MinimumWidth = 120,
            ReadOnly = true
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColValue,
            HeaderText = "Значение",
            FillWeight = 33,
            MinimumWidth = 100
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColContext,
            HeaderText = "Контекст",
            FillWeight = 14,
            MinimumWidth = 70
        });
        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = ColAction,
            HeaderText = "Действие",
            FillWeight = 11,
            MinimumWidth = 70,
            DataSource = new[] { "text", "richtext", "script", "lisp", "macro" },
            FlatStyle = FlatStyle.Flat
        });

        grid.CellValueChanged            += OnCellValueChanged;
        grid.CurrentCellDirtyStateChanged += OnCurrentCellDirty;
        grid.CellMouseClick              += OnCellMouseClick;
        grid.CellMouseEnter              += OnCellMouseEnter;
        grid.CellMouseLeave              += (_, _) => grid.Cursor = Cursors.Default;
        grid.CellFormatting              += OnCellFormatting;
        grid.DataError                   += (_, _) => { /* suppress combo box type errors */ };
        return grid;
    }

    private void BuildTriggersTab(TabPage tab)
    {
        tab.Padding = new Padding(8);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0),
            AutoSize = true
        };
        var btnAdd    = new Button { Text = "➕ Добавить", Width = 110 };
        var btnDelete = new Button { Text = "🗑 Удалить",  Width = 110 };
        btnAdd.Click    += OnAdd;
        btnDelete.Click += OnDelete;
        _btnSave.Click  += OnSave;
        buttons.Controls.AddRange(new Control[] { btnAdd, btnDelete, _btnSave });

        tab.Controls.Add(_grid);        // Fill
        tab.Controls.Add(_lblHint);     // Top
        tab.Controls.Add(_filterPanel); // Top
        tab.Controls.Add(buttons);      // Bottom
    }

    // ═══════════════════════════════════════════════════════════════
    //  Macro tab construction
    // ═══════════════════════════════════════════════════════════════

    private void BuildMacrosTab(TabPage tab)
    {
        tab.Padding = new Padding(8);

        // Left: list of macro names + add/delete
        var left = new Panel { Dock = DockStyle.Left, Width = 210 };
        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        var btnMacroAdd = new Button { Text = "➕", Width = 44 };
        var btnMacroDel = new Button { Text = "🗑", Width = 44 };
        btnMacroAdd.Click += OnMacroAdd;
        btnMacroDel.Click += OnMacroDelete;
        leftButtons.Controls.AddRange(new Control[] { btnMacroAdd, btnMacroDel });

        _macroList.SelectedIndexChanged += OnMacroListSelected;

        left.Controls.Add(_macroList);   // Fill
        left.Controls.Add(leftButtons);  // Bottom

        // Right: name + steps editor
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };

        var nameRow = new Panel { Dock = DockStyle.Top, Height = 28 };
        var nameLbl = new Label { Text = "Имя:", Dock = DockStyle.Left, Width = 44, TextAlign = ContentAlignment.MiddleLeft };
        _macroName.TextChanged += (_, _) => { if (!_macroLoading) MarkMacroDirty(); };
        nameRow.Controls.Add(_macroName); // Fill
        nameRow.Controls.Add(nameLbl);    // Left

        var help = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = 56,
            ForeColor = Color.Gray,
            Text = "Шаги (по одному на строку): " +
                   "type <текст> · key <сочетание> (Ctrl+S, Enter, F5) · sleep <мс> · " +
                   "click/dclick/rclick x,y · run <команда>. " +
                   "Пустые строки и строки с # игнорируются."
        };

        _macroSteps.TextChanged += (_, _) => { if (!_macroLoading) MarkMacroDirty(); };

        var macroSaveRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Padding = new Padding(0, 6, 0, 0)
        };
        _macroBtnSave.Click += OnMacroSave;
        macroSaveRow.Controls.Add(_macroBtnSave);

        right.Controls.Add(_macroSteps);  // Fill
        right.Controls.Add(nameRow);      // Top
        right.Controls.Add(help);         // Bottom
        right.Controls.Add(macroSaveRow); // Bottom

        tab.Controls.Add(right); // Fill
        tab.Controls.Add(left);  // Left
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
                var row = _grid.Rows[idx];
                ApplyRowTypeStyles(row);
                ApplyValueCellForAction(row);
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
    //  Row styling & the macro-name dropdown on the Value cell
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

    /// <summary>
    /// For action="macro" rows, turn the Value cell into a dropdown of macro names;
    /// otherwise keep it a plain text cell. Preserves the current value.
    /// </summary>
    private void ApplyValueCellForAction(DataGridViewRow row)
    {
        string action = row.Cells[ColAction].Value?.ToString() ?? "text";
        string current = row.Cells[ColValue].Value?.ToString() ?? "";

        bool prev = _loading;
        _loading = true;
        try
        {
            if (action == "macro")
            {
                if (row.Cells[ColValue] is not DataGridViewComboBoxCell)
                {
                    var combo = new DataGridViewComboBoxCell
                    {
                        FlatStyle = FlatStyle.Flat,
                        DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox
                    };
                    row.Cells[ColValue] = combo;
                }
                FillMacroCombo((DataGridViewComboBoxCell)row.Cells[ColValue], current);
                row.Cells[ColValue].ToolTipText = "Макрос из вкладки «Макросы»";
            }
            else if (row.Cells[ColValue] is DataGridViewComboBoxCell)
            {
                row.Cells[ColValue] = new DataGridViewTextBoxCell();
                row.Cells[ColValue].Value = current;
                row.Cells[ColValue].ToolTipText = "";
            }
        }
        finally
        {
            _loading = prev;
        }
    }

    private void FillMacroCombo(DataGridViewComboBoxCell combo, string current)
    {
        combo.Items.Clear();
        foreach (var n in _macroDefs.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)))
            combo.Items.Add(n);
        // Keep an unknown / inline value visible instead of erroring out.
        if (current.Length > 0 && !combo.Items.Contains(current))
            combo.Items.Add(current);
        combo.Value = current.Length > 0 ? current
                    : combo.Items.Count > 0 ? combo.Items[0] : null;
    }

    /// <summary>Rebuild macro dropdown contents (called when returning to the triggers tab).</summary>
    private void RefreshMacroValueCells()
    {
        bool prev = _loading;
        _loading = true;
        try
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Cells[ColValue] is DataGridViewComboBoxCell combo)
                    FillMacroCombo(combo, row.Cells[ColValue].Value?.ToString() ?? "");
            }
        }
        finally { _loading = prev; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Cell formatting
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
    //  Trigger edit tracking
    // ═══════════════════════════════════════════════════════════════

    private void OnCurrentCellDirty(object? sender, EventArgs e)
    {
        if (_grid.IsCurrentCellDirty)
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }

    private void OnCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;

        string col = _grid.Columns[e.ColumnIndex].Name;
        if (col == ColType)
            ApplyRowTypeStyles(_grid.Rows[e.RowIndex]);
        else if (col == ColAction)
            ApplyValueCellForAction(_grid.Rows[e.RowIndex]);

        MarkDirty();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _btnSave.Enabled = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Trigger mouse handling
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
    //  Trigger add / delete
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
    //  Trigger save
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
    //  Macro tab logic
    // ═══════════════════════════════════════════════════════════════

    private void PopulateMacroList()
    {
        _macroLoading = true;
        try
        {
            _macroList.Items.Clear();
            foreach (var m in _macroDefs)
                _macroList.Items.Add(m.Name);

            _macroCurrent = -1;
            if (_macroList.Items.Count > 0)
            {
                _macroList.SelectedIndex = 0;
                LoadMacroIntoEditor(0);
            }
            else
            {
                LoadMacroIntoEditor(-1);
            }

            _macroDirty = false;
            _macroBtnSave.Enabled = false;
        }
        finally { _macroLoading = false; }
    }

    private void OnMacroListSelected(object? sender, EventArgs e)
    {
        if (_macroLoading) return;

        // Flush the editor back into the previously selected macro first.
        FlushEditorToCurrent();
        LoadMacroIntoEditor(_macroList.SelectedIndex);
    }

    private void LoadMacroIntoEditor(int index)
    {
        bool prev = _macroLoading;
        _macroLoading = true;
        try
        {
            _macroCurrent = index;
            if (index >= 0 && index < _macroDefs.Count)
            {
                _macroName.Text = _macroDefs[index].Name;
                _macroSteps.Text = string.Join(Environment.NewLine, _macroDefs[index].Steps);
                _macroName.Enabled = true;
                _macroSteps.Enabled = true;
            }
            else
            {
                _macroName.Text = "";
                _macroSteps.Text = "";
                _macroName.Enabled = false;
                _macroSteps.Enabled = false;
            }
        }
        finally { _macroLoading = prev; }
    }

    /// <summary>Copy the editor fields back into the currently selected macro def.</summary>
    private void FlushEditorToCurrent()
    {
        if (_macroCurrent < 0 || _macroCurrent >= _macroDefs.Count) return;

        var def = _macroDefs[_macroCurrent];
        def.Name = _macroName.Text.Trim();
        def.Steps = _macroSteps.Text
            .Replace("\r", "")
            .Split('\n')
            .ToList();

        // Keep the list label in sync without re-triggering selection logic.
        _macroLoading = true;
        try { _macroList.Items[_macroCurrent] = def.Name; }
        finally { _macroLoading = false; }
    }

    private void OnMacroAdd(object? sender, EventArgs e)
    {
        FlushEditorToCurrent();

        var def = new MacroDef { Name = "новый_макрос", Steps = new List<string> { "key Ctrl+S" } };
        _macroDefs.Add(def);

        _macroLoading = true;
        try { _macroList.Items.Add(def.Name); }
        finally { _macroLoading = false; }

        _macroList.SelectedIndex = _macroList.Items.Count - 1; // fires selection → loads editor
        MarkMacroDirty();
        _macroName.Focus();
        _macroName.SelectAll();
    }

    private void OnMacroDelete(object? sender, EventArgs e)
    {
        int idx = _macroList.SelectedIndex;
        if (idx < 0 || idx >= _macroDefs.Count) return;

        string name = _macroDefs[idx].Name;
        if (MessageBox.Show($"Удалить макрос «{name}»?", "MacroEngine — Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _macroDefs.RemoveAt(idx);

        _macroLoading = true;
        try { _macroList.Items.RemoveAt(idx); }
        finally { _macroLoading = false; }

        _macroCurrent = -1;
        if (_macroList.Items.Count > 0)
            _macroList.SelectedIndex = Math.Min(idx, _macroList.Items.Count - 1);
        else
            LoadMacroIntoEditor(-1);

        MarkMacroDirty();
    }

    private void OnMacroSave(object? sender, EventArgs e)
    {
        FlushEditorToCurrent();

        // Validate: non-empty unique names.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _macroDefs)
        {
            if (string.IsNullOrWhiteSpace(m.Name))
            {
                MessageBox.Show("У каждого макроса должно быть имя.",
                    "MacroEngine — Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!seen.Add(m.Name))
            {
                MessageBox.Show($"Имя макроса «{m.Name}» повторяется. Имена должны быть уникальными.",
                    "MacroEngine — Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        _macros.Save(_macroDefs);
        _macroDirty = false;
        _macroBtnSave.Enabled = false;

        RefreshMacroValueCells();

        MessageBox.Show($"Сохранено макросов: {_macroDefs.Count}",
            "MacroEngine", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void MarkMacroDirty()
    {
        _macroDirty = true;
        _macroBtnSave.Enabled = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Close guard
    // ═══════════════════════════════════════════════════════════════

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        FlushEditorToCurrent();

        if (_dirty || _macroDirty)
        {
            var result = MessageBox.Show(
                "Есть несохранённые изменения. Сохранить перед закрытием?",
                "MacroEngine",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                if (_macroDirty) OnMacroSave(this, EventArgs.Empty);
                if (_dirty) OnSave(this, EventArgs.Empty);
            }
            else if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
            }
        }
        KeyInterceptor.IsRecordingHotkey = false;
        base.OnFormClosing(e);
    }
}
