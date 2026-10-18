using MacroEngine.Core;
using System.Diagnostics;

namespace MacroEngine.UI;

/// <summary>
/// Editor window, laid out as list + card (GeoBase design language, see Theme):
///   «Триггеры» — searchable list on the left, a plain-language card on the right:
///                «Когда срабатывает» → «Что делать» → value → «Где работает».
///   «Макросы»  — named step scripts referenced by triggers with action "macro".
///
/// Edits are kept in memory and written by one «Сохранить» (Ctrl+S) for both
/// triggers.json and macros.json. Validation and conflicts are shown live in the
/// list and on the card; confirmations go to a snackbar instead of message boxes.
/// </summary>
internal sealed class SettingsForm : Form
{
    private enum Kind { Text, Hotkey, Leader }

    /// <summary>
    /// Editable view of a <see cref="TriggerEntry"/>. Keeps the hotkey and the leader
    /// chord in separate fields so switching the type back and forth loses nothing,
    /// and keeps the «only in…» mode even while its program list is still empty.
    /// </summary>
    private sealed class Row
    {
        public Kind Kind;
        public string Trigger = "";
        public string Hotkey = "";
        public string Chord = "";
        public string Value = "";
        public string Action = "text";
        public bool Everywhere = true;
        public List<string> Include = new();
        public List<string> Exclude = new();

        public static Row From(TriggerEntry t)
        {
            var r = new Row
            {
                Kind = !string.IsNullOrWhiteSpace(t.Hotkey) ? Kind.Hotkey
                     : !string.IsNullOrWhiteSpace(t.Leader) ? Kind.Leader
                     : Kind.Text,
                Trigger = t.Trigger,
                Hotkey = t.Hotkey ?? "",
                Chord = t.Leader ?? "",
                Value = t.Value,
                Action = string.IsNullOrWhiteSpace(t.Action) ? "text" : t.Action.Trim().ToLowerInvariant()
            };
            var parts = (t.Context ?? "*").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in parts)
            {
                if (p.StartsWith('!')) { if (p.Length > 1) r.Exclude.Add(p[1..].Trim()); }
                else if (p != "*") r.Include.Add(p);
            }
            r.Everywhere = parts.Contains("*") || r.Include.Count == 0;
            if (r.Everywhere) r.Include.Clear();
            return r;
        }

        public string Context =>
            string.Join(", ", (Everywhere ? new List<string> { "*" } : Include).Concat(Exclude.Select(x => "!" + x)));

        public TriggerEntry ToEntry() => new()
        {
            Trigger = Kind == Kind.Hotkey ? "" : Trigger.Trim(),
            Hotkey  = Kind == Kind.Hotkey ? Hotkey : null,
            Leader  = Kind == Kind.Leader ? Chord : null,
            Value   = Value,
            Context = Context,
            Action  = Action
        };

        public Row Clone()
        {
            var r = (Row)MemberwiseClone();
            r.Include = Include.ToList();
            r.Exclude = Exclude.ToList();
            return r;
        }

        public string Title => Kind switch
        {
            Kind.Hotkey => Hotkey.Length > 0 ? Hotkey : "сочетание не записано",
            Kind.Leader => (Chord.Length > 0 ? Chord : "аккорд?") + " → " + (Trigger.Length > 0 ? Trigger : "…"),
            _           => Trigger.Length > 0 ? Trigger : "(пустой триггер)"
        };

        public string WhereText => Everywhere
            ? (Exclude.Count > 0 ? "везде, кроме " + string.Join(", ", Exclude) : "везде")
            : (Include.Count > 0 ? string.Join(", ", Include) : "нигде") +
              (Exclude.Count > 0 ? ", кроме " + string.Join(", ", Exclude) : "");
    }

    private static readonly (string Key, string Label, string Short, string Hint)[] Actions =
    {
        ("text",     "Вставить текст",                 "текст",
            "Текст печатается в активное окно. Кнопки ниже вставляют токены — дату, буфер обмена, место курсора, вопрос пользователю."),
        ("richtext", "Вставить форматированный текст", "RTF",
            "RTF-разметка или путь к .rtf-файлу. Вставляется через буфер обмена — для Word и других редакторов."),
        ("script",   "Запустить программу или скрипт", "запуск",
            "Команда выполняется как в командной строке, например: python C:\\scripts\\job.py"),
        ("lisp",     "Загрузить LISP в AutoCAD",       "LISP",
            "Путь к .lsp/.fas/.vlx — в командную строку AutoCAD отправляется (load \"…\")."),
        ("macro",    "Выполнить макрос",               "макрос",
            "Последовательность шагов с вкладки «Макросы»: ввод текста, клавиши, паузы, клики мышью."),
    };

    private static readonly string[] KindTags = { "текст", "сочетание", "аккорд" };

    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private List<Row> _rows = new();
    private List<MacroDef> _macroDefs = new();
    private readonly Dictionary<Row, string> _errors = new();
    private readonly Dictionary<Row, string> _conflicts = new();

    private bool _dirty;
    private bool _loading;

    // ── Shell ───────────────────────────────────────────────────────
    private readonly Label _subtitle;
    private readonly Label _unsaved;
    private readonly GbButton _btnSave;
    private readonly GbButton _btnRevert;
    private readonly TabStrip _tabs;
    private readonly Panel _pageTriggers;
    private readonly Panel _pageMacros;
    private readonly Snackbar _snack = new();

    // ── Trigger list ────────────────────────────────────────────────
    private readonly InputBox _search = new();
    private readonly ComboBox _filter;
    private readonly ListBox _list;
    private readonly List<Row> _view = new();
    private Row? _cur;

    // ── Trigger card ────────────────────────────────────────────────
    private readonly Panel _cardScroll;
    private readonly Panel _cardEmpty;
    private readonly Label _cardTitle;
    private readonly Label _cardSubtitle;
    private readonly Note _issues = new();

    private readonly Segmented _when = new("Набираю текст", "Нажимаю сочетание", "Аккорд + буквы");
    private readonly Stack _whenText = new();
    private readonly Stack _whenHotkey = new();
    private readonly Stack _whenLeader = new();
    private readonly InputBox _trigText = new(mono: true);
    private readonly GbButton _hotkeyBtn = new("");
    private readonly GbButton _chordBtn = new("");
    private readonly InputBox _leaderRest = new(mono: true);
    private readonly Note _whenNote = new();

    private readonly ComboBox _action;
    private readonly Note _actionNote = new();

    private readonly Label _valueLabel = Theme.FieldLabel("");
    private readonly InputBox _valueText = new(multiline: true, mono: true);
    private readonly FlowLayoutPanel _tokens;
    private readonly TableLayoutPanel _valuePath;
    private readonly InputBox _valueLine = new(mono: true);
    private readonly GbButton _browse = new("Обзор…");
    private readonly FlowLayoutPanel _valueMacro;
    private readonly ComboBox _macroPick;
    private readonly Note _macroPreview = new();

    private readonly Segmented _where = new("Везде", "Только в выбранных программах");
    private readonly Stack _includeBlock = new();
    private readonly FlowLayoutPanel _includeChips;
    private readonly FlowLayoutPanel _excludeChips;

    // ── Macros tab ──────────────────────────────────────────────────
    private readonly ListBox _macroList;
    private readonly Panel _macroCard;
    private readonly Panel _macroEmpty;
    private readonly InputBox _macroName = new();
    private readonly InputBox _macroSteps = new(multiline: true, mono: true);
    private readonly Note _macroUsage = new();
    private readonly Note _macroIssues = new();
    private MacroDef? _curMacro;

    public SettingsForm(TriggerConfig config, MacroLibrary macros)
    {
        _config = config;
        _macros = macros;

        Theme.ApplyForm(this);
        Text = "MacroEngine — Редактор";
        Size = new Size(Theme.S(1040), Theme.S(700));
        MinimumSize = new Size(Theme.S(860), Theme.S(560));
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        // ── App bar ─────────────────────────────────────────────────
        var appbar = new BorderlessBar { Dock = DockStyle.Top, Height = Theme.S(52), Padding = new Padding(Theme.S(14), 0, Theme.S(12), 1) };
        var titles = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            Dock = DockStyle.Left, Padding = new Padding(0, Theme.S(7), 0, 0), BackColor = Theme.PanelBg
        };
        titles.Controls.Add(new Label { Text = "MacroEngine", AutoSize = true, Font = Theme.PanelTitle, ForeColor = Theme.Text, Margin = Padding.Empty });
        _subtitle = new Label { AutoSize = true, Font = Theme.Caption, ForeColor = Theme.TextMuted, Margin = Padding.Empty };
        titles.Controls.Add(_subtitle);

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true,
            Dock = DockStyle.Right, Padding = new Padding(0, Theme.S(10), 0, 0), BackColor = Theme.PanelBg
        };
        _btnSave = new GbButton("Сохранить", ButtonKind.Primary) { Margin = Padding.Empty };
        _btnRevert = new GbButton("Отменить изменения", ButtonKind.Ghost);
        _unsaved = new Label
        {
            Text = "Есть несохранённые изменения", AutoSize = true, Font = Theme.Label, ForeColor = Theme.TextMuted,
            Padding = new Padding(Theme.S(8), Theme.S(3), Theme.S(4), Theme.S(3)), Margin = new Padding(0, Theme.S(4), Theme.S(8), 0)
        };
        _unsaved.Paint += (_, e) => { using var b = new SolidBrush(Theme.Warning); e.Graphics.FillRectangle(b, 0, 0, 2, _unsaved.Height); };
        _btnSave.Click += (_, _) => SaveAll();
        _btnRevert.Click += (_, _) => Revert();
        new ToolTip().SetToolTip(_btnSave, "Ctrl+S");
        actions.Controls.AddRange(new Control[] { _btnSave, _btnRevert, _unsaved });
        appbar.Controls.Add(actions);
        appbar.Controls.Add(titles);

        _tabs = new TabStrip("Триггеры", "Макросы");

        // ── Pages ───────────────────────────────────────────────────
        _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Body, Dock = DockStyle.Fill };
        _list = MakeList(Theme.S(48), DrawTriggerItem);
        _pageTriggers = BuildTriggersPage(out _cardScroll, out _cardEmpty, out _cardTitle, out _cardSubtitle,
            out _action, out _tokens, out _valuePath, out _valueMacro, out _macroPick, out _includeChips, out _excludeChips);

        _macroList = MakeList(Theme.S(44), DrawMacroItem);
        _pageMacros = BuildMacrosPage(out _macroCard, out _macroEmpty);
        _pageMacros.Visible = false;

        _tabs.SelectedChanged += (_, _) =>
        {
            _pageTriggers.Visible = _tabs.SelectedIndex == 0;
            _pageMacros.Visible = _tabs.SelectedIndex == 1;
            if (_tabs.SelectedIndex == 0) { RefreshMacroPick(); LoadCard(); }
            else { _macroList.Invalidate(); LoadMacroCard(); }
        };

        Controls.Add(_snack);
        Controls.Add(_pageTriggers);
        Controls.Add(_pageMacros);
        Controls.Add(_tabs);
        Controls.Add(appbar);
        Resize += (_, _) => _snack.Reposition();

        LoadFromDisk();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Data
    // ═══════════════════════════════════════════════════════════════

    private void LoadFromDisk()
    {
        _rows = _config.Load().Select(Row.From).ToList();
        // Work on copies: MacroLibrary indexes the very objects Load() returns,
        // and the running engine must not see unsaved edits.
        _macroDefs = _macros.Load().Select(m => new MacroDef { Name = m.Name, Steps = m.Steps.ToList() }).ToList();
        _cur = null;
        _curMacro = null;
        Analyze();
        RebuildFilter();
        RebuildList(select: _rows.FirstOrDefault());
        RebuildMacroList(select: _macroDefs.FirstOrDefault());
        SetDirty(false);
    }

    private void Revert()
    {
        if (!_dirty) return;
        if (MessageBox.Show("Отменить все несохранённые изменения?", "MacroEngine",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        LoadFromDisk();
        _snack.Show("Изменения отменены");
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _unsaved.Visible = dirty;
        _btnRevert.Visible = dirty;
        _btnSave.Enabled = dirty;
        _subtitle.Text = $"{Plural(_rows.Count, "триггер", "триггера", "триггеров")} · {Plural(_macroDefs.Count, "макрос", "макроса", "макросов")}";
        _tabs.SetText(0, $"Триггеры  {_rows.Count}");
        _tabs.SetText(1, $"Макросы  {_macroDefs.Count}");
    }

    private void Changed()
    {
        if (_loading) return;
        Analyze();
        SetDirty(true);
    }

    private bool SaveAll()
    {
        Analyze();
        var bad = _rows.FirstOrDefault(r => _errors.ContainsKey(r));
        if (bad != null)
        {
            _tabs.SelectedIndex = 0;
            ShowRow(bad);
            _snack.Show("Не сохранено: " + _errors[bad], Tone.Error);
            return false;
        }

        string? macroError = ValidateMacros(out var badMacro);
        if (macroError != null)
        {
            _tabs.SelectedIndex = 1;
            if (badMacro != null) _macroList.SelectedIndex = _macroDefs.IndexOf(badMacro);
            _snack.Show("Не сохранено: " + macroError, Tone.Error);
            return false;
        }

        _macros.Save(_macroDefs.Select(m => new MacroDef { Name = m.Name.Trim(), Steps = m.Steps.ToList() }));
        _config.Save(_rows.Select(r => r.ToEntry()).ToList());
        SetDirty(false);

        string msg = $"Сохранено: {Plural(_rows.Count, "триггер", "триггера", "триггеров")}, {Plural(_macroDefs.Count, "макрос", "макроса", "макросов")}";
        if (_conflicts.Count > 0)
            _snack.Show(msg + $" · возможных конфликтов: {_conflicts.Count} (отмечены оранжевым)", Tone.Warning);
        else
            _snack.Show(msg, Tone.Success);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Validation (live)
    // ═══════════════════════════════════════════════════════════════

    private void Analyze()
    {
        _errors.Clear();
        _conflicts.Clear();
        foreach (var r in _rows)
        {
            string? e = Validate(r);
            if (e != null) _errors[r] = e;
        }

        for (int i = 0; i < _rows.Count; i++)
        for (int j = i + 1; j < _rows.Count; j++)
        {
            var a = _rows[i]; var b = _rows[j];
            if (a.Kind != b.Kind || !ContextsOverlap(a, b)) continue;
            bool same = a.Kind switch
            {
                Kind.Text   => a.Trigger.Length > 0 && a.Trigger == b.Trigger,
                Kind.Hotkey => a.Hotkey.Length > 0 && string.Equals(a.Hotkey, b.Hotkey, StringComparison.OrdinalIgnoreCase),
                _           => a.Chord.Length > 0 && string.Equals(a.Chord, b.Chord, StringComparison.OrdinalIgnoreCase) && a.Trigger == b.Trigger
            };
            if (!same) continue;
            _conflicts.TryAdd(a, $"«{a.Title}» уже используется в другом триггере ({b.WhereText}) — сработает только один.");
            _conflicts.TryAdd(b, $"«{b.Title}» уже используется в другом триггере ({a.WhereText}) — сработает только один.");
        }

        _list.Invalidate();
        _macroList.Invalidate();
        if (_cur != null) ShowIssues(_cur);
    }

    private string? Validate(Row r)
    {
        switch (r.Kind)
        {
            case Kind.Text when r.Trigger.Trim().Length == 0:
                return "не указан текст триггера";
            case Kind.Hotkey when r.Hotkey.Length == 0:
                return "сочетание клавиш не записано";
            case Kind.Hotkey when SystemHotkeys.IsSystem(r.Hotkey):
                return $"«{r.Hotkey}» — системное сочетание, его нельзя назначить";
            case Kind.Leader when r.Chord.Length == 0:
                return "аккорд не записан";
            case Kind.Leader when r.Trigger.Trim().Length == 0:
                return "не указаны буквы, которые набираются при зажатом аккорде";
            case Kind.Leader when SystemHotkeys.IsSystem($"{r.Chord}+{r.Trigger.Trim()}"):
                return $"«{r.Chord}+{r.Trigger.Trim()}» — системное сочетание";
        }
        if (!r.Everywhere && r.Include.Count == 0)
            return "выберите хотя бы одну программу или «Везде»";
        if (r.Action == "macro" && r.Value.Trim().Length > 0
            && !_macroDefs.Any(m => string.Equals(m.Name.Trim(), r.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
            return $"макрос «{r.Value}» не найден";
        if (r.Value.Trim().Length == 0)
            return r.Action == "macro" ? "не выбран макрос" : "не заполнено значение";
        return null;
    }

    private static bool ContextsOverlap(Row a, Row b)
    {
        if (a.Everywhere || b.Everywhere) return true;
        return a.Include.Intersect(b.Include, StringComparer.OrdinalIgnoreCase).Any();
    }

    private string? ValidateMacros(out MacroDef? bad)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _macroDefs)
        {
            bad = m;
            if (string.IsNullOrWhiteSpace(m.Name)) return "у макроса нет имени";
            if (!seen.Add(m.Name.Trim())) return $"имя макроса «{m.Name}» повторяется";
        }
        bad = null;
        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Triggers page
    // ═══════════════════════════════════════════════════════════════

    private Panel BuildTriggersPage(out Panel cardScroll, out Panel cardEmpty, out Label cardTitle, out Label cardSubtitle,
        out ComboBox action, out FlowLayoutPanel tokens, out TableLayoutPanel valuePath, out FlowLayoutPanel valueMacro,
        out ComboBox macroPick, out FlowLayoutPanel includeChips, out FlowLayoutPanel excludeChips)
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(10)), BackColor = Theme.PageBg };

        // ── Left: search, filter, list ──────────────────────────────
        var left = new BorderPanel { Dock = DockStyle.Left, Width = Theme.S(330), Padding = new Padding(1) };
        var leftHead = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Theme.PanelBg,
            Padding = new Padding(Theme.S(8), Theme.S(8), Theme.S(8), Theme.S(6))
        };
        leftHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leftHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _search.Placeholder = "Поиск (Ctrl+F)";
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, 0, Theme.S(6), Theme.S(6));
        _search.Box.TextChanged += (_, _) => RebuildList(select: _cur);
        var btnNew = new GbButton("+ Новый", ButtonKind.Primary) { Margin = new Padding(0, 0, 0, Theme.S(6)) };
        btnNew.Click += (_, _) => AddTrigger();
        new ToolTip().SetToolTip(btnNew, "Ctrl+N");
        _filter.Margin = Padding.Empty;
        _filter.SelectedIndexChanged += (_, _) => { if (!_loading) RebuildList(select: _cur); };
        leftHead.Controls.Add(_search, 0, 0);
        leftHead.Controls.Add(btnNew, 1, 0);
        leftHead.Controls.Add(_filter, 0, 1);
        leftHead.SetColumnSpan(_filter, 2);
        left.Controls.Add(_list);
        left.Controls.Add(new Divider());
        left.Controls.Add(leftHead);

        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _cur = _list.SelectedIndex >= 0 && _list.SelectedIndex < _view.Count ? _view[_list.SelectedIndex] : null;
            LoadCard();
        };
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { DeleteTrigger(); e.Handled = true; } };

        // ── Right: card ─────────────────────────────────────────────
        var right = new BorderPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
        var gap = new Panel { Dock = DockStyle.Left, Width = Theme.S(8), BackColor = Theme.PageBg };

        cardEmpty = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PanelBg, Visible = false };
        var emptyText = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Body, ForeColor = Theme.TextGrey,
            Text = "Выберите триггер слева или создайте новый (Ctrl+N)."
        };
        cardEmpty.Controls.Add(emptyText);

        cardScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.PanelBg, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), Theme.S(16)) };
        var card = new Stack { Dock = DockStyle.Top };

        // Header: title + subtitle + duplicate/delete
        var head = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, Theme.S(4)) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var headTitles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        cardTitle = new Label { AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, Margin = Padding.Empty };
        cardSubtitle = new Label { AutoSize = true, Font = Theme.Label, ForeColor = Theme.TextMuted, Margin = new Padding(0, Theme.S(2), 0, 0) };
        headTitles.Controls.Add(cardTitle);
        headTitles.Controls.Add(cardSubtitle);
        var headButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var btnDup = new GbButton("Дублировать", small: true);
        var btnDel = new GbButton("Удалить", ButtonKind.Danger, small: true) { Margin = Padding.Empty };
        btnDup.Click += (_, _) => DuplicateTrigger();
        btnDel.Click += (_, _) => DeleteTrigger();
        new ToolTip().SetToolTip(btnDup, "Ctrl+D");
        new ToolTip().SetToolTip(btnDel, "Delete в списке");
        headButtons.Controls.AddRange(new Control[] { btnDup, btnDel });
        head.Controls.Add(headTitles, 0, 0);
        head.Controls.Add(headButtons, 1, 0);
        card.Add(head);
        card.Add(_issues);
        _issues.Visible = false;

        // 1. When
        card.Add(new SectionHeader("Когда срабатывает"));
        card.Add(_when, stretch: false);
        _when.SelectedChanged += (_, _) => { if (_cur == null) return; _cur.Kind = (Kind)_when.SelectedIndex; ShowWhen(); Changed(); RefreshCurrent(); };

        _whenText.Add(Theme.FieldLabel("Набранный текст"), stretch: false);
        _whenText.Add(_trigText);
        _trigText.Placeholder = "например  !tel  или  @@";
        _trigText.Box.TextChanged += (_, _) => { if (_cur == null || _loading) return; _cur.Trigger = _trigText.Text; Changed(); RefreshCurrent(); };
        card.Add(_whenText);

        _whenHotkey.Add(Theme.FieldLabel("Сочетание клавиш"), stretch: false);
        _whenHotkey.Add(_hotkeyBtn, stretch: false);
        _hotkeyBtn.Click += (_, _) => Record(leader: false);
        card.Add(_whenHotkey);

        var leaderGrid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        leaderGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        leaderGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leaderGrid.Controls.Add(Theme.FieldLabel("Зажать аккорд"), 0, 0);
        leaderGrid.Controls.Add(Theme.FieldLabel("и набрать буквы"), 1, 0);
        _chordBtn.Margin = new Padding(0, 0, Theme.S(10), 0);
        _chordBtn.Click += (_, _) => Record(leader: true);
        leaderGrid.Controls.Add(_chordBtn, 0, 1);
        _leaderRest.Dock = DockStyle.Fill;
        _leaderRest.Placeholder = "например  gm";
        _leaderRest.Box.TextChanged += (_, _) => { if (_cur == null || _loading) return; _cur.Trigger = _leaderRest.Text; Changed(); RefreshCurrent(); };
        leaderGrid.Controls.Add(_leaderRest, 1, 1);
        _whenLeader.Add(leaderGrid);
        card.Add(_whenLeader);
        card.Add(_whenNote);

        // 2. What
        card.Add(new SectionHeader("Что делать"));
        action = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Body, Width = Theme.S(320), Margin = new Padding(0, 0, 0, Theme.S(2)) };
        foreach (var a in Actions) action.Items.Add(a.Label);
        action.SelectedIndexChanged += (_, _) =>
        {
            if (_cur == null || _loading || _action.SelectedIndex < 0) return;
            _cur.Action = Actions[_action.SelectedIndex].Key;
            ShowValueEditor();
            Changed();
            RefreshCurrent();
        };
        card.Add(action, stretch: false);
        card.Add(_actionNote);

        // 3. Value — one of: multiline text + tokens / path + browse / macro picker
        card.Add(_valueLabel, stretch: false);
        _valueText.Height = Theme.S(150);
        _valueText.Box.TextChanged += (_, _) => { if (_cur == null || _loading) return; _cur.Value = _valueText.Text.Replace("\r\n", "\n"); Changed(); RefreshCurrent(); };
        card.Add(_valueText);

        tokens = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, Theme.S(4), 0, 0) };
        tokens.Controls.Add(new Label { Text = "Вставить:", AutoSize = true, Font = Theme.Caption, ForeColor = Theme.TextMuted, Margin = new Padding(0, Theme.S(6), Theme.S(4), 0) });
        AddToken(tokens, "Дата", "{date}", "Сегодняшняя дата (дд.мм.гггг)");
        AddToken(tokens, "Время", "{time}", "Текущее время (чч:мм)");
        AddToken(tokens, "Дата и время", "{datetime:dd.MM.yyyy HH:mm}", "Формат можно поменять после двоеточия");
        AddToken(tokens, "Буфер обмена", "{clipboard}", "Текст из буфера обмена");
        AddToken(tokens, "Курсор", "{cursor}", "Здесь окажется курсор после вставки");
        AddToken(tokens, "Спросить…", "{input:Подпись}", "Перед вставкой появится окно для ввода", select: "Подпись");
        AddToken(tokens, "Выбор…", "{choice:вариант 1|вариант 2}", "Перед вставкой появится список вариантов", select: "вариант 1|вариант 2");
        card.Add(tokens);

        valuePath = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        valuePath.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        valuePath.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _valueLine.Dock = DockStyle.Fill;
        _valueLine.Margin = new Padding(0, 0, Theme.S(6), 0);
        _valueLine.Box.TextChanged += (_, _) => { if (_cur == null || _loading) return; _cur.Value = _valueLine.Text; Changed(); RefreshCurrent(); };
        _browse.Margin = Padding.Empty;
        _browse.Click += (_, _) => BrowseForValue();
        valuePath.Controls.Add(_valueLine, 0, 0);
        valuePath.Controls.Add(_browse, 1, 0);
        card.Add(valuePath);

        valueMacro = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        macroPick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Body, Width = Theme.S(260), Margin = new Padding(0, Theme.S(2), Theme.S(6), 0) };
        macroPick.SelectedIndexChanged += (_, _) =>
        {
            if (_cur == null || _loading || _macroPick.SelectedItem is not string name) return;
            _cur.Value = name;
            UpdateMacroPreview();
            Changed();
            RefreshCurrent();
        };
        var btnEditMacro = new GbButton("Открыть шаги");
        btnEditMacro.Click += (_, _) => OpenMacro(_cur?.Value);
        var btnNewMacro = new GbButton("+ Новый макрос", ButtonKind.Ghost);
        btnNewMacro.Click += (_, _) =>
        {
            var def = AddMacro();
            if (_cur != null) { _cur.Value = def.Name; Changed(); }
            OpenMacro(def.Name);
        };
        valueMacro.Controls.AddRange(new Control[] { macroPick, btnEditMacro, btnNewMacro });
        card.Add(valueMacro, stretch: false);
        card.Add(_macroPreview);

        // 4. Where
        card.Add(new SectionHeader("Где работает"));
        card.Add(_where, stretch: false);
        _where.SelectedChanged += (_, _) =>
        {
            if (_cur == null) return;
            _cur.Everywhere = _where.SelectedIndex == 0;
            ShowWhere();
            Changed();
            RefreshCurrent();
        };
        _includeBlock.Add(Theme.FieldLabel("Программы"), stretch: false);
        includeChips = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        _includeBlock.Add(includeChips);
        card.Add(_includeBlock);
        card.Add(Theme.FieldLabel("Кроме"), stretch: false);
        excludeChips = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        card.Add(excludeChips);
        card.Add(new Note("Программа указывается именем процесса (acad, winword, chrome) или частью заголовка окна. " +
                          "Кнопка «+ Добавить» показывает открытые сейчас окна."));

        cardScroll.Controls.Add(card);
        right.Controls.Add(cardScroll);
        right.Controls.Add(cardEmpty);

        page.Controls.Add(right);
        page.Controls.Add(gap);
        page.Controls.Add(left);
        return page;
    }

    private void AddToken(FlowLayoutPanel host, string label, string token, string tip, string? select = null)
    {
        var b = new GbButton(label, small: true) { Margin = new Padding(0, 0, Theme.S(4), Theme.S(4)) };
        new ToolTip().SetToolTip(b, $"{tip}\n{token}");
        b.Click += (_, _) =>
        {
            var box = _valueText.Box;
            int at = box.SelectionStart;
            box.SelectedText = token;
            if (select != null)
            {
                int off = token.IndexOf(select, StringComparison.Ordinal);
                box.Select(at + off, select.Length);
            }
            box.Focus();
        };
        host.Controls.Add(b);
    }

    // ── List ────────────────────────────────────────────────────────

    private void RebuildFilter()
    {
        _loading = true;
        try
        {
            string? prev = _filter.SelectedItem as string;
            _filter.Items.Clear();
            _filter.Items.Add("Все программы");
            _filter.Items.Add("Только «везде»");
            foreach (var p in _rows.SelectMany(r => r.Include).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p))
                _filter.Items.Add(p);
            int idx = prev != null ? _filter.Items.IndexOf(prev) : -1;
            _filter.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally { _loading = false; }
    }

    private bool MatchesView(Row r)
    {
        int f = _filter.SelectedIndex;
        if (f == 1 && !r.Everywhere) return false;
        if (f >= 2 && !r.Include.Contains(_filter.SelectedItem as string ?? "", StringComparer.OrdinalIgnoreCase)) return false;

        string q = _search.Text.Trim();
        if (q.Length == 0) return true;
        return r.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.Value.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.WhereText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildList(Row? select)
    {
        _loading = true;
        try
        {
            _view.Clear();
            _view.AddRange(_rows.Where(MatchesView));
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var r in _view) _list.Items.Add(r);
            _list.EndUpdate();
            int idx = select != null ? _view.IndexOf(select) : -1;
            if (idx < 0 && _view.Count > 0 && select == null) idx = 0;
            _list.SelectedIndex = idx;
            _cur = idx >= 0 ? _view[idx] : null;
        }
        finally { _loading = false; }
        LoadCard();
    }

    private void ShowRow(Row r)
    {
        if (!_view.Contains(r))
        {
            _search.Text = "";
            _loading = true;
            _filter.SelectedIndex = 0;
            _loading = false;
        }
        RebuildList(select: r);
        _list.Focus();
    }

    private void RefreshCurrent()
    {
        if (_cur == null) return;
        int i = _view.IndexOf(_cur);
        if (i >= 0) _list.Invalidate(_list.GetItemRectangle(i));
        UpdateCardHeader();
    }

    private static ListBox MakeList(int itemHeight, DrawItemEventHandler draw)
    {
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = itemHeight,
            IntegralHeight = false,
            BackColor = Theme.PanelBg,
            Font = Theme.Body
        };
        list.DrawItem += draw;
        return list;
    }

    private void DrawTriggerItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _view.Count) return;
        var r = _view[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        var b = e.Bounds;

        using (var bg = new SolidBrush(selected ? Theme.AccentSoft : Theme.PanelBg)) g.FillRectangle(bg, b);
        Color? bar = _errors.ContainsKey(r) ? Theme.Error : _conflicts.ContainsKey(r) ? Theme.Warning : selected ? Theme.Accent : null;
        if (bar != null) using (var br = new SolidBrush(bar.Value)) g.FillRectangle(br, b.X, b.Y, Theme.S(3), b.Height);
        using (var div = new Pen(Theme.Divider)) g.DrawLine(div, b.X, b.Bottom - 1, b.Right, b.Bottom - 1);

        int pad = Theme.S(12);
        int half = b.Height / 2;
        string tag = KindTags[(int)r.Kind];
        var tagSize = TextRenderer.MeasureText(g, tag, Theme.Caption);
        var top = new Rectangle(b.X + pad, b.Y + Theme.S(5), b.Width - pad * 2 - tagSize.Width - Theme.S(6), half - Theme.S(4));
        TextRenderer.DrawText(g, r.Title, Theme.BodyBold, top, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, tag, Theme.Caption, new Rectangle(b.Right - pad - tagSize.Width, top.Y, tagSize.Width, top.Height),
            Theme.TextGrey, TextFormatFlags.Right | TextFormatFlags.Bottom);

        var act = Actions.FirstOrDefault(a => a.Key == r.Action);
        string preview = (act.Short ?? r.Action) + " · " + OneLine(r.Value);
        string where = r.WhereText;
        var whereSize = TextRenderer.MeasureText(g, where, Theme.Caption);
        int whereW = Math.Min(whereSize.Width, b.Width / 3);
        var bottom = new Rectangle(b.X + pad, b.Y + half + Theme.S(1), b.Width - pad * 2 - whereW - Theme.S(8), half - Theme.S(4));
        string? err = _errors.GetValueOrDefault(r);
        TextRenderer.DrawText(g, err ?? preview, Theme.Caption, bottom, err != null ? Theme.Error : Theme.TextGrey,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, where, Theme.Caption, new Rectangle(b.Right - pad - whereW, bottom.Y, whereW, bottom.Height),
            r.Everywhere ? Theme.TextGrey : Theme.Accent, TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    private static string OneLine(string s)
    {
        s = s.Replace("\r", "").Replace("\n", " ¶ ");
        return s.Length > 120 ? s[..120] : s;
    }

    // ── Card ────────────────────────────────────────────────────────

    private void LoadCard()
    {
        _cardEmpty.Visible = _cur == null;
        _cardScroll.Visible = _cur != null;
        if (_cur == null) return;

        _loading = true;
        try
        {
            _when.SelectedIndex = (int)_cur.Kind;
            _trigText.Text = _cur.Trigger;
            _leaderRest.Text = _cur.Trigger;
            int ai = Array.FindIndex(Actions, a => a.Key == _cur.Action);
            _action.SelectedIndex = ai >= 0 ? ai : 0;
            if (ai < 0) _cur.Action = "text";
            _valueText.Text = _cur.Value.Replace("\r\n", "\n").Replace("\n", "\r\n");
            _valueLine.Text = _cur.Value;
            _where.SelectedIndex = _cur.Everywhere ? 0 : 1;
            RefreshMacroPick();
        }
        finally { _loading = false; }

        _cardScroll.SuspendLayout();
        ShowWhen();
        ShowValueEditor();
        ShowWhere();
        ShowIssues(_cur);
        UpdateCardHeader();
        _cardScroll.ResumeLayout();
        _cardScroll.AutoScrollPosition = Point.Empty;
    }

    private void UpdateCardHeader()
    {
        if (_cur == null) return;
        _cardTitle.Text = _cur.Title;
        var act = Actions.FirstOrDefault(a => a.Key == _cur.Action);
        _cardSubtitle.Text = $"{act.Label ?? _cur.Action} · {_cur.WhereText}";
    }

    private void ShowIssues(Row r)
    {
        if (r != _cur) return;
        if (_errors.TryGetValue(r, out var e))
            _issues.Set("Нужно исправить: " + e + ".", Tone.Error);
        else if (_conflicts.TryGetValue(r, out var c))
            _issues.Set(c, Tone.Warning);
        else
            _issues.Set("", Tone.Neutral);
        // Rows above may have appeared/collapsed: repaint the whole card, otherwise
        // the scroll panel leaves stale copies of shifted controls behind.
        _cardScroll.Invalidate(true);
    }

    private void ShowWhen()
    {
        if (_cur == null) return;
        _whenText.Visible = _cur.Kind == Kind.Text;
        _whenHotkey.Visible = _cur.Kind == Kind.Hotkey;
        _whenLeader.Visible = _cur.Kind == Kind.Leader;
        _hotkeyBtn.Text = _cur.Hotkey.Length > 0 ? _cur.Hotkey + "   ·   изменить" : "Записать сочетание…";
        _chordBtn.Text = _cur.Chord.Length > 0 ? _cur.Chord + "   ·   изменить" : "Записать аккорд…";
        _loading = true;
        _trigText.Text = _cur.Trigger;
        _leaderRest.Text = _cur.Trigger;
        _loading = false;
        _whenNote.Set(_cur.Kind switch
        {
            Kind.Text   => "Набранные символы сотрутся и заменятся результатом. Начинайте триггер с ! или @, чтобы он не срабатывал в обычном тексте.",
            Kind.Hotkey => "Ctrl/Alt + клавиша или отдельная F1–F12. Системные сочетания (Ctrl+C, Ctrl+S, Alt+Tab…) назначить нельзя.",
            _           => "Зажмите аккорд (2–3 модификатора, например Ctrl+Alt) и, не отпуская, наберите буквы. Во время набора в углу экрана видна подсказка."
        }, Tone.Neutral);
    }

    private void ShowValueEditor()
    {
        if (_cur == null) return;
        string a = _cur.Action;
        bool text = a is "text" or "richtext";
        bool path = a is "script" or "lisp";
        bool macro = a == "macro";

        _valueText.Visible = text;
        _tokens.Visible = a == "text";
        _valuePath.Visible = path;
        _valueMacro.Visible = macro;
        _macroPreview.Visible = macro;
        _valueLabel.Text = a switch
        {
            "text"     => "Текст для вставки",
            "richtext" => "RTF или путь к .rtf",
            "script"   => "Команда",
            "lisp"     => "Файл LISP",
            _          => "Макрос"
        };
        _valueLine.Placeholder = a == "lisp" ? @"C:\lisp\routines.lsp" : @"python C:\scripts\job.py";
        _actionNote.Set(Actions.First(x => x.Key == a).Hint, Tone.Neutral);

        _loading = true;
        _valueText.Text = _cur.Value.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _valueLine.Text = _cur.Value;
        _loading = false;
        if (macro) { RefreshMacroPick(); UpdateMacroPreview(); }
    }

    private void ShowWhere()
    {
        if (_cur == null) return;
        _includeBlock.Visible = !_cur.Everywhere;
        BuildChips(_includeChips, _cur.Include, exclude: false);
        BuildChips(_excludeChips, _cur.Exclude, exclude: true);
    }

    private void BuildChips(FlowLayoutPanel host, List<string> items, bool exclude)
    {
        host.SuspendLayout();
        foreach (Control c in host.Controls.Cast<Control>().ToList()) { host.Controls.Remove(c); c.Dispose(); }
        foreach (var item in items.ToList())
        {
            var chip = new Chip(item, exclude);
            chip.Removed += (_, _) =>
            {
                items.Remove(item);
                ShowWhere();
                Changed();
                RefreshCurrent();
                if (!exclude) RebuildFilter();
            };
            host.Controls.Add(chip);
        }
        var add = new GbButton(exclude ? "+ Исключение" : "+ Добавить", ButtonKind.Ghost, small: true) { Margin = new Padding(0, 0, 0, Theme.S(4)) };
        add.Click += (_, _) => ShowProgramMenu(add, items, exclude);
        host.Controls.Add(add);
        host.ResumeLayout();
    }

    private void ShowProgramMenu(Control anchor, List<string> target, bool exclude)
    {
        var menu = new ContextMenuStrip { Renderer = new GbMenuRenderer(), Font = Theme.Body, ShowImageMargin = false };
        menu.Items.Add(new ToolStripLabel("Открытые окна") { ForeColor = Theme.TextMuted, Font = Theme.Caption });

        int own = Environment.ProcessId;
        var procs = new List<(string Name, string Title)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id != own && p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0)
                    procs.Add((p.ProcessName.ToLowerInvariant(), p.MainWindowTitle));
            }
            catch { /* access denied for some system processes */ }
            finally { p.Dispose(); }
        }
        foreach (var grp in procs.GroupBy(p => p.Name).OrderBy(g => g.Key))
        {
            string name = grp.Key;
            if (target.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            string title = grp.First().Title;
            if (title.Length > 50) title = title[..50] + "…";
            var item = new ToolStripMenuItem($"{name}   —   {title}");
            item.Click += (_, _) => AddProgram(target, name, exclude);
            menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var manual = new ToolStripMenuItem("Ввести вручную…");
        manual.Click += (_, _) =>
        {
            string v = PromptForm.AskText("Имя процесса или часть заголовка окна (например acad, revit, chrome):").Trim();
            if (v.Length > 0) AddProgram(target, v.TrimStart('!'), exclude);
        };
        menu.Items.Add(manual);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void AddProgram(List<string> target, string name, bool exclude)
    {
        if (target.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        target.Add(name);
        ShowWhere();
        Changed();
        RefreshCurrent();
        if (!exclude) RebuildFilter();
    }

    private void Record(bool leader)
    {
        if (_cur == null) return;
        // Trigger matching is off while our own window is in front (TrayApplicationContext),
        // so existing hotkeys can't fire and swallow keys during recording.
        using (var recorder = new HotkeyRecorderForm(leaderMode: leader))
        {
            if (recorder.ShowDialog(this) != DialogResult.OK || recorder.CapturedCombo.Length == 0) return;
            if (leader) _cur.Chord = recorder.CapturedCombo;
            else _cur.Hotkey = recorder.CapturedCombo;
        }
        ShowWhen();
        Changed();
        RefreshCurrent();
    }

    private void BrowseForValue()
    {
        if (_cur == null) return;
        using var dlg = new OpenFileDialog
        {
            Filter = _cur.Action == "lisp"
                ? "LISP (*.lsp;*.fas;*.vlx)|*.lsp;*.fas;*.vlx|Все файлы|*.*"
                : "Программы и скрипты (*.exe;*.bat;*.cmd;*.ps1;*.py)|*.exe;*.bat;*.cmd;*.ps1;*.py|Все файлы|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string path = dlg.FileName;
        if (_cur.Action == "script")
        {
            if (path.Contains(' ')) path = $"\"{path}\"";
            if (path.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".py\"", StringComparison.OrdinalIgnoreCase))
                path = "python " + path;
        }
        _valueLine.Text = path; // TextChanged writes it into the row
    }

    private void RefreshMacroPick()
    {
        bool prev = _loading;
        _loading = true;
        try
        {
            _macroPick.Items.Clear();
            foreach (var n in _macroDefs.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)))
                _macroPick.Items.Add(n);
            string cur = _cur?.Action == "macro" ? _cur.Value.Trim() : "";
            int idx = -1;
            for (int i = 0; i < _macroPick.Items.Count; i++)
                if (string.Equals((string)_macroPick.Items[i]!, cur, StringComparison.OrdinalIgnoreCase)) idx = i;
            _macroPick.SelectedIndex = idx;
        }
        finally { _loading = prev; }
    }

    private void UpdateMacroPreview()
    {
        var def = _macroDefs.FirstOrDefault(m => string.Equals(m.Name, _cur?.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            _macroPreview.Set(_macroDefs.Count == 0
                ? "Макросов пока нет — создайте первый кнопкой «+ Новый макрос»."
                : "Выберите макрос из списка.", Tone.Neutral);
            return;
        }
        var steps = def.Steps.Where(s => s.Trim().Length > 0 && !s.TrimStart().StartsWith('#')).ToList();
        string shown = string.Join("  →  ", steps.Take(6)) + (steps.Count > 6 ? $"  …ещё {steps.Count - 6}" : "");
        _macroPreview.Set(steps.Count == 0 ? "Макрос пустой." : shown, Tone.Neutral);
    }

    // ── Add / duplicate / delete ────────────────────────────────────

    private void AddTrigger()
    {
        var r = new Row { Kind = Kind.Text, Trigger = "!", Action = "text" };
        int at = _cur != null ? _rows.IndexOf(_cur) + 1 : _rows.Count;
        _rows.Insert(at, r);
        Changed();
        ShowRow(r);
        _trigText.Box.Focus();
        _trigText.Box.SelectionStart = _trigText.Text.Length;
    }

    private void DuplicateTrigger()
    {
        if (_cur == null) return;
        var r = _cur.Clone();
        _rows.Insert(_rows.IndexOf(_cur) + 1, r);
        Changed();
        ShowRow(r);
        _snack.Show("Создана копия — поменяйте триггер, чтобы не было конфликта");
    }

    private void DeleteTrigger()
    {
        if (_cur == null) return;
        var r = _cur;
        int at = _rows.IndexOf(r);
        int viewAt = _view.IndexOf(r);
        _rows.Remove(r);
        Changed();
        RebuildFilter();
        var next = _view.Count > 1 ? _view[Math.Min(viewAt + 1, _view.Count - 1)] : null;
        if (next == r) next = viewAt > 0 ? _view[viewAt - 1] : null;
        RebuildList(select: next);
        _snack.Show($"Удалён «{r.Title}»", Tone.Neutral, "Вернуть", () =>
        {
            _rows.Insert(Math.Min(at, _rows.Count), r);
            Changed();
            RebuildFilter();
            ShowRow(r);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Macros page
    // ═══════════════════════════════════════════════════════════════

    private Panel BuildMacrosPage(out Panel macroCard, out Panel macroEmpty)
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(10)), BackColor = Theme.PageBg };

        var left = new BorderPanel { Dock = DockStyle.Left, Width = Theme.S(280), Padding = new Padding(1) };
        var leftHead = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.PanelBg, Padding = new Padding(Theme.S(8), Theme.S(8), Theme.S(8), Theme.S(6)) };
        var btnNew = new GbButton("+ Новый макрос", ButtonKind.Primary) { Margin = Padding.Empty };
        btnNew.Click += (_, _) => { var d = AddMacro(); RebuildMacroList(d); _macroName.Box.Focus(); _macroName.Box.SelectAll(); };
        leftHead.Controls.Add(btnNew);
        left.Controls.Add(_macroList);
        left.Controls.Add(new Divider());
        left.Controls.Add(leftHead);
        _macroList.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _curMacro = _macroList.SelectedIndex >= 0 && _macroList.SelectedIndex < _macroDefs.Count ? _macroDefs[_macroList.SelectedIndex] : null;
            LoadMacroCard();
        };
        _macroList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { DeleteMacro(); e.Handled = true; } };

        var gap = new Panel { Dock = DockStyle.Left, Width = Theme.S(8), BackColor = Theme.PageBg };
        var right = new BorderPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };

        macroEmpty = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PanelBg, Visible = false };
        macroEmpty.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Body, ForeColor = Theme.TextGrey,
            Text = "Макрос — это последовательность шагов: ввод текста, клавиши, паузы, клики.\nСоздайте первый кнопкой «+ Новый макрос»."
        });

        macroCard = new Panel { Dock = DockStyle.Fill, BackColor = Theme.PanelBg, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), Theme.S(16)) };

        var head = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var nameStack = new Stack { Dock = DockStyle.Fill };
        nameStack.Add(Theme.FieldLabel("Имя макроса"), stretch: false);
        nameStack.Add(_macroName);
        _macroName.Margin = new Padding(0, 0, Theme.S(12), 0);
        _macroName.Box.TextChanged += (_, _) => RenameCurrentMacro(_macroName.Text);
        var btnDel = new GbButton("Удалить", ButtonKind.Danger, small: true) { Margin = new Padding(0, Theme.S(22), 0, 0), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnDel.Click += (_, _) => DeleteMacro();
        head.Controls.Add(nameStack, 0, 0);
        head.Controls.Add(btnDel, 1, 0);

        var stepsHead = new Stack { Dock = DockStyle.Top };
        stepsHead.Add(_macroUsage);
        stepsHead.Add(_macroIssues);
        stepsHead.Add(new SectionHeader("Шаги"));
        var stepButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, Theme.S(4)) };
        stepButtons.Controls.Add(new Label { Text = "Добавить шаг:", AutoSize = true, Font = Theme.Caption, ForeColor = Theme.TextMuted, Margin = new Padding(0, Theme.S(6), Theme.S(4), 0) });
        AddStep(stepButtons, "Ввести текст", "type ", "текст", "Напечатать текст (токены {date}, {time} работают)");
        AddStep(stepButtons, "Нажать клавиши", "key ", "Ctrl+S", "Сочетание или клавиша: Ctrl+S, Enter, F5, Alt+Tab");
        AddStep(stepButtons, "Пауза", "sleep ", "300", "Подождать, мс");
        AddStep(stepButtons, "Клик", "click ", "100,200", "Клик мышью по координатам экрана x,y (также dclick, rclick)");
        AddStep(stepButtons, "Запустить", "run ", "notepad.exe", "Запустить программу или команду");
        AddStep(stepButtons, "Комментарий", "# ", "пояснение", "Строки с # пропускаются");
        stepsHead.Add(stepButtons);

        _macroSteps.Dock = DockStyle.Fill;
        _macroSteps.Box.WordWrap = false;
        _macroSteps.Box.ScrollBars = ScrollBars.Both;
        _macroSteps.Box.TextChanged += (_, _) =>
        {
            if (_curMacro == null || _loading) return;
            _curMacro.Steps = _macroSteps.Text.Replace("\r", "").Split('\n').ToList();
            Changed();
            InvalidateMacroItem();
        };
        var stepsNote = new Note("Один шаг на строку, выполняются сверху вниз. Пустые строки и строки с # пропускаются.") { Dock = DockStyle.Bottom };

        macroCard.Controls.Add(_macroSteps);
        macroCard.Controls.Add(stepsNote);
        macroCard.Controls.Add(stepsHead);
        macroCard.Controls.Add(head);

        right.Controls.Add(macroCard);
        right.Controls.Add(macroEmpty);
        page.Controls.Add(right);
        page.Controls.Add(gap);
        page.Controls.Add(left);
        return page;
    }

    private void AddStep(FlowLayoutPanel host, string label, string prefix, string placeholder, string tip)
    {
        var b = new GbButton(label, small: true) { Margin = new Padding(0, 0, Theme.S(4), Theme.S(4)) };
        new ToolTip().SetToolTip(b, tip);
        b.Click += (_, _) =>
        {
            var box = _macroSteps.Box;
            // Insert on a new line after the caret's line, then select the placeholder.
            int lineEnd = box.Text.IndexOf('\n', box.SelectionStart);
            int at = lineEnd < 0 ? box.TextLength : lineEnd + 1;
            string text = prefix + placeholder;
            bool needBreakBefore = lineEnd < 0 && box.TextLength > 0 && !box.Text.EndsWith('\n');
            string insert = (needBreakBefore ? "\r\n" : "") + text + (lineEnd < 0 ? "" : "\r\n");
            box.Select(at, 0);
            box.SelectedText = insert;
            box.Select(at + (needBreakBefore ? 2 : 0) + prefix.Length, placeholder.Length);
            box.Focus();
        };
        host.Controls.Add(b);
    }

    private void RebuildMacroList(MacroDef? select)
    {
        _loading = true;
        try
        {
            _macroList.BeginUpdate();
            _macroList.Items.Clear();
            foreach (var m in _macroDefs) _macroList.Items.Add(m);
            _macroList.EndUpdate();
            int idx = select != null ? _macroDefs.IndexOf(select) : -1;
            _macroList.SelectedIndex = idx;
            _curMacro = idx >= 0 ? _macroDefs[idx] : null;
        }
        finally { _loading = false; }
        LoadMacroCard();
    }

    private void LoadMacroCard()
    {
        _macroEmpty.Visible = _curMacro == null;
        _macroCard.Visible = _curMacro != null;
        if (_curMacro == null) return;
        _loading = true;
        try
        {
            _macroName.Text = _curMacro.Name;
            _macroSteps.Text = string.Join("\r\n", _curMacro.Steps);
        }
        finally { _loading = false; }
        UpdateMacroUsage();
    }

    private void UpdateMacroUsage()
    {
        if (_curMacro == null) return;
        var users = _rows.Where(r => r.Action == "macro" && string.Equals(r.Value.Trim(), _curMacro.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        _macroUsage.Set(users.Count == 0
            ? "Пока не используется ни одним триггером. Привязать: вкладка «Триггеры» → «Что делать» → «Выполнить макрос»."
            : "Запускается триггерами: " + string.Join(", ", users.Select(u => u.Title)), Tone.Neutral);

        string? err = string.IsNullOrWhiteSpace(_curMacro.Name) ? "У макроса должно быть имя."
            : _macroDefs.Count(m => string.Equals(m.Name.Trim(), _curMacro.Name.Trim(), StringComparison.OrdinalIgnoreCase)) > 1
                ? "Такое имя уже есть у другого макроса." : null;
        _macroIssues.Set(err ?? "", Tone.Error);
    }

    /// <summary>Rename the macro and keep triggers that point at it in sync.</summary>
    private void RenameCurrentMacro(string newName)
    {
        if (_curMacro == null || _loading) return;
        string old = _curMacro.Name.Trim();
        _curMacro.Name = newName;
        if (old.Length > 0)
            foreach (var r in _rows.Where(r => r.Action == "macro" && string.Equals(r.Value.Trim(), old, StringComparison.OrdinalIgnoreCase)))
                r.Value = newName.Trim();
        Changed();
        InvalidateMacroItem();
        UpdateMacroUsage();
    }

    private void InvalidateMacroItem()
    {
        int i = _curMacro != null ? _macroDefs.IndexOf(_curMacro) : -1;
        if (i >= 0) _macroList.Invalidate(_macroList.GetItemRectangle(i));
    }

    private MacroDef AddMacro()
    {
        string name = "новый_макрос";
        for (int n = 2; _macroDefs.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"новый_макрос_{n}";
        var def = new MacroDef { Name = name, Steps = new List<string> { "key Ctrl+S" } };
        _macroDefs.Add(def);
        Changed();
        return def;
    }

    private void OpenMacro(string? name)
    {
        var def = _macroDefs.FirstOrDefault(m => string.Equals(m.Name.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
        _tabs.SelectedIndex = 1;
        RebuildMacroList(def ?? _curMacro ?? _macroDefs.FirstOrDefault());
    }

    private void DeleteMacro()
    {
        if (_curMacro == null) return;
        var def = _curMacro;
        int at = _macroDefs.IndexOf(def);
        _macroDefs.Remove(def);
        Changed();
        RebuildMacroList(_macroDefs.Count > 0 ? _macroDefs[Math.Min(at, _macroDefs.Count - 1)] : null);
        int users = _rows.Count(r => r.Action == "macro" && string.Equals(r.Value.Trim(), def.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        _snack.Show($"Удалён макрос «{def.Name}»" + (users > 0 ? $" — он нужен триггерам: {users}" : ""),
            users > 0 ? Tone.Warning : Tone.Neutral, "Вернуть", () =>
            {
                _macroDefs.Insert(Math.Min(at, _macroDefs.Count), def);
                Changed();
                RebuildMacroList(def);
            });
    }

    private void DrawMacroItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _macroDefs.Count) return;
        var m = _macroDefs[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        var b = e.Bounds;
        using (var bg = new SolidBrush(selected ? Theme.AccentSoft : Theme.PanelBg)) g.FillRectangle(bg, b);
        if (selected) using (var br = new SolidBrush(Theme.Accent)) g.FillRectangle(br, b.X, b.Y, Theme.S(3), b.Height);
        using (var div = new Pen(Theme.Divider)) g.DrawLine(div, b.X, b.Bottom - 1, b.Right, b.Bottom - 1);

        int pad = Theme.S(12);
        int half = b.Height / 2;
        TextRenderer.DrawText(g, string.IsNullOrWhiteSpace(m.Name) ? "(без имени)" : m.Name, Theme.BodyBold,
            new Rectangle(b.X + pad, b.Y + Theme.S(4), b.Width - pad * 2, half - Theme.S(3)), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        int steps = m.Steps.Count(s => s.Trim().Length > 0 && !s.TrimStart().StartsWith('#'));
        int users = _rows.Count(r => r.Action == "macro" && string.Equals(r.Value.Trim(), m.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        string sub = Plural(steps, "шаг", "шага", "шагов") + (users > 0 ? $" · триггеров: {users}" : " · не используется");
        TextRenderer.DrawText(g, sub, Theme.Caption,
            new Rectangle(b.X + pad, b.Y + half + Theme.S(1), b.Width - pad * 2, half - Theme.S(3)), Theme.TextGrey,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Keyboard & close
    // ═══════════════════════════════════════════════════════════════

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = _list; // not the first button in tab order
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                if (_dirty) SaveAll();
                return true;
            case Keys.Control | Keys.N:
                if (_tabs.SelectedIndex == 0) AddTrigger();
                else { var d = AddMacro(); RebuildMacroList(d); _macroName.Box.Focus(); _macroName.Box.SelectAll(); }
                return true;
            case Keys.Control | Keys.F when _tabs.SelectedIndex == 0:
                _search.Box.Focus();
                _search.Box.SelectAll();
                return true;
            case Keys.Control | Keys.D when _tabs.SelectedIndex == 0:
                DuplicateTrigger();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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

            if (result == DialogResult.Cancel || (result == DialogResult.Yes && !SaveAll()))
                e.Cancel = true;
        }
        if (!e.Cancel) KeyInterceptor.IsRecordingHotkey = false;
        base.OnFormClosing(e);
    }

    private static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        string w = m10 == 1 && m100 != 11 ? one
                 : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? few
                 : many;
        return $"{n} {w}";
    }

    // ── Small layout pieces ─────────────────────────────────────────

    /// <summary>White strip with a bottom border (.appbar).</summary>
    private sealed class BorderlessBar : Panel
    {
        public BorderlessBar() { BackColor = Theme.PanelBg; DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    private sealed class Divider : Control
    {
        public Divider() { Dock = DockStyle.Top; Height = 1; BackColor = Theme.Divider; }
    }
}
