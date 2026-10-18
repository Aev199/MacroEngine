using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using MacroEngine.Core;

namespace MacroEngine.UI;

/// <summary>Editable view-model for one trigger rule.</summary>
internal sealed class TriggerRow : INotifyPropertyChanged
{
    public static readonly string[] Types = { "Текст", "Шорткат", "Лидер" };
    public static readonly string[] Actions =
        { "text", "richtext", "script", "lisp", "macro", "open", "launch" };

    private string _type = "Текст";
    private string _trigger = "";
    private string _hotkey = "";
    private string _value = "";
    private string _context = "*";
    private string _action = "text";

    public string Type
    {
        get => _type;
        set
        {
            if (!Set(ref _type, value)) return;
            Notify(nameof(TriggerEditable));
            Notify(nameof(HotkeyEditable));
            Notify(nameof(HotkeyDisplay));
            Notify(nameof(DisplayTrigger));
            Notify(nameof(KindTag));
        }
    }

    public string Trigger
    {
        get => _trigger;
        set
        {
            if (Set(ref _trigger, value))
                Notify(nameof(DisplayTrigger));
        }
    }

    public string Hotkey
    {
        get => _hotkey;
        set
        {
            if (!Set(ref _hotkey, value)) return;
            Notify(nameof(HotkeyDisplay));
            Notify(nameof(DisplayTrigger));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (!Set(ref _value, value)) return;
            Notify(nameof(ValuePreview));
            Notify(nameof(ActionSummary));
            Notify(nameof(Subtitle));
        }
    }

    public string Context
    {
        get => _context;
        set
        {
            if (!Set(ref _context, value)) return;
            Notify(nameof(WhereText));
            Notify(nameof(WhereBrush));
        }
    }

    public string Action
    {
        get => _action;
        set
        {
            if (!Set(ref _action, value)) return;
            Notify(nameof(ActionSummary));
            Notify(nameof(Subtitle));
        }
    }

    public bool TriggerEditable => Type != "Шорткат";
    public bool HotkeyEditable => Type != "Текст";

    public string DisplayTrigger => Type switch
    {
        "Шорткат" => Hotkey.Length > 0 ? Hotkey : "Не назначен",
        "Лидер" => Hotkey.Length > 0
            ? $"{Hotkey}  {Trigger}".Trim()
            : Trigger.Length > 0 ? Trigger : "Не назначен",
        _ => Trigger.Length > 0 ? Trigger : "Новый триггер"
    };

    public string HotkeyDisplay => !HotkeyEditable
        ? "Не используется"
        : Hotkey.Length > 0 ? Hotkey : "Назначить";

    public string ValuePreview
    {
        get
        {
            if (Value.Length == 0) return "Пусто";
            string oneLine = FlattenValue(Value);
            return oneLine.Length > 54 ? oneLine[..54] + "..." : oneLine;
        }
    }

    /// <summary>
    /// One-line explanation shown directly in the trigger list. It tells the user
    /// what the rule will do without requiring selection or knowledge of internal
    /// action identifiers such as "text", "macro" or "launch".
    /// </summary>
    public string ActionSummary
    {
        get
        {
            string label = Action.Trim().ToLowerInvariant() switch
            {
                "text" => "Вставить текст",
                "richtext" => "Вставить форматированный текст",
                "script" => "Скрипт",
                "lisp" => "LISP",
                "macro" => "Макрос",
                "open" => "Открыть",
                "launch" => "Запустить",
                { Length: > 0 } unknown => unknown,
                _ => "Действие"
            };

            string preview = FlattenValue(Value);
            if (preview.Length == 0)
                return label + " · пусто";

            const int maxPreviewLength = 64;
            if (preview.Length > maxPreviewLength)
                preview = preview[..maxPreviewLength].TrimEnd() + "...";

            return $"{label} · {preview}";
        }
    }

    private static string FlattenValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";

        var sb = new StringBuilder(value.Length);
        bool previousWhitespace = false;
        foreach (char ch in value)
        {
            bool whitespace = char.IsWhiteSpace(ch);
            if (whitespace)
            {
                if (!previousWhitespace)
                    sb.Append(' ');
            }
            else
            {
                sb.Append(ch);
            }
            previousWhitespace = whitespace;
        }
        return sb.ToString().Trim();
    }

    // ── List presentation (state owned by the settings window) ──────

    private string? _problem;
    private Tone _problemTone;
    private bool _restricted;

    /// <summary>
    /// «Только в выбранных программах» chosen in the editor. Kept apart from
    /// <see cref="Context"/> so the mode survives while its program list is empty.
    /// </summary>
    public bool Restricted
    {
        get => _restricted;
        set
        {
            if (_restricted == value) return;
            _restricted = value;
            Notify(nameof(WhereText));
            Notify(nameof(WhereBrush));
        }
    }

    public string? Problem => _problem;
    public Tone ProblemTone => _problemTone;

    public void SetProblem(string? text, Tone tone)
    {
        if (_problem == text && _problemTone == tone) return;
        _problem = text;
        _problemTone = text == null ? Tone.Neutral : tone;
        Notify(nameof(Subtitle));
        Notify(nameof(SubtitleBrush));
        Notify(nameof(StatusBrush));
    }

    public string KindTag => Type switch { "Шорткат" => "сочетание", "Лидер" => "аккорд", _ => "текст" };

    public string Subtitle => _problemTone == Tone.Error && _problem != null ? _problem : ActionSummary;

    public IBrush SubtitleBrush => UiTheme.B(_problemTone == Tone.Error ? UiTheme.Error : UiTheme.TextGrey);

    public IBrush StatusBrush => _problem == null ? Brushes.Transparent : UiTheme.B(UiTheme.ToneColor(_problemTone));

    public string WhereText => ContextSpec.Describe(Context, Restricted);

    public IBrush WhereBrush => UiTheme.B(Restricted ? UiTheme.Accent : UiTheme.TextGrey);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return false;
        field = value;
        Notify(name!);
        return true;
    }

    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static TriggerRow From(TriggerEntry trigger) => new()
    {
        Restricted = !ContextSpec.IsEverywhere(trigger.Context),
        Type = !string.IsNullOrWhiteSpace(trigger.Hotkey) ? "Шорткат"
            : !string.IsNullOrWhiteSpace(trigger.Leader) ? "Лидер"
            : "Текст",
        Trigger = trigger.Trigger,
        Hotkey = trigger.Hotkey ?? trigger.Leader ?? "",
        Value = trigger.Value,
        Context = trigger.Context,
        Action = trigger.Action
    };

    public TriggerEntry ToEntry() => new()
    {
        Trigger = Type == "Шорткат" ? "" : Trigger.Trim(),
        Hotkey = Type == "Шорткат" ? Hotkey.Trim() : null,
        Leader = Type == "Лидер" ? Hotkey.Trim() : null,
        Value = Value,
        Context = string.IsNullOrWhiteSpace(Context) ? "*" : Context.Trim(),
        Action = string.IsNullOrWhiteSpace(Action) ? "text" : Action.Trim()
    };
}

/// <summary>
/// Stable model fingerprints used by the settings window. Dirty state is derived
/// from actual model differences instead of UI change events, so harmless binding
/// initialization can never create a false unsaved-changes warning.
/// </summary>
internal static class SettingsStateFingerprint
{
    public static string Triggers(IEnumerable<TriggerEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (TriggerEntry entry in entries)
        {
            Add(sb, entry.Trigger);
            Add(sb, entry.Hotkey);
            Add(sb, entry.Leader);
            Add(sb, entry.Value);
            Add(sb, entry.Context);
            Add(sb, entry.Action);
            sb.Append('#');
        }
        return sb.ToString();
    }

    public static string Macros(IEnumerable<MacroDef> macros)
    {
        var sb = new StringBuilder();
        foreach (MacroDef macro in macros)
        {
            Add(sb, macro.Name);
            sb.Append(macro.Steps.Count).Append(':');
            foreach (string step in macro.Steps)
                Add(sb, step);
            sb.Append('#');
        }
        return sb.ToString();
    }

    private static void Add(StringBuilder sb, string? value)
    {
        value ??= "";
        sb.Append(value.Length).Append(':').Append(value).Append('|');
    }
}

/// <summary>
/// The context string ("*", "acad, revit", "*, !browser") as the editor shows it:
/// «везде» or a list of programs, plus exclusions.
/// </summary>
internal static class ContextSpec
{
    public static void Parse(string? context, out List<string> include, out List<string> exclude)
    {
        include = new List<string>();
        exclude = new List<string>();
        foreach (string part in (context ?? "*").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith('!'))
            {
                if (part.Length > 1) exclude.Add(part[1..].Trim());
            }
            else if (part != "*")
            {
                include.Add(part);
            }
        }
    }

    public static bool IsEverywhere(string? context)
    {
        var parts = (context ?? "*").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 || parts.Contains("*");
    }

    public static string Build(bool restricted, IEnumerable<string> include, IEnumerable<string> exclude) =>
        string.Join(", ", (restricted ? include : new[] { "*" }).Concat(exclude.Select(x => "!" + x)));

    public static string Describe(string? context, bool restricted)
    {
        Parse(context, out var include, out var exclude);
        string where = !restricted ? "везде" : include.Count > 0 ? string.Join(", ", include) : "нигде";
        return exclude.Count > 0 ? $"{where}, кроме {string.Join(", ", exclude)}" : where;
    }
}

/// <summary>
/// Settings editor in the GeoBase design language (see <see cref="UiTheme"/>):
///   «Триггеры» — searchable list on the left, a plain-language card on the right:
///                «Когда срабатывает» → «Что делать» → value → «Где работает».
///   «Макросы»  — named step scripts used by triggers with action "macro".
///
/// One «Сохранить» (Ctrl+S) writes triggers and macros. Validation and conflicts
/// are shown live in the list and on the card; confirmations go to a snackbar,
/// deletes can be undone from it. Dirty state is derived from model fingerprints.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static readonly (string Key, string Label, string Hint)[] ActionInfo =
    {
        ("text",     "Вставить текст",
            "Текст печатается в активное окно. Кнопки ниже вставляют токены — дату, буфер обмена, место курсора, вопрос перед вставкой."),
        ("richtext", "Вставить форматированный текст",
            "RTF-разметка или путь к .rtf-файлу. Вставляется через буфер обмена — для Word и других редакторов."),
        ("macro",    "Выполнить макрос",
            "Последовательность шагов с вкладки «Макросы»: ввод текста, клавиши, паузы, клики мышью."),
        ("launch",   "Запустить программу",
            "Программа или документ запускается через Windows, например: notepad.exe C:\\notes.txt"),
        ("open",     "Открыть папку или файл",
            "Путь открывается в проводнике. Токены вроде {date} тоже работают."),
        ("script",   "Выполнить команду или скрипт",
            "Команда выполняется как в командной строке, например: python C:\\scripts\\job.py"),
        ("lisp",     "Загрузить LISP в AutoCAD",
            "Путь к .lsp/.fas/.vlx — в командную строку AutoCAD отправляется (load \"…\")."),
    };

    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly List<TriggerRow> _rows = new();
    private readonly List<MacroDef> _macroDefs = new();
    private readonly List<MacroRow> _macroRows = new();
    private string _savedState = "";
    private bool _dirty;
    private bool _loading;

    // ── Shell ───────────────────────────────────────────────────────
    private readonly TextBlock _subtitle = new() { FontSize = UiTheme.FsCaption, Foreground = UiTheme.B(UiTheme.TextMuted) };
    private readonly Border _unsaved;
    private readonly Button _btnSave = UiTheme.Btn("Сохранить", "primary");
    private readonly Button _btnRevert = UiTheme.Btn("Отменить изменения", "ghost");
    private readonly UnderlineTab _tabTriggers = new();
    private readonly UnderlineTab _tabMacros = new();
    private readonly Control _pageTriggers;
    private readonly Control _pageMacros;
    private readonly Snackbar _snack = new();

    // ── Trigger list ────────────────────────────────────────────────
    private readonly TextBox _search = new() { Watermark = "Поиск (Ctrl+F)" };
    private readonly ComboBox _filter = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _list = new();
    private List<TriggerRow> _view = new();
    private TriggerRow? _cur;

    // ── Trigger card ────────────────────────────────────────────────
    private readonly ScrollViewer _card = new();
    private readonly Control _cardEmpty;
    private readonly TextBlock _cardTitle = new() { FontSize = UiTheme.FsTitle, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _cardSubtitle = new() { FontSize = UiTheme.FsLabel, Foreground = UiTheme.B(UiTheme.TextMuted), Margin = new Thickness(0, 2, 0, 0) };
    private readonly Note _issues = new();

    private readonly ToggleButton[] _when = { UiTheme.Seg("Набираю текст"), UiTheme.Seg("Нажимаю сочетание"), UiTheme.Seg("Аккорд + буквы") };
    private readonly StackPanel _whenText = new();
    private readonly StackPanel _whenHotkey = new();
    private readonly Grid _whenLeader = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
    private readonly TextBox _trigText = new() { FontFamily = UiTheme.Mono, Watermark = "например  !tel  или  @@" };
    private readonly Button _hotkeyBtn = UiTheme.Btn("");
    private readonly Button _chordBtn = UiTheme.Btn("");
    private readonly TextBox _leaderRest = new() { FontFamily = UiTheme.Mono, Watermark = "например  gm" };
    private readonly Note _whenNote = new();

    private readonly ComboBox _action = new() { MinWidth = 320 };
    private readonly Note _actionNote = new();

    private readonly TextBlock _valueLabel = UiTheme.FieldLabel("");
    private readonly TextBox _valueText = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
        FontFamily = UiTheme.Mono, Height = 150, VerticalContentAlignment = VerticalAlignment.Top
    };
    private readonly WrapPanel _tokens = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Grid _valuePath = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
    private readonly TextBox _valueLine = new() { FontFamily = UiTheme.Mono };
    private readonly Button _browseFile = UiTheme.Btn("Файл…");
    private readonly Button _browseFolder = UiTheme.Btn("Папка…");
    private readonly StackPanel _valueMacro = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly ComboBox _macroPick = new() { MinWidth = 260 };
    private readonly Note _macroPreview = new();

    private readonly ToggleButton[] _where = { UiTheme.Seg("Везде"), UiTheme.Seg("Только в выбранных программах") };
    private readonly StackPanel _includeBlock = new();
    private readonly WrapPanel _includeChips = new();
    private readonly WrapPanel _excludeChips = new();
    private readonly Grid _manualEntry = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), IsVisible = false, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBox _manualText = new() { Watermark = "имя процесса или часть заголовка окна (acad, revit, chrome)" };
    private bool _manualExclude;

    // ── Macros tab ──────────────────────────────────────────────────
    private readonly ListBox _macroList = new();
    private readonly Control _macroCard;
    private readonly Control _macroEmpty;
    private readonly TextBox _macroName = new();
    private readonly TextBox _macroSteps = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
        FontFamily = UiTheme.Mono, VerticalContentAlignment = VerticalAlignment.Top
    };
    private readonly Note _macroUsage = new();
    private readonly Note _macroIssues = new();
    private MacroRow? _curMacro;

    private bool _closeConfirmed;
    private bool _closingGuardActive;

    public SettingsWindow(TriggerConfig config, MacroLibrary macros)
    {
        _config = config;
        _macros = macros;

        Title = "MacroEngine — Настройки";
        Width = 1040;
        Height = 700;
        MinWidth = 860;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        _unsaved = new Border
        {
            BorderBrush = UiTheme.B(UiTheme.Warning),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(8, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock { Text = "Есть несохранённые изменения", FontSize = UiTheme.FsLabel, Foreground = UiTheme.B(UiTheme.TextMuted) }
        };
        _btnSave.Click += async (_, _) => await SaveAllAsync();
        _btnRevert.Click += async (_, _) => await RevertAsync();
        ToolTip.SetTip(_btnSave, "Ctrl+S");

        _pageTriggers = BuildTriggersPage(out _cardEmpty);
        _pageMacros = BuildMacrosPage(out _macroCard, out _macroEmpty);

        _tabTriggers.Click += (_, _) => SelectTab(0);
        _tabMacros.Click += (_, _) => SelectTab(1);

        var appbar = new Border
        {
            Background = UiTheme.B(UiTheme.PanelBg),
            Padding = new Thickness(14, 8, 12, 0),
            Child = BuildAppBar()
        };
        var tabs = new Border
        {
            Background = UiTheme.B(UiTheme.PanelBg),
            BorderBrush = UiTheme.B(UiTheme.Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _tabTriggers, _tabMacros } }
        };

        var pages = new Grid { Margin = new Thickness(10, 8, 10, 10), Children = { _pageTriggers, _pageMacros } };
        var shell = new DockPanel();
        DockPanel.SetDock(appbar, Dock.Top);
        DockPanel.SetDock(tabs, Dock.Top);
        shell.Children.Add(appbar);
        shell.Children.Add(tabs);
        shell.Children.Add(pages);

        Content = new Grid { Children = { shell, _snack } };

        AddHandler(KeyDownEvent, OnShortcut, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closing += OnClosingGuard;

        LoadFromDisk();
        SelectTab(0);
    }

    private Control BuildAppBar()
    {
        var titles = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children = { new TextBlock { Text = "MacroEngine", FontSize = UiTheme.FsPanel, FontWeight = FontWeight.Bold }, _subtitle }
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { _unsaved, _btnRevert, _btnSave }
        };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        bar.Children.Add(titles);
        Grid.SetColumn(actions, 1);
        bar.Children.Add(actions);
        return bar;
    }

    private void SelectTab(int index)
    {
        _tabTriggers.Active = index == 0;
        _tabMacros.Active = index == 1;
        _pageTriggers.IsVisible = index == 0;
        _pageMacros.IsVisible = index == 1;
        if (index == 0) { RefreshMacroPick(); LoadCard(); }
        else { foreach (var m in _macroRows) m.Refresh(); LoadMacroCard(); }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Data, dirty state, save
    // ═══════════════════════════════════════════════════════════════

    private void LoadFromDisk()
    {
        _loading = true;
        try
        {
            _rows.Clear();
            foreach (TriggerEntry t in _config.Load())
            {
                var row = TriggerRow.From(t);
                row.PropertyChanged += OnRowChanged;
                _rows.Add(row);
            }
            // Work on copies: the library indexes the objects Load() returns,
            // and the running engine must not see unsaved edits.
            _macroDefs.Clear();
            _macroDefs.AddRange(_macros.Load().Select(m => new MacroDef { Name = m.Name, Steps = m.Steps.ToList() }));
            _macroRows.Clear();
            _macroRows.AddRange(_macroDefs.Select(d => new MacroRow(d, this)));
            _savedState = CurrentState();
        }
        finally { _loading = false; }

        _cur = null;
        _curMacro = null;
        Analyze();
        RebuildFilter();
        RebuildList(_rows.FirstOrDefault());
        RebuildMacroList(_macroRows.FirstOrDefault());
        RefreshDirty();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        if (e.PropertyName is nameof(TriggerRow.Type) or nameof(TriggerRow.Trigger) or nameof(TriggerRow.Hotkey)
            or nameof(TriggerRow.Value) or nameof(TriggerRow.Context) or nameof(TriggerRow.Action))
            Changed();
    }

    private string CurrentState() =>
        SettingsStateFingerprint.Triggers(_rows.Select(r => r.ToEntry())) + "\n" +
        string.Join(",", _rows.Select(r => r.Restricted ? 1 : 0)) + "\n" +
        SettingsStateFingerprint.Macros(_macroDefs);

    private void Changed()
    {
        if (_loading) return;
        Analyze();
        RefreshDirty();
        UpdateCardHeader();
    }

    private void RefreshDirty()
    {
        _dirty = CurrentState() != _savedState;
        _unsaved.IsVisible = _dirty;
        _btnRevert.IsVisible = _dirty;
        _btnSave.IsEnabled = _dirty;
        _subtitle.Text = $"{Plural(_rows.Count, "триггер", "триггера", "триггеров")} · {Plural(_macroDefs.Count, "макрос", "макроса", "макросов")}";
        _tabTriggers.Label = $"Триггеры  {_rows.Count}";
        _tabMacros.Label = $"Макросы  {_macroDefs.Count}";
    }

    private async Task RevertAsync()
    {
        if (!_dirty) return;
        if (!await ConfirmDialog.Show(this, "Отменить все несохранённые изменения?", "Отменить изменения"))
            return;
        LoadFromDisk();
        _snack.Show("Изменения отменены");
    }

    private async Task<bool> SaveAllAsync()
    {
        Analyze();
        var bad = _rows.FirstOrDefault(r => r.ProblemTone == Tone.Error);
        if (bad != null)
        {
            SelectTab(0);
            ShowRow(bad);
            _snack.Show("Не сохранено: " + bad.Problem, Tone.Error);
            return false;
        }
        var badMacro = _macroRows.FirstOrDefault(m => m.Problem != null);
        if (badMacro != null)
        {
            SelectTab(1);
            RebuildMacroList(badMacro);
            _snack.Show("Не сохранено: " + badMacro.Problem, Tone.Error);
            return false;
        }

        foreach (var m in _macroDefs) m.Name = m.Name.Trim();
        var entries = _rows.Select(r => r.ToEntry()).ToList();
        try
        {
            _macros.Save(_macroDefs.Select(m => new MacroDef { Name = m.Name, Steps = m.Steps.ToList() }).ToList());
            _config.Save(entries);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Settings save failed: {ex.GetType().Name}: {ex.Message}");
            await MessageDialog.Show(this, "Ошибка сохранения", "Не удалось сохранить настройки.\n\n" + ex.Message);
            return false;
        }

        _savedState = CurrentState();
        RefreshDirty();
        RebuildFilter();
        int conflicts = _rows.Count(r => r.ProblemTone == Tone.Warning);
        string msg = $"Сохранено: {Plural(_rows.Count, "триггер", "триггера", "триггеров")}, {Plural(_macroDefs.Count, "макрос", "макроса", "макросов")}";
        if (conflicts > 0)
            _snack.Show(msg + $" · возможных конфликтов: {conflicts} (отмечены оранжевым)", Tone.Warning);
        else
            _snack.Show(msg, Tone.Success);
        return true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Validation (live)
    // ═══════════════════════════════════════════════════════════════

    private void Analyze()
    {
        var problems = new Dictionary<TriggerRow, (string Text, Tone Tone)>();
        foreach (var r in _rows)
        {
            string? e = Validate(r);
            if (e != null) problems[r] = (e, Tone.Error);
        }

        for (int i = 0; i < _rows.Count; i++)
        for (int j = i + 1; j < _rows.Count; j++)
        {
            var a = _rows[i]; var b = _rows[j];
            if (a.Type != b.Type || !ContextsOverlap(a, b)) continue;
            string ka = HotkeyRules.Normalize(a.Hotkey.Trim()), kb = HotkeyRules.Normalize(b.Hotkey.Trim());
            bool same = a.Type switch
            {
                "Шорткат" => ka.Length > 0 && string.Equals(ka, kb, StringComparison.OrdinalIgnoreCase),
                "Лидер"   => ka.Length > 0 && string.Equals(ka, kb, StringComparison.OrdinalIgnoreCase) && a.Trigger.Trim() == b.Trigger.Trim(),
                _         => a.Trigger.Trim().Length > 0 && a.Trigger.Trim() == b.Trigger.Trim()
            };
            if (!same) continue;
            problems.TryAdd(a, ($"«{a.DisplayTrigger}» уже используется в другом триггере ({b.WhereText}) — сработает только один", Tone.Warning));
            problems.TryAdd(b, ($"«{b.DisplayTrigger}» уже используется в другом триггере ({a.WhereText}) — сработает только один", Tone.Warning));
        }

        foreach (var r in _rows)
        {
            if (problems.TryGetValue(r, out var p)) r.SetProblem(p.Text, p.Tone);
            else r.SetProblem(null, Tone.Neutral);
        }
        foreach (var m in _macroRows) m.Refresh();
        if (_cur != null) ShowIssues();
        if (_curMacro != null) UpdateMacroUsage();
    }

    private string? Validate(TriggerRow r)
    {
        string trigger = r.Trigger.Trim();
        string hotkey = r.Hotkey.Trim();
        switch (r.Type)
        {
            case "Текст" when trigger.Length == 0:
                return "не указан текст триггера";
            case "Шорткат" when hotkey.Length == 0:
                return "сочетание клавиш не записано";
            case "Шорткат" when !HotkeyRules.TryValidateShortcut(hotkey, out string err):
                return err.TrimEnd('.');
            case "Шорткат" when SystemHotkeys.IsSystem(hotkey):
                return $"«{hotkey}» — системное сочетание, его нельзя назначить";
            case "Лидер" when hotkey.Length == 0:
                return "аккорд не записан";
            case "Лидер" when trigger.Length == 0:
                return "не указаны буквы, которые набираются при зажатом аккорде";
            case "Лидер" when !HotkeyRules.TryValidateLeader(hotkey, out string err):
                return err.TrimEnd('.');
            case "Лидер" when SystemHotkeys.IsSystem($"{hotkey}+{trigger}"):
                return $"«{hotkey}+{trigger}» — системное сочетание";
        }
        ContextSpec.Parse(r.Context, out var include, out _);
        if (r.Restricted && include.Count == 0)
            return "выберите хотя бы одну программу или «Везде»";
        if (r.Value.Trim().Length == 0)
            return r.Action == "macro" ? "не выбран макрос" : "не заполнено значение";
        if (r.Action == "macro" && !_macroDefs.Any(m => string.Equals(m.Name.Trim(), r.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
            return $"макрос «{r.Value.Trim()}» не найден";
        return null;
    }

    private static bool ContextsOverlap(TriggerRow a, TriggerRow b)
    {
        if (!a.Restricted || !b.Restricted) return true;
        ContextSpec.Parse(a.Context, out var ia, out _);
        ContextSpec.Parse(b.Context, out var ib, out _);
        return ia.Intersect(ib, StringComparer.OrdinalIgnoreCase).Any();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Triggers page
    // ═══════════════════════════════════════════════════════════════

    private Control BuildTriggersPage(out Control cardEmpty)
    {
        // ── Left: search, filter, list ──────────────────────────────
        var btnNew = UiTheme.Btn("+ Новый", "primary");
        btnNew.Click += (_, _) => AddTrigger();
        ToolTip.SetTip(btnNew, "Ctrl+N");
        _search.TextChanged += (_, _) => RebuildList(_cur);
        _filter.SelectionChanged += (_, _) => { if (!_loading) RebuildList(_cur); };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(8, 8, 8, 8) };
        head.Children.Add(_search);
        btnNew.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(btnNew, 1);
        head.Children.Add(btnNew);
        _filter.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(_filter, 1);
        Grid.SetColumnSpan(_filter, 2);
        head.Children.Add(_filter);

        _list.Classes.Add("rows");
        _list.ItemTemplate = BuildTriggerTemplate();
        _list.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _cur = _list.SelectedItem as TriggerRow;
            LoadCard();
        };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Delete) { DeleteTrigger(); e.Handled = true; } };

        var leftDock = new DockPanel();
        var headBorder = new Border { BorderBrush = UiTheme.B(UiTheme.Divider), BorderThickness = new Thickness(0, 0, 0, 1), Child = head };
        DockPanel.SetDock(headBorder, Dock.Top);
        leftDock.Children.Add(headBorder);
        leftDock.Children.Add(_list);

        // ── Right: card ─────────────────────────────────────────────
        cardEmpty = new TextBlock
        {
            Text = "Выберите триггер слева или создайте новый (Ctrl+N).",
            Foreground = UiTheme.B(UiTheme.TextGrey),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var card = new StackPanel { Margin = new Thickness(18, 14, 18, 18) };

        var btnDup = UiTheme.Btn("Дублировать", small: true);
        var btnDel = UiTheme.Btn("Удалить", "danger", small: true);
        btnDup.Click += (_, _) => DuplicateTrigger();
        btnDel.Click += (_, _) => DeleteTrigger();
        ToolTip.SetTip(btnDup, "Ctrl+D");
        ToolTip.SetTip(btnDel, "Delete в списке");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new StackPanel { Children = { _cardTitle, _cardSubtitle } });
        var headButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Top, Children = { btnDup, btnDel } };
        Grid.SetColumn(headButtons, 1);
        header.Children.Add(headButtons);
        card.Children.Add(header);
        card.Children.Add(_issues);

        // 1. When
        card.Children.Add(UiTheme.SectionHeader("Когда срабатывает"));
        card.Children.Add(Segmented(_when, i =>
        {
            if (_cur == null) return;
            _cur.Type = TriggerRow.Types[i];
            ShowWhen();
        }));

        _whenText.Children.Add(UiTheme.FieldLabel("Набранный текст"));
        _whenText.Children.Add(_trigText);
        _trigText.TextChanged += (_, _) => { if (_cur != null && !_loading) _cur.Trigger = _trigText.Text ?? ""; };
        card.Children.Add(_whenText);

        _whenHotkey.Children.Add(UiTheme.FieldLabel("Сочетание клавиш"));
        _hotkeyBtn.HorizontalAlignment = HorizontalAlignment.Left;
        _hotkeyBtn.Click += async (_, _) => await RecordAsync();
        _whenHotkey.Children.Add(_hotkeyBtn);
        card.Children.Add(_whenHotkey);

        var chordLabel = UiTheme.FieldLabel("Зажать аккорд");
        var restLabel = UiTheme.FieldLabel("и набрать буквы");
        Grid.SetColumn(restLabel, 1);
        _whenLeader.RowDefinitions = new RowDefinitions("Auto,Auto");
        _whenLeader.Children.Add(chordLabel);
        _whenLeader.Children.Add(restLabel);
        _chordBtn.Margin = new Thickness(0, 0, 10, 0);
        _chordBtn.Click += async (_, _) => await RecordAsync();
        Grid.SetRow(_chordBtn, 1);
        _whenLeader.Children.Add(_chordBtn);
        Grid.SetRow(_leaderRest, 1);
        Grid.SetColumn(_leaderRest, 1);
        _whenLeader.Children.Add(_leaderRest);
        _leaderRest.TextChanged += (_, _) => { if (_cur != null && !_loading) _cur.Trigger = _leaderRest.Text ?? ""; };
        card.Children.Add(_whenLeader);
        card.Children.Add(_whenNote);

        // 2. What
        card.Children.Add(UiTheme.SectionHeader("Что делать"));
        _action.ItemsSource = ActionInfo.Select(a => a.Label).ToList();
        _action.HorizontalAlignment = HorizontalAlignment.Left;
        _action.SelectionChanged += (_, _) =>
        {
            if (_cur == null || _loading || _action.SelectedIndex < 0) return;
            _cur.Action = ActionInfo[_action.SelectedIndex].Key;
            ShowValueEditor();
        };
        card.Children.Add(_action);
        card.Children.Add(_actionNote);

        // 3. Value — one of: multiline text + tokens / path + browse / macro picker
        card.Children.Add(_valueLabel);
        _valueText.TextChanged += (_, _) => { if (_cur != null && !_loading) _cur.Value = (_valueText.Text ?? "").Replace("\r\n", "\n"); };
        card.Children.Add(_valueText);

        _tokens.Children.Add(new TextBlock { Text = "Вставить:", FontSize = UiTheme.FsCaption, Foreground = UiTheme.B(UiTheme.TextMuted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) });
        AddToken("Дата", "{date}", "Сегодняшняя дата (дд.мм.гггг)");
        AddToken("Время", "{time}", "Текущее время (чч:мм)");
        AddToken("Дата и время", "{datetime:dd.MM.yyyy HH:mm}", "Формат можно поменять после двоеточия");
        AddToken("Буфер обмена", "{clipboard}", "Текст из буфера обмена");
        AddToken("Курсор", "{cursor}", "Здесь окажется курсор после вставки");
        AddToken("Спросить…", "{input:Подпись}", "Перед вставкой появится окно для ввода", "Подпись");
        AddToken("Выбор…", "{choice:вариант 1|вариант 2}", "Перед вставкой появится список вариантов", "вариант 1|вариант 2");
        card.Children.Add(_tokens);

        _valuePath.Children.Add(_valueLine);
        _valueLine.TextChanged += (_, _) => { if (_cur != null && !_loading) _cur.Value = _valueLine.Text ?? ""; };
        _browseFile.Margin = new Thickness(6, 0, 0, 0);
        _browseFolder.Margin = new Thickness(6, 0, 0, 0);
        _browseFile.Click += async (_, _) => await BrowseAsync(folder: false);
        _browseFolder.Click += async (_, _) => await BrowseAsync(folder: true);
        Grid.SetColumn(_browseFile, 1);
        Grid.SetColumn(_browseFolder, 2);
        _valuePath.Children.Add(_browseFile);
        _valuePath.Children.Add(_browseFolder);
        card.Children.Add(_valuePath);

        _macroPick.SelectionChanged += (_, _) =>
        {
            if (_cur == null || _loading || _macroPick.SelectedItem is not string name) return;
            _cur.Value = name;
            UpdateMacroPreview();
        };
        var btnOpenMacro = UiTheme.Btn("Открыть шаги");
        btnOpenMacro.Click += (_, _) => OpenMacro(_cur?.Value);
        var btnNewMacro = UiTheme.Btn("+ Новый макрос", "ghost");
        btnNewMacro.Click += (_, _) =>
        {
            var m = AddMacro();
            if (_cur != null) _cur.Value = m.Def.Name;
            OpenMacro(m.Def.Name);
        };
        _valueMacro.Children.Add(_macroPick);
        _valueMacro.Children.Add(btnOpenMacro);
        _valueMacro.Children.Add(btnNewMacro);
        card.Children.Add(_valueMacro);
        card.Children.Add(_macroPreview);

        // 4. Where
        card.Children.Add(UiTheme.SectionHeader("Где работает"));
        card.Children.Add(Segmented(_where, i =>
        {
            if (_cur == null) return;
            _cur.Restricted = i == 1;
            ContextSpec.Parse(_cur.Context, out var include, out var exclude);
            _cur.Context = ContextSpec.Build(_cur.Restricted, include, exclude);
            ShowWhere();
            Changed();
        }));
        _includeBlock.Children.Add(UiTheme.FieldLabel("Программы"));
        _includeBlock.Children.Add(_includeChips);
        card.Children.Add(_includeBlock);
        card.Children.Add(UiTheme.FieldLabel("Кроме"));
        card.Children.Add(_excludeChips);

        var manualOk = UiTheme.Btn("Добавить", small: true);
        var manualCancel = UiTheme.Btn("Отмена", "ghost", small: true);
        manualOk.Margin = new Thickness(6, 0, 0, 0);
        manualOk.Click += (_, _) => CommitManual();
        manualCancel.Click += (_, _) => _manualEntry.IsVisible = false;
        _manualText.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { CommitManual(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _manualEntry.IsVisible = false; e.Handled = true; }
        };
        Grid.SetColumn(manualOk, 1);
        Grid.SetColumn(manualCancel, 2);
        _manualEntry.Children.Add(_manualText);
        _manualEntry.Children.Add(manualOk);
        _manualEntry.Children.Add(manualCancel);
        card.Children.Add(_manualEntry);

        card.Children.Add(new Note("Программа указывается именем процесса (acad, winword, chrome) или частью заголовка окна. " +
                                   "Кнопка «+ Добавить» показывает открытые сейчас окна."));

        _card.Content = card;

        var right = new Grid { Children = { _card, cardEmpty } };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("340,8,*") };
        var left = UiTheme.Panel(leftDock);
        var rightPanel = UiTheme.Panel(right);
        body.Children.Add(left);
        Grid.SetColumn(rightPanel, 2);
        body.Children.Add(rightPanel);
        return body;
    }

    private static IDataTemplate BuildTriggerTemplate() =>
        new FuncDataTemplate<TriggerRow>((_, _) =>
        {
            TextBlock Bound(string prop, double size, FontWeight weight = FontWeight.Normal, string? brushProp = null)
            {
                var t = new TextBlock { FontSize = size, FontWeight = weight, TextTrimming = TextTrimming.CharacterEllipsis };
                t.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(prop));
                if (brushProp != null) t.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding(brushProp));
                return t;
            }

            var title = Bound(nameof(TriggerRow.DisplayTrigger), UiTheme.FsBody, FontWeight.SemiBold);
            var tag = Bound(nameof(TriggerRow.KindTag), UiTheme.FsCaption);
            tag.Foreground = UiTheme.B(UiTheme.TextGrey);
            tag.Margin = new Thickness(8, 0, 0, 0);
            var subtitle = Bound(nameof(TriggerRow.Subtitle), UiTheme.FsCaption, brushProp: nameof(TriggerRow.SubtitleBrush));
            var where = Bound(nameof(TriggerRow.WhereText), UiTheme.FsCaption, brushProp: nameof(TriggerRow.WhereBrush));
            where.Margin = new Thickness(8, 0, 0, 0);
            where.MaxWidth = 120;
            where.TextAlignment = TextAlignment.Right;

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
                Margin = new Thickness(14, 7, 12, 7)
            };
            grid.Children.Add(title);
            Grid.SetColumn(tag, 1);
            grid.Children.Add(tag);
            Grid.SetRow(subtitle, 1);
            grid.Children.Add(subtitle);
            Grid.SetRow(where, 1);
            Grid.SetColumn(where, 1);
            grid.Children.Add(where);

            // Selection bar comes from the ListBoxItem style; a problem bar overrides it.
            var selectionBar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
            selectionBar.Classes.Add("selbar");
            var problemBar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
            problemBar.Bind(Border.BackgroundProperty, new Avalonia.Data.Binding(nameof(TriggerRow.StatusBrush)));

            return new Border
            {
                BorderBrush = UiTheme.B(UiTheme.Divider),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new Grid { Children = { grid, selectionBar, problemBar } }
            };
        });

    private StackPanel Segmented(ToggleButton[] buttons, Action<int> onSelect)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 2, 0, 0) };
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            buttons[i].Click += (_, _) =>
            {
                for (int k = 0; k < buttons.Length; k++) buttons[k].IsChecked = k == index;
                if (!_loading) onSelect(index);
            };
            panel.Children.Add(buttons[i]);
        }
        return panel;
    }

    private static void Check(ToggleButton[] buttons, int index)
    {
        for (int k = 0; k < buttons.Length; k++) buttons[k].IsChecked = k == index;
    }

    private void AddToken(string label, string token, string tip, string? select = null)
    {
        var b = UiTheme.Btn(label, small: true);
        b.Margin = new Thickness(0, 0, 4, 4);
        ToolTip.SetTip(b, $"{tip}\n{token}");
        b.Click += (_, _) =>
        {
            string text = _valueText.Text ?? "";
            int at = Math.Clamp(_valueText.CaretIndex, 0, text.Length);
            _valueText.Text = text.Insert(at, token);
            if (select != null)
            {
                int off = token.IndexOf(select, StringComparison.Ordinal);
                _valueText.SelectionStart = at + off;
                _valueText.SelectionEnd = at + off + select.Length;
            }
            else
            {
                _valueText.CaretIndex = at + token.Length;
            }
            _valueText.Focus();
        };
        _tokens.Children.Add(b);
    }

    // ── List ────────────────────────────────────────────────────────

    private void RebuildFilter()
    {
        bool prev = _loading;
        _loading = true;
        try
        {
            string? selected = _filter.SelectedItem as string;
            var options = new List<string> { "Все программы", "Только «везде»" };
            options.AddRange(_rows.Where(r => r.Restricted)
                .SelectMany(r => { ContextSpec.Parse(r.Context, out var inc, out _); return inc; })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
            _filter.ItemsSource = options;
            int idx = selected != null ? options.IndexOf(selected) : -1;
            _filter.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally { _loading = prev; }
    }

    private bool MatchesView(TriggerRow r)
    {
        int f = _filter.SelectedIndex;
        if (f == 1 && r.Restricted) return false;
        if (f >= 2)
        {
            ContextSpec.Parse(r.Context, out var include, out _);
            if (!r.Restricted || !include.Contains(_filter.SelectedItem as string ?? "", StringComparer.OrdinalIgnoreCase))
                return false;
        }
        string q = (_search.Text ?? "").Trim();
        return q.Length == 0
            || r.DisplayTrigger.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.Value.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.WhereText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildList(TriggerRow? select)
    {
        bool prev = _loading;
        _loading = true;
        try
        {
            _view = _rows.Where(MatchesView).ToList();
            _list.ItemsSource = _view;
            _cur = select != null && _view.Contains(select) ? select : select == null ? _view.FirstOrDefault() : null;
            _list.SelectedItem = _cur;
        }
        finally { _loading = prev; }
        LoadCard();
    }

    private void ShowRow(TriggerRow row)
    {
        if (!_view.Contains(row))
        {
            _loading = true;
            _search.Text = "";
            _filter.SelectedIndex = 0;
            _loading = false;
        }
        RebuildList(row);
        _list.ScrollIntoView(row);
    }

    // ── Card ────────────────────────────────────────────────────────

    private void LoadCard()
    {
        _cardEmpty.IsVisible = _cur == null;
        _card.IsVisible = _cur != null;
        if (_cur == null) return;

        bool prev = _loading;
        _loading = true;
        try
        {
            Check(_when, Array.IndexOf(TriggerRow.Types, _cur.Type));
            int ai = Array.FindIndex(ActionInfo, a => a.Key == _cur.Action.Trim().ToLowerInvariant());
            _action.SelectedIndex = ai >= 0 ? ai : 0;
            Check(_where, _cur.Restricted ? 1 : 0);
        }
        finally { _loading = prev; }

        _manualEntry.IsVisible = false;
        ShowWhen();
        ShowValueEditor();
        ShowWhere();
        ShowIssues();
        UpdateCardHeader();
        _card.Offset = default;
    }

    private void UpdateCardHeader()
    {
        if (_cur == null) return;
        _cardTitle.Text = _cur.DisplayTrigger;
        int ai = Array.FindIndex(ActionInfo, a => a.Key == _cur.Action);
        _cardSubtitle.Text = $"{(ai >= 0 ? ActionInfo[ai].Label : _cur.Action)} · {_cur.WhereText}";
    }

    private void ShowIssues()
    {
        if (_cur == null) return;
        _issues.Set(_cur.Problem == null ? ""
            : _cur.ProblemTone == Tone.Error ? "Нужно исправить: " + _cur.Problem + "."
            : _cur.Problem + ".", _cur.ProblemTone);
    }

    private void ShowWhen()
    {
        if (_cur == null) return;
        _whenText.IsVisible = _cur.Type == "Текст";
        _whenHotkey.IsVisible = _cur.Type == "Шорткат";
        _whenLeader.IsVisible = _cur.Type == "Лидер";
        string hk = _cur.Hotkey.Trim();
        _hotkeyBtn.Content = hk.Length > 0 ? $"{hk}   ·   изменить" : "Записать сочетание…";
        _chordBtn.Content = hk.Length > 0 ? $"{hk}   ·   изменить" : "Записать аккорд…";

        bool prev = _loading;
        _loading = true;
        _trigText.Text = _cur.Trigger;
        _leaderRest.Text = _cur.Trigger;
        _loading = prev;

        _whenNote.Set(_cur.Type switch
        {
            "Шорткат" => "Ctrl/Alt + клавиша или отдельная F1–F12. Системные сочетания (Ctrl+C, Ctrl+S, Alt+Tab…) назначить нельзя.",
            "Лидер"   => "Зажмите аккорд из 2–3 модификаторов (например Ctrl+Alt) и, не отпуская, наберите буквы. Во время набора в углу экрана видна подсказка.",
            _         => "Набранные символы сотрутся и заменятся результатом. Начинайте триггер с ! или @, чтобы он не срабатывал в обычном тексте."
        }, Tone.Neutral);
    }

    private void ShowValueEditor()
    {
        if (_cur == null) return;
        string a = _cur.Action.Trim().ToLowerInvariant();
        bool text = a is "text" or "richtext";
        bool macro = a == "macro";

        _valueText.IsVisible = text;
        _tokens.IsVisible = a == "text";
        _valuePath.IsVisible = !text && !macro;
        _browseFolder.IsVisible = a == "open";
        _valueMacro.IsVisible = macro;
        _macroPreview.IsVisible = macro;
        _valueLabel.Text = a switch
        {
            "text"     => "Текст для вставки",
            "richtext" => "RTF или путь к .rtf",
            "launch"   => "Программа и аргументы",
            "open"     => "Папка или файл",
            "script"   => "Команда",
            "lisp"     => "Файл LISP",
            _          => "Макрос"
        };
        _valueLine.Watermark = a switch
        {
            "lisp"   => @"C:\lisp\routines.lsp",
            "open"   => @"D:\Projects\{date}",
            "launch" => "notepad.exe",
            _        => @"python C:\scripts\job.py"
        };
        int ai = Array.FindIndex(ActionInfo, x => x.Key == a);
        _actionNote.Set(ai >= 0 ? ActionInfo[ai].Hint : "", Tone.Neutral);

        bool prev = _loading;
        _loading = true;
        _valueText.Text = _cur.Value;
        _valueLine.Text = _cur.Value;
        _loading = prev;
        if (macro) { RefreshMacroPick(); UpdateMacroPreview(); }
    }

    private void ShowWhere()
    {
        if (_cur == null) return;
        _includeBlock.IsVisible = _cur.Restricted;
        ContextSpec.Parse(_cur.Context, out var include, out var exclude);
        BuildChips(_includeChips, include, exclude: false);
        BuildChips(_excludeChips, exclude, exclude: true);
    }

    private void BuildChips(WrapPanel host, List<string> items, bool exclude)
    {
        host.Children.Clear();
        foreach (string item in items)
        {
            var chip = new Chip(item, exclude);
            chip.Removed += (_, _) => EditContext(exclude, list => list.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase)));
            host.Children.Add(chip);
        }
        var add = UiTheme.Btn(exclude ? "+ Исключение" : "+ Добавить", "ghost", small: true);
        add.Margin = new Thickness(0, 0, 0, 6);
        add.Click += (_, _) => ShowProgramMenu(add, exclude);
        host.Children.Add(add);
    }

    private void EditContext(bool exclude, Action<List<string>> edit)
    {
        if (_cur == null) return;
        ContextSpec.Parse(_cur.Context, out var include, out var excl);
        edit(exclude ? excl : include);
        _cur.Context = ContextSpec.Build(_cur.Restricted, include.Distinct(StringComparer.OrdinalIgnoreCase), excl.Distinct(StringComparer.OrdinalIgnoreCase));
        ShowWhere();
        if (!exclude) RebuildFilter();
    }

    private void ShowProgramMenu(Control anchor, bool exclude)
    {
        if (_cur == null) return;
        ContextSpec.Parse(_cur.Context, out var include, out var excl);
        var taken = exclude ? excl : include;

        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Items.Add(new MenuItem { Header = "Открытые окна", IsEnabled = false, FontSize = UiTheme.FsCaption });
        int own = Environment.ProcessId;
        var windows = new List<(string Name, string Title)>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (p.Id != own && p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0)
                    windows.Add((p.ProcessName.ToLowerInvariant(), p.MainWindowTitle));
            }
            catch { /* access denied for some system processes */ }
            finally { p.Dispose(); }
        }
        foreach (var group in windows.GroupBy(w => w.Name).OrderBy(g => g.Key))
        {
            string name = group.Key;
            if (taken.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            string title = group.First().Title;
            if (title.Length > 50) title = title[..50] + "…";
            var item = new MenuItem { Header = $"{name}   —   {title}" };
            item.Click += (_, _) => EditContext(exclude, list => list.Add(name));
            flyout.Items.Add(item);
        }
        if (windows.Count == 0)
            flyout.Items.Add(new MenuItem { Header = "(нет открытых окон)", IsEnabled = false });
        flyout.Items.Add(new Separator());
        var manual = new MenuItem { Header = "Ввести вручную…" };
        manual.Click += (_, _) =>
        {
            _manualExclude = exclude;
            _manualText.Text = "";
            _manualEntry.IsVisible = true;
            _manualText.Focus();
        };
        flyout.Items.Add(manual);
        flyout.ShowAt(anchor);
    }

    private void CommitManual()
    {
        string value = (_manualText.Text ?? "").Trim().TrimStart('!').Trim();
        _manualEntry.IsVisible = false;
        if (value.Length > 0 && !value.Contains(','))
            EditContext(_manualExclude, list => list.Add(value));
    }

    private async Task RecordAsync()
    {
        if (_cur == null) return;
        var recorder = new HotkeyRecorderWindow(_cur.Type == "Лидер");
        string? combo = await recorder.ShowDialog<string?>(this);
        if (string.IsNullOrEmpty(combo)) return;
        _cur.Hotkey = combo;
        ShowWhen();
    }

    private async Task BrowseAsync(bool folder)
    {
        if (_cur == null) return;
        string? path;
        if (folder)
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            path = picked.FirstOrDefault()?.TryGetLocalPath();
        }
        else
        {
            var types = _cur.Action == "lisp"
                ? new[] { new FilePickerFileType("LISP") { Patterns = new[] { "*.lsp", "*.fas", "*.vlx" } }, FilePickerFileTypes.All }
                : new[] { FilePickerFileTypes.All };
            var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = types });
            path = picked.FirstOrDefault()?.TryGetLocalPath();
        }
        if (string.IsNullOrEmpty(path)) return;

        if (_cur.Action is "script" or "launch")
        {
            if (path.Contains(' ')) path = $"\"{path}\"";
            if (_cur.Action == "script" && path.TrimEnd('"').EndsWith(".py", StringComparison.OrdinalIgnoreCase))
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
            var names = _macroDefs.Select(m => m.Name.Trim()).Where(n => n.Length > 0).ToList();
            _macroPick.ItemsSource = names;
            string cur = _cur?.Action == "macro" ? _cur.Value.Trim() : "";
            _macroPick.SelectedIndex = names.FindIndex(n => string.Equals(n, cur, StringComparison.OrdinalIgnoreCase));
        }
        finally { _loading = prev; }
    }

    private void UpdateMacroPreview()
    {
        var def = _macroDefs.FirstOrDefault(m => string.Equals(m.Name.Trim(), _cur?.Value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (def == null)
        {
            _macroPreview.Set(_macroDefs.Count == 0
                ? "Макросов пока нет — создайте первый кнопкой «+ Новый макрос»."
                : "Выберите макрос из списка.", Tone.Neutral);
            return;
        }
        var steps = MacroRow.RealSteps(def).ToList();
        string shown = string.Join("  →  ", steps.Take(6)) + (steps.Count > 6 ? $"  …ещё {steps.Count - 6}" : "");
        _macroPreview.Set(steps.Count == 0 ? "Макрос пустой." : shown, Tone.Neutral);
    }

    // ── Add / duplicate / delete ────────────────────────────────────

    private void AddTrigger()
    {
        var row = new TriggerRow { Trigger = "!", Action = "text" };
        row.PropertyChanged += OnRowChanged;
        int at = _cur != null ? _rows.IndexOf(_cur) + 1 : _rows.Count;
        _rows.Insert(at, row);
        Changed();
        ShowRow(row);
        _trigText.Focus();
        _trigText.CaretIndex = _trigText.Text?.Length ?? 0;
    }

    private void DuplicateTrigger()
    {
        if (_cur == null) return;
        var copy = TriggerRow.From(_cur.ToEntry());
        copy.Restricted = _cur.Restricted;
        copy.Context = _cur.Context;
        copy.PropertyChanged += OnRowChanged;
        _rows.Insert(_rows.IndexOf(_cur) + 1, copy);
        Changed();
        ShowRow(copy);
        _snack.Show("Создана копия — поменяйте триггер, чтобы не было конфликта");
    }

    private void DeleteTrigger()
    {
        if (_cur == null) return;
        var row = _cur;
        int at = _rows.IndexOf(row);
        int viewAt = _view.IndexOf(row);
        _rows.Remove(row);
        Changed();
        RebuildFilter();
        var rest = _view.Where(r => r != row).ToList();
        RebuildList(rest.Count > 0 ? rest[Math.Clamp(viewAt, 0, rest.Count - 1)] : null);
        _snack.Show($"Удалён «{row.DisplayTrigger}»", Tone.Neutral, "Вернуть", () =>
        {
            _rows.Insert(Math.Min(at, _rows.Count), row);
            Changed();
            RebuildFilter();
            ShowRow(row);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Macros page
    // ═══════════════════════════════════════════════════════════════

    /// <summary>List item for a macro: name plus "N шагов · триггеров: M".</summary>
    private sealed class MacroRow : INotifyPropertyChanged
    {
        private readonly SettingsWindow _owner;
        public MacroDef Def { get; }
        public string? Problem { get; private set; }

        public MacroRow(MacroDef def, SettingsWindow owner) { Def = def; _owner = owner; }

        public string Name => string.IsNullOrWhiteSpace(Def.Name) ? "(без имени)" : Def.Name;

        public string Summary
        {
            get
            {
                if (Problem != null) return Problem;
                int users = _owner.UsersOf(Def).Count;
                return Plural(RealSteps(Def).Count(), "шаг", "шага", "шагов") +
                       (users > 0 ? $" · триггеров: {users}" : " · не используется");
            }
        }

        public IBrush SummaryBrush => UiTheme.B(Problem != null ? UiTheme.Error : UiTheme.TextGrey);
        public IBrush StatusBrush => Problem != null ? UiTheme.B(UiTheme.Error) : Brushes.Transparent;

        public static IEnumerable<string> RealSteps(MacroDef def) =>
            def.Steps.Where(s => s.Trim().Length > 0 && !s.TrimStart().StartsWith('#'));

        public void Refresh()
        {
            string name = Def.Name.Trim();
            Problem = name.Length == 0 ? "у макроса нет имени"
                : _owner._macroDefs.Count(m => string.Equals(m.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) > 1
                    ? $"имя «{name}» повторяется"
                    : null;
            foreach (var p in new[] { nameof(Name), nameof(Summary), nameof(SummaryBrush), nameof(StatusBrush) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private List<TriggerRow> UsersOf(MacroDef def) =>
        _rows.Where(r => r.Action == "macro" && def.Name.Trim().Length > 0
                         && string.Equals(r.Value.Trim(), def.Name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    private Control BuildMacrosPage(out Control macroCard, out Control macroEmpty)
    {
        var btnNew = UiTheme.Btn("+ Новый макрос", "primary");
        btnNew.HorizontalAlignment = HorizontalAlignment.Left;
        btnNew.Click += (_, _) => { var m = AddMacro(); RebuildMacroList(m); _macroName.Focus(); _macroName.SelectAll(); };

        _macroList.Classes.Add("rows");
        _macroList.ItemTemplate = new FuncDataTemplate<MacroRow>((_, _) =>
        {
            var name = new TextBlock { FontSize = UiTheme.FsBody, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(MacroRow.Name)));
            var sub = new TextBlock { FontSize = UiTheme.FsCaption, TextTrimming = TextTrimming.CharacterEllipsis };
            sub.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(MacroRow.Summary)));
            sub.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding(nameof(MacroRow.SummaryBrush)));
            var selectionBar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
            selectionBar.Classes.Add("selbar");
            var problemBar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
            problemBar.Bind(Border.BackgroundProperty, new Avalonia.Data.Binding(nameof(MacroRow.StatusBrush)));
            return new Border
            {
                BorderBrush = UiTheme.B(UiTheme.Divider),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new Grid
                {
                    Children =
                    {
                        new StackPanel { Margin = new Thickness(14, 7, 12, 7), Children = { name, sub } },
                        selectionBar,
                        problemBar
                    }
                }
            };
        });
        _macroList.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _curMacro = _macroList.SelectedItem as MacroRow;
            LoadMacroCard();
        };
        _macroList.KeyDown += (_, e) => { if (e.Key == Key.Delete) { DeleteMacro(); e.Handled = true; } };

        var leftDock = new DockPanel();
        var head = new Border { Padding = new Thickness(8), BorderBrush = UiTheme.B(UiTheme.Divider), BorderThickness = new Thickness(0, 0, 0, 1), Child = btnNew };
        DockPanel.SetDock(head, Dock.Top);
        leftDock.Children.Add(head);
        leftDock.Children.Add(_macroList);

        macroEmpty = new TextBlock
        {
            Text = "Макрос — это последовательность шагов: ввод текста, клавиши, паузы, клики.\nСоздайте первый кнопкой «+ Новый макрос».",
            TextAlignment = TextAlignment.Center,
            Foreground = UiTheme.B(UiTheme.TextGrey),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var btnDel = UiTheme.Btn("Удалить", "danger", small: true);
        btnDel.VerticalAlignment = VerticalAlignment.Bottom;
        btnDel.Margin = new Thickness(12, 0, 0, 2);
        btnDel.Click += (_, _) => DeleteMacro();
        _macroName.TextChanged += (_, _) => RenameCurrentMacro(_macroName.Text ?? "");
        var nameGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        nameGrid.Children.Add(new StackPanel { Children = { UiTheme.FieldLabel("Имя макроса"), _macroName } });
        Grid.SetColumn(btnDel, 1);
        nameGrid.Children.Add(btnDel);

        var stepButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        stepButtons.Children.Add(new TextBlock { Text = "Добавить шаг:", FontSize = UiTheme.FsCaption, Foreground = UiTheme.B(UiTheme.TextMuted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) });
        AddStep(stepButtons, "Ввести текст", "type ", "текст", "Напечатать текст (токены {date}, {time} работают)");
        AddStep(stepButtons, "Нажать клавиши", "key ", "Ctrl+S", "Сочетание или клавиша: Ctrl+S, Enter, F5, Alt+Tab");
        AddStep(stepButtons, "Пауза", "sleep ", "300", "Подождать, мс");
        AddStep(stepButtons, "Клик", "click ", "100,200", "Клик мышью по координатам экрана x,y (также dclick, rclick)");
        AddStep(stepButtons, "Запустить", "run ", "notepad.exe", "Запустить программу или команду");
        AddStep(stepButtons, "Комментарий", "# ", "пояснение", "Строки с # пропускаются");

        _macroSteps.TextChanged += (_, _) =>
        {
            if (_curMacro == null || _loading) return;
            _curMacro.Def.Steps = (_macroSteps.Text ?? "").Replace("\r", "").Split('\n').ToList();
            _curMacro.Refresh();
            Changed();
        };

        var top = new StackPanel
        {
            Children = { nameGrid, _macroIssues, _macroUsage, UiTheme.SectionHeader("Шаги"), stepButtons }
        };
        var bottom = new Note("Один шаг на строку, выполняются сверху вниз. Пустые строки и строки с # пропускаются.");
        var cardDock = new DockPanel { Margin = new Thickness(18, 10, 18, 18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        cardDock.Children.Add(top);
        cardDock.Children.Add(bottom);
        cardDock.Children.Add(_macroSteps);
        macroCard = cardDock;

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("290,8,*") };
        body.Children.Add(UiTheme.Panel(leftDock));
        var right = UiTheme.Panel(new Grid { Children = { macroCard, macroEmpty } });
        Grid.SetColumn(right, 2);
        body.Children.Add(right);
        return body;
    }

    private void AddStep(WrapPanel host, string label, string prefix, string placeholder, string tip)
    {
        var b = UiTheme.Btn(label, small: true);
        b.Margin = new Thickness(0, 0, 4, 4);
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) =>
        {
            // Insert a new line after the caret's line, then select the placeholder.
            string text = (_macroSteps.Text ?? "").Replace("\r\n", "\n");
            int caret = Math.Clamp(_macroSteps.CaretIndex, 0, text.Length);
            int lineEnd = text.IndexOf('\n', caret);
            string line = prefix + placeholder;
            int at;
            if (lineEnd < 0)
            {
                bool needBreak = text.Length > 0 && !text.EndsWith('\n');
                at = text.Length + (needBreak ? 1 : 0);
                text = text + (needBreak ? "\n" : "") + line;
            }
            else
            {
                at = lineEnd + 1;
                text = text.Insert(at, line + "\n");
            }
            _macroSteps.Text = text;
            _macroSteps.SelectionStart = at + prefix.Length;
            _macroSteps.SelectionEnd = at + line.Length;
            _macroSteps.Focus();
        };
        host.Children.Add(b);
    }

    private void RebuildMacroList(MacroRow? select)
    {
        bool prev = _loading;
        _loading = true;
        try
        {
            foreach (var m in _macroRows) m.Refresh();
            _macroList.ItemsSource = _macroRows.ToList();
            _curMacro = select != null && _macroRows.Contains(select) ? select : null;
            _macroList.SelectedItem = _curMacro;
        }
        finally { _loading = prev; }
        LoadMacroCard();
    }

    private void LoadMacroCard()
    {
        _macroEmpty.IsVisible = _curMacro == null;
        _macroCard.IsVisible = _curMacro != null;
        if (_curMacro == null) return;
        bool prev = _loading;
        _loading = true;
        try
        {
            _macroName.Text = _curMacro.Def.Name;
            _macroSteps.Text = string.Join("\n", _curMacro.Def.Steps);
        }
        finally { _loading = prev; }
        UpdateMacroUsage();
    }

    private void UpdateMacroUsage()
    {
        if (_curMacro == null) return;
        var users = UsersOf(_curMacro.Def);
        _macroUsage.Set(users.Count == 0
            ? "Пока не используется ни одним триггером. Привязать: вкладка «Триггеры» → «Что делать» → «Выполнить макрос»."
            : "Запускается триггерами: " + string.Join(", ", users.Select(u => u.DisplayTrigger)), Tone.Neutral);
        _macroIssues.Set(_curMacro.Problem != null ? "Нужно исправить: " + _curMacro.Problem + "." : "", Tone.Error);
    }

    /// <summary>Rename the macro and keep triggers that point at it in sync.</summary>
    private void RenameCurrentMacro(string newName)
    {
        if (_curMacro == null || _loading) return;
        string old = _curMacro.Def.Name.Trim();
        _curMacro.Def.Name = newName;
        if (old.Length > 0)
            foreach (var r in _rows.Where(r => r.Action == "macro" && string.Equals(r.Value.Trim(), old, StringComparison.OrdinalIgnoreCase)))
                r.Value = newName.Trim();
        Changed();
    }

    private MacroRow AddMacro()
    {
        string name = "новый_макрос";
        for (int n = 2; _macroDefs.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = $"новый_макрос_{n}";
        var def = new MacroDef { Name = name, Steps = new List<string> { "key Ctrl+S" } };
        _macroDefs.Add(def);
        var row = new MacroRow(def, this);
        _macroRows.Add(row);
        Changed();
        return row;
    }

    private void OpenMacro(string? name)
    {
        var row = _macroRows.FirstOrDefault(m => string.Equals(m.Def.Name.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
        SelectTab(1);
        RebuildMacroList(row ?? _curMacro ?? _macroRows.FirstOrDefault());
    }

    private void DeleteMacro()
    {
        if (_curMacro == null) return;
        var row = _curMacro;
        int at = _macroRows.IndexOf(row);
        _macroRows.Remove(row);
        _macroDefs.Remove(row.Def);
        Changed();
        RebuildMacroList(_macroRows.Count > 0 ? _macroRows[Math.Min(at, _macroRows.Count - 1)] : null);
        int users = UsersOf(row.Def).Count;
        _snack.Show($"Удалён макрос «{row.Def.Name}»" + (users > 0 ? $" — он нужен триггерам: {users}" : ""),
            users > 0 ? Tone.Warning : Tone.Neutral, "Вернуть", () =>
            {
                _macroRows.Insert(Math.Min(at, _macroRows.Count), row);
                _macroDefs.Insert(Math.Min(at, _macroDefs.Count), row.Def);
                Changed();
                RebuildMacroList(row);
            });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Keyboard & close
    // ═══════════════════════════════════════════════════════════════

    private async void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control) return;
        bool triggers = _pageTriggers.IsVisible;
        switch (e.Key)
        {
            case Key.S:
                e.Handled = true;
                if (_dirty) await SaveAllAsync();
                break;
            case Key.N:
                e.Handled = true;
                if (triggers) AddTrigger();
                else { var m = AddMacro(); RebuildMacroList(m); _macroName.Focus(); _macroName.SelectAll(); }
                break;
            case Key.F when triggers:
                e.Handled = true;
                _search.Focus();
                _search.SelectAll();
                break;
            case Key.D when triggers:
                e.Handled = true;
                DuplicateTrigger();
                break;
        }
    }

    private async void OnClosingGuard(object? sender, WindowClosingEventArgs args)
    {
        if (_closeConfirmed || !_dirty)
            return;

        args.Cancel = true;
        if (_closingGuardActive)
            return;

        _closingGuardActive = true;
        try
        {
            UnsavedChangesChoice choice = await UnsavedChangesDialog.Show(this);
            if (choice == UnsavedChangesChoice.Cancel)
                return;
            if (choice == UnsavedChangesChoice.Save && !await SaveAllAsync())
                return;

            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closingGuardActive = false;
        }
    }

    /// <summary>Underline tab (.gb-subtab): text + 2px accent line when active.</summary>
    private sealed class UnderlineTab : Button
    {
        private readonly TextBlock _text = new() { FontSize = UiTheme.FsLabel };
        private readonly Border _line = new() { Height = 2, Margin = new Thickness(0, 7, 0, 0) };

        public UnderlineTab()
        {
            Classes.Add("ghost");
            Padding = new Thickness(12, 9, 12, 0);
            CornerRadius = new CornerRadius(0);
            Content = new StackPanel { Children = { _text, _line } };
            Active = false;
        }

        protected override Type StyleKeyOverride => typeof(Button);

        public string Label { set => _text.Text = value; }

        public bool Active
        {
            set
            {
                _text.FontWeight = value ? FontWeight.SemiBold : FontWeight.Normal;
                _text.Foreground = UiTheme.B(value ? UiTheme.Text : UiTheme.TextMuted);
                _line.Background = value ? UiTheme.B(UiTheme.Accent) : Brushes.Transparent;
            }
        }
    }

    private static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        string w = m10 == 1 && m100 != 11 ? one
                 : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? few
                 : many;
        return $"{n} {w}";
    }
}
