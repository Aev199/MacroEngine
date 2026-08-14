using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
        }
    }

    public string Context
    {
        get => _context;
        set => Set(ref _context, value);
    }

    public string Action
    {
        get => _action;
        set
        {
            if (Set(ref _action, value))
                Notify(nameof(ActionSummary));
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
/// Compact settings UI: rules are selected from a semantic list and edited in a
/// property inspector. Each list row shows both activation and result, so the
/// overview remains useful without becoming a dense editable table.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static readonly FontFamily MonoFont =
        new("Cascadia Mono,Consolas,monospace");

    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly List<MacroDef> _macroDefs;

    private readonly ObservableCollection<TriggerRow> _rows = new();
    private readonly ObservableCollection<string> _macroNames = new();
    private readonly ObservableCollection<string> _filterOptions = new();

    private readonly ListBox _triggerList;
    private readonly StackPanel _inspector;
    private readonly TextBlock _inspectorTitle;
    private readonly ComboBox _typeEditor;
    private readonly TextBox _triggerEditor;
    private readonly Button _hotkeyEditor;
    private readonly ComboBox _actionEditor;
    private readonly Button _valueEditor;
    private readonly TextBox _contextEditor;
    private readonly Button _deleteTrigger;
    private readonly Button _btnSave;
    private readonly ComboBox _filterCombo;
    private readonly TextBlock _triggerStatus;

    private string _filterContext = "Все";
    private bool _loading;
    private bool _filterLoading;
    private bool _inspectorLoading;
    private bool _dirty;
    private string _savedTriggerState = "";

    private readonly ListBox _macroList;
    private readonly TextBox _macroName;
    private readonly TextBox _macroSteps;
    private readonly Button _macroBtnSave;
    private readonly TextBlock _macroStatus;

    private int _macroCurrent = -1;
    private bool _macroLoading;
    private bool _macroDirty;
    private string _savedMacroState = "";

    private bool _closeConfirmed;
    private bool _closingGuardActive;

    public SettingsWindow(TriggerConfig config, MacroLibrary macros)
    {
        _config = config;
        _macros = macros;
        _macroDefs = macros.Load();

        Title = "MacroEngine — Настройки";
        Width = 920;
        Height = 560;
        MinWidth = 740;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        _filterCombo = new ComboBox
        {
            ItemsSource = _filterOptions,
            Width = 180,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _filterCombo.SelectionChanged += OnFilterChanged;

        _triggerStatus = BuildStatusText();
        _btnSave = new Button
        {
            Content = "Сохранить",
            IsEnabled = false,
            MinWidth = 90,
            Classes = { "accent" }
        };
        _deleteTrigger = new Button
        {
            Content = "Удалить",
            IsEnabled = false,
            MinWidth = 78
        };

        _triggerList = new ListBox
        {
            ItemTemplate = BuildTriggerTemplate()
        };
        _triggerList.SelectionChanged += (_, _) => ShowSelectedTrigger();

        _inspectorTitle = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 3)
        };

        _typeEditor = new ComboBox
        {
            ItemsSource = TriggerRow.Types,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _typeEditor.SelectionChanged += OnTypeChanged;

        _triggerEditor = new TextBox { Watermark = "!mail" };
        _triggerEditor.TextChanged += OnTriggerTextChanged;

        _hotkeyEditor = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        _hotkeyEditor.Click += OnHotkeyClick;

        _actionEditor = new ComboBox
        {
            ItemsSource = TriggerRow.Actions,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = MonoFont
        };
        _actionEditor.SelectionChanged += OnActionChanged;

        _valueEditor = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        _valueEditor.Click += OnValueClick;

        _contextEditor = new TextBox
        {
            FontFamily = MonoFont,
            Watermark = "*"
        };
        _contextEditor.TextChanged += OnContextChanged;
        _contextEditor.LostFocus += (_, _) =>
        {
            PopulateFilterOptions();
            if (_filterContext != "Все")
                ApplyFilter();
        };

        _inspector = BuildTriggerInspector();
        _inspector.IsEnabled = false;

        _macroList = new ListBox { ItemsSource = _macroNames };
        _macroName = new TextBox();
        _macroSteps = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = MonoFont
        };
        _macroBtnSave = new Button
        {
            Content = "Сохранить",
            IsEnabled = false,
            MinWidth = 90,
            Classes = { "accent" }
        };
        _macroStatus = BuildStatusText();

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Триггеры", Content = BuildTriggersTab() },
                new TabItem { Header = "Макросы", Content = BuildMacrosTab() }
            }
        };

        Content = new Border
        {
            Padding = new Thickness(8),
            Child = tabs
        };

        PopulateRows(config.Load());
        PopulateMacroList();

        Closing += OnClosingGuard;
    }

    private TriggerRow? SelectedTrigger => _triggerList.SelectedItem as TriggerRow;

    private static TextBlock BuildStatusText() => new()
    {
        Opacity = 0.58,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    private static IDataTemplate BuildTriggerTemplate() =>
        new FuncDataTemplate<TriggerRow>((_, _) =>
        {
            var trigger = new TextBlock
            {
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = FontWeight.Medium,
                FontSize = 13
            };
            trigger.Bind(TextBlock.TextProperty,
                new Avalonia.Data.Binding(nameof(TriggerRow.DisplayTrigger)));

            var summary = new TextBlock
            {
                Opacity = 0.68,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 11,
                Margin = new Thickness(0, 1, 0, 0)
            };
            summary.Bind(TextBlock.TextProperty,
                new Avalonia.Data.Binding(nameof(TriggerRow.ActionSummary)));

            var meaning = new StackPanel
            {
                Spacing = 0,
                Children = { trigger, summary }
            };

            var context = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Opacity = 0.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontFamily = MonoFont,
                FontSize = 11,
                Margin = new Thickness(12, 0, 0, 0)
            };
            context.Bind(TextBlock.TextProperty,
                new Avalonia.Data.Binding(nameof(TriggerRow.Context)));

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(4, 4, 5, 4)
            };
            grid.Children.Add(meaning);
            Grid.SetColumn(context, 1);
            grid.Children.Add(context);
            return grid;
        });

    private StackPanel BuildTriggerInspector() => new()
    {
        Margin = new Thickness(14, 1, 2, 0),
        Spacing = 7,
        Children =
        {
            _inspectorTitle,
            PropertyRow("Тип", _typeEditor),
            PropertyRow("Триггер", _triggerEditor),
            PropertyRow("Клавиши", _hotkeyEditor),
            PropertyRow("Действие", _actionEditor),
            PropertyRow("Значение", _valueEditor),
            PropertyRow("Контекст", _contextEditor)
        }
    };

    private static Control PropertyRow(string label, Control input)
    {
        var labelBlock = new TextBlock
        {
            Text = label,
            Opacity = 0.56,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("100,*")
        };
        row.Children.Add(labelBlock);
        Grid.SetColumn(input, 1);
        row.Children.Add(input);
        return row;
    }

    private Control BuildTriggersTab()
    {
        var syntax = new Button
        {
            Content = "Синтаксис",
            Padding = new Thickness(7, 3)
        };
        syntax.Click += async (_, _) => await MessageDialog.Show(
            this,
            "Синтаксис",
            "Контекст: * — везде; acad — AutoCAD; !browser — исключение. Значения разделяются запятыми.\n\n" +
            "Токены: {date}, {time}, {datetime:HH:mm}, {clipboard}, {input:подпись}, {choice:a|b|c}, {cursor}.\n\n" +
            "macro — имя макроса; open — путь; launch — команда запуска.");

        var filterLabel = new TextBlock
        {
            Text = "Контекст",
            Opacity = 0.56,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        };
        var filterBlock = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 0,
            Children = { filterLabel, _filterCombo }
        };

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(2, 2, 2, 7)
        };
        toolbar.Children.Add(filterBlock);
        Grid.SetColumn(syntax, 1);
        toolbar.Children.Add(syntax);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(9, 0, 9, 5)
        };
        header.Children.Add(ColumnHeader("Триггер / что делает"));
        var contextHeader = ColumnHeader("Контекст", HorizontalAlignment.Right);
        Grid.SetColumn(contextHeader, 1);
        header.Children.Add(contextHeader);

        var leftContent = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { header, _triggerList }
        };
        Grid.SetRow(_triggerList, 1);

        var left = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(0, 0, 10, 0),
            Child = leftContent
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,2*"),
            Children = { left, _inspector }
        };
        Grid.SetColumn(_inspector, 1);

        var add = new Button { Content = "Добавить", MinWidth = 78 };
        add.Click += OnAdd;
        _deleteTrigger.Click += OnDelete;
        _btnSave.Click += OnSave;

        var editButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { add, _deleteTrigger }
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 8, 0, 0)
        };
        footer.Children.Add(editButtons);
        Grid.SetColumn(_triggerStatus, 1);
        _triggerStatus.HorizontalAlignment = HorizontalAlignment.Right;
        _triggerStatus.Margin = new Thickness(8, 0);
        footer.Children.Add(_triggerStatus);
        Grid.SetColumn(_btnSave, 2);
        footer.Children.Add(_btnSave);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 5, 0, 0),
            Children = { toolbar, body, footer }
        };
        Grid.SetRow(body, 1);
        Grid.SetRow(footer, 2);
        return root;
    }

    private static TextBlock ColumnHeader(
        string text,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left) => new()
    {
        Text = text,
        Opacity = 0.46,
        FontSize = 10,
        HorizontalAlignment = horizontalAlignment
    };

    private void ShowSelectedTrigger()
    {
        TriggerRow? row = SelectedTrigger;
        _inspectorLoading = true;
        try
        {
            _inspectorTitle.Text = row?.DisplayTrigger ?? "Правило не выбрано";
            _typeEditor.SelectedItem = row?.Type;
            _triggerEditor.Text = row?.Trigger ?? "";
            _hotkeyEditor.Content = row?.HotkeyDisplay ?? "Назначить";
            _actionEditor.SelectedItem = row?.Action;
            _valueEditor.Content = row?.ValuePreview ?? "";
            _contextEditor.Text = row?.Context ?? "";
        }
        finally
        {
            _inspectorLoading = false;
        }

        _inspector.IsEnabled = row != null;
        _deleteTrigger.IsEnabled = row != null;
        UpdateInspectorAvailability();
    }

    private void UpdateInspectorAvailability()
    {
        TriggerRow? row = SelectedTrigger;
        _triggerEditor.IsEnabled = row?.TriggerEditable == true;
        _hotkeyEditor.IsEnabled = row?.HotkeyEditable == true;
        if (row != null)
        {
            _hotkeyEditor.Content = row.HotkeyDisplay;
            _valueEditor.Content = row.ValuePreview;
            _inspectorTitle.Text = row.DisplayTrigger;
        }
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_inspectorLoading || SelectedTrigger is not TriggerRow row
            || _typeEditor.SelectedItem is not string value)
            return;

        row.Type = value;
        UpdateInspectorAvailability();
    }

    private void OnTriggerTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (_inspectorLoading || SelectedTrigger is not TriggerRow row) return;
        row.Trigger = _triggerEditor.Text ?? "";
        _inspectorTitle.Text = row.DisplayTrigger;
    }

    private async void OnHotkeyClick(object? sender, RoutedEventArgs args)
    {
        if (SelectedTrigger is not TriggerRow row) return;
        var recorder = new HotkeyRecorderWindow(row.Type == "Лидер");
        string? combo = await recorder.ShowDialog<string?>(this);
        if (string.IsNullOrEmpty(combo)) return;
        row.Hotkey = combo;
        UpdateInspectorAvailability();
    }

    private void OnActionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_inspectorLoading || SelectedTrigger is not TriggerRow row
            || _actionEditor.SelectedItem is not string value)
            return;
        row.Action = value;
    }

    private async void OnValueClick(object? sender, RoutedEventArgs args)
    {
        if (SelectedTrigger is not TriggerRow row) return;

        IReadOnlyList<string>? macroNames = row.Action == "macro"
            ? _macroDefs.Select(m => m.Name.Trim())
                .Where(n => n.Length > 0)
                .ToList()
            : null;

        var editor = new ValueEditorWindow(row.Value, row.DisplayTrigger, macroNames);
        string? result = await editor.ShowDialog<string?>(this);
        if (result == null) return;
        row.Value = result;
        _valueEditor.Content = row.ValuePreview;
    }

    private void OnContextChanged(object? sender, TextChangedEventArgs args)
    {
        if (_inspectorLoading || SelectedTrigger is not TriggerRow row) return;
        row.Context = _contextEditor.Text ?? "";
    }

    private void PopulateRows(IEnumerable<TriggerEntry> triggers)
    {
        _loading = true;
        try
        {
            _rows.Clear();
            foreach (TriggerEntry trigger in triggers)
            {
                var row = TriggerRow.From(trigger);
                AttachDirtyTracking(row);
                _rows.Add(row);
            }
            _savedTriggerState = CurrentTriggerState();
        }
        finally
        {
            _loading = false;
        }

        PopulateFilterOptions();
        ApplyFilter();
        RefreshTriggerDirtyState();
    }

    private void AttachDirtyTracking(TriggerRow row)
    {
        row.PropertyChanged += (_, args) =>
        {
            if (_loading) return;
            if (args.PropertyName is nameof(TriggerRow.Type)
                or nameof(TriggerRow.Trigger)
                or nameof(TriggerRow.Hotkey)
                or nameof(TriggerRow.Value)
                or nameof(TriggerRow.Context)
                or nameof(TriggerRow.Action))
            {
                RefreshTriggerDirtyState();
            }
        };
    }

    private string CurrentTriggerState() =>
        SettingsStateFingerprint.Triggers(_rows.Select(row => row.ToEntry()));

    private void RefreshTriggerDirtyState()
    {
        if (_loading) return;
        _dirty = CurrentTriggerState() != _savedTriggerState;
        _btnSave.IsEnabled = _dirty;
        _triggerStatus.Text = _dirty ? "Не сохранено" : "";
    }

    private void PopulateFilterOptions()
    {
        var contexts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasGlobal = false;
        foreach (TriggerRow row in _rows)
        {
            foreach (string part in row.Context.Split(',', StringSplitOptions.TrimEntries))
            {
                if (part.Length == 0) continue;
                if (part == "*") hasGlobal = true;
                else contexts.Add(part);
            }
        }

        var available = new HashSet<string>(contexts, StringComparer.OrdinalIgnoreCase)
            { "Все" };
        if (hasGlobal) available.Add("Глобальные");
        if (!available.Contains(_filterContext))
            _filterContext = "Все";

        _filterLoading = true;
        try
        {
            _filterOptions.Clear();
            _filterOptions.Add("Все");
            if (hasGlobal) _filterOptions.Add("Глобальные");
            foreach (string context in contexts)
                _filterOptions.Add(context);
            _filterCombo.SelectedItem = _filterContext;
        }
        finally
        {
            _filterLoading = false;
        }
    }

    private void OnFilterChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_filterLoading || _filterCombo.SelectedItem is not string selected) return;
        _filterContext = selected;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        TriggerRow? selected = SelectedTrigger;
        IReadOnlyList<TriggerRow> visible;

        if (_filterContext == "Все")
        {
            visible = _rows.ToList();
        }
        else
        {
            visible = _rows.Where(row =>
            {
                var parts = row.Context.Split(',', StringSplitOptions.TrimEntries);
                return _filterContext == "Глобальные"
                    ? parts.Any(p => p == "*")
                    : parts.Any(p => string.Equals(
                        p, _filterContext, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        }

        _triggerList.ItemsSource = visible;
        if (selected != null && visible.Contains(selected))
            _triggerList.SelectedItem = selected;
        else
            _triggerList.SelectedItem = visible.FirstOrDefault();

        ShowSelectedTrigger();
    }

    private void OnAdd(object? sender, RoutedEventArgs args)
    {
        var row = new TriggerRow { Trigger = "!new", Value = "текст подстановки" };
        AttachDirtyTracking(row);
        _rows.Add(row);

        _filterContext = "Все";
        PopulateFilterOptions();
        ApplyFilter();
        _triggerList.SelectedItem = row;
        RefreshTriggerDirtyState();
        _triggerEditor.Focus();
        _triggerEditor.SelectAll();
    }

    private async void OnDelete(object? sender, RoutedEventArgs args)
    {
        if (SelectedTrigger is not TriggerRow row) return;

        if (!await ConfirmDialog.Show(this, $"Удалить «{row.DisplayTrigger}»?", "Удалить"))
            return;

        _rows.Remove(row);
        PopulateFilterOptions();
        ApplyFilter();
        RefreshTriggerDirtyState();
    }

    private async void OnSave(object? sender, RoutedEventArgs args) =>
        await SaveTriggersAsync();

    private async Task<bool> SaveTriggersAsync()
    {
        var entries = new List<TriggerEntry>();

        for (int i = 0; i < _rows.Count; i++)
        {
            TriggerRow row = _rows[i];
            string type = row.Type;
            string trigger = row.Trigger.Trim();
            string hotkey = row.Hotkey.Trim();

            string? error = null;
            if (type == "Текст" && trigger.Length == 0)
                error = $"Правило {i + 1}: поле «Триггер» не может быть пустым.";
            else if (type is "Шорткат" or "Лидер" && hotkey.Length == 0)
                error = $"Правило {i + 1}: сочетание клавиш не записано.";
            else if (type == "Лидер" && trigger.Length == 0)
                error = $"Правило {i + 1}: для лидера укажите остаток триггера, например gm.";
            else if (type == "Шорткат" && !HotkeyRules.TryValidateShortcut(hotkey, out string shortcutError))
                error = $"Правило {i + 1}: {shortcutError}";
            else if (type == "Лидер" && !HotkeyRules.TryValidateLeader(hotkey, out string leaderError))
                error = $"Правило {i + 1}: {leaderError}";
            else if (type == "Шорткат" && SystemHotkeys.IsSystem(hotkey))
                error = $"Правило {i + 1}: «{hotkey}» — системное сочетание, его нельзя назначить.";
            else if (type == "Лидер" && SystemHotkeys.IsSystem($"{hotkey}+{trigger}"))
                error = $"Правило {i + 1}: «{hotkey}+{trigger}» образует системное сочетание.";

            if (error != null)
            {
                await MessageDialog.Show(this, "Ошибка", error);
                return false;
            }

            entries.Add(row.ToEntry());
        }

        var conflicts = FindConflicts(entries);
        if (conflicts.Count > 0)
        {
            await MessageDialog.Show(this, "Предупреждение",
                "Возможные конфликты:\n\n" + string.Join("\n", conflicts));
        }

        try
        {
            _config.Save(entries);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Trigger config save failed: {ex.GetType().Name}: {ex.Message}");
            await MessageDialog.Show(this, "Ошибка сохранения",
                "Не удалось сохранить триггеры.\n\n" + ex.Message);
            return false;
        }

        _savedTriggerState = SettingsStateFingerprint.Triggers(entries);
        RefreshTriggerDirtyState();
        PopulateFilterOptions();
        _triggerStatus.Text = $"Сохранено: {entries.Count}";
        return true;
    }

    private static List<string> FindConflicts(IReadOnlyList<TriggerEntry> entries)
    {
        var conflicts = new List<string>();
        for (int i = 0; i < entries.Count; i++)
        {
            for (int j = i + 1; j < entries.Count; j++)
            {
                TriggerEntry a = entries[i];
                TriggerEntry b = entries[j];

                if (a.Trigger.Length > 0
                    && a.Trigger == b.Trigger
                    && string.IsNullOrEmpty(a.Hotkey)
                    && string.IsNullOrEmpty(b.Hotkey)
                    && string.IsNullOrEmpty(a.Leader)
                    && string.IsNullOrEmpty(b.Leader)
                    && ContextsOverlap(a.Context, b.Context))
                {
                    conflicts.Add($"Правила {i + 1} и {j + 1}: триггер «{a.Trigger}»");
                }

                if (!string.IsNullOrEmpty(a.Hotkey)
                    && !string.IsNullOrEmpty(b.Hotkey)
                    && string.Equals(
                        HotkeyRules.Normalize(a.Hotkey),
                        HotkeyRules.Normalize(b.Hotkey),
                        StringComparison.OrdinalIgnoreCase)
                    && ContextsOverlap(a.Context, b.Context))
                {
                    conflicts.Add($"Правила {i + 1} и {j + 1}: шорткат «{a.Hotkey}»");
                }

                if (!string.IsNullOrEmpty(a.Leader)
                    && !string.IsNullOrEmpty(b.Leader)
                    && string.Equals(
                        HotkeyRules.Normalize(a.Leader),
                        HotkeyRules.Normalize(b.Leader),
                        StringComparison.OrdinalIgnoreCase)
                    && a.Trigger == b.Trigger
                    && ContextsOverlap(a.Context, b.Context))
                {
                    conflicts.Add($"Правила {i + 1} и {j + 1}: лидер «{a.Leader}+{a.Trigger}»");
                }
            }
        }

        return conflicts;
    }

    private static bool ContextsOverlap(string a, string b)
    {
        var positiveA = a.Split(',', StringSplitOptions.TrimEntries)
            .Where(value => !value.StartsWith('!'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var positiveB = b.Split(',', StringSplitOptions.TrimEntries)
            .Where(value => !value.StartsWith('!'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (positiveA.Contains("*") || positiveB.Contains("*")) return true;
        return positiveA.Overlaps(positiveB);
    }

    private Control BuildMacrosTab()
    {
        var add = new Button { Content = "Добавить" };
        var delete = new Button { Content = "Удалить" };
        add.Click += OnMacroAdd;
        delete.Click += OnMacroDelete;

        var leftButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 7, 0, 0),
            Children = { add, delete }
        };

        var leftContent = new DockPanel();
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        leftContent.Children.Add(leftButtons);
        leftContent.Children.Add(_macroList);
        _macroList.SelectionChanged += OnMacroListSelected;

        var left = new Border
        {
            Width = 220,
            Padding = new Thickness(0, 0, 10, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = leftContent
        };

        _macroName.TextChanged += OnMacroNameChanged;
        _macroSteps.TextChanged += OnMacroStepsChanged;

        var syntax = new Button
        {
            Content = "Синтаксис",
            Padding = new Thickness(7, 3),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        syntax.Click += async (_, _) => await MessageDialog.Show(
            this,
            "Синтаксис макросов",
            "Одна команда на строку:\n" +
            "type <текст>\nkey <сочетание>\nsleep <мс>\nclick x,y\ndclick x,y\nrclick x,y\nrun <команда>\n\n" +
            "Строки, начинающиеся с #, игнорируются.");

        var nameRow = PropertyRow("Имя", _macroName);

        var editorHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 4, 0, 5)
        };
        editorHeader.Children.Add(new TextBlock
        {
            Text = "Шаги",
            Opacity = 0.56,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(syntax, 1);
        editorHeader.Children.Add(syntax);

        _macroBtnSave.Click += OnMacroSave;
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 8, 0, 0)
        };
        footer.Children.Add(_macroStatus);
        Grid.SetColumn(_macroBtnSave, 1);
        footer.Children.Add(_macroBtnSave);

        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(14, 1, 0, 0),
            Children = { nameRow, editorHeader, _macroSteps, footer }
        };
        Grid.SetRow(editorHeader, 1);
        Grid.SetRow(_macroSteps, 2);
        Grid.SetRow(footer, 3);

        var root = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);
        return root;
    }

    private void PopulateMacroList()
    {
        _macroLoading = true;
        try
        {
            _macroNames.Clear();
            foreach (MacroDef macro in _macroDefs)
                _macroNames.Add(macro.Name);

            _macroCurrent = -1;
            if (_macroDefs.Count > 0)
            {
                _macroList.SelectedIndex = 0;
                LoadMacroIntoEditor(0);
            }
            else
            {
                LoadMacroIntoEditor(-1);
            }
            _savedMacroState = SettingsStateFingerprint.Macros(_macroDefs);
        }
        finally
        {
            _macroLoading = false;
        }

        RefreshMacroDirtyState();
    }

    private void OnMacroListSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (_macroLoading) return;
        LoadMacroIntoEditor(_macroList.SelectedIndex);
    }

    private void LoadMacroIntoEditor(int index)
    {
        bool previous = _macroLoading;
        _macroLoading = true;
        try
        {
            _macroCurrent = index;
            if (index >= 0 && index < _macroDefs.Count)
            {
                _macroName.Text = _macroDefs[index].Name;
                _macroSteps.Text = string.Join(Environment.NewLine, _macroDefs[index].Steps);
                _macroName.IsEnabled = true;
                _macroSteps.IsEnabled = true;
            }
            else
            {
                _macroName.Text = "";
                _macroSteps.Text = "";
                _macroName.IsEnabled = false;
                _macroSteps.IsEnabled = false;
            }
        }
        finally
        {
            _macroLoading = previous;
        }
    }

    private void OnMacroNameChanged(object? sender, TextChangedEventArgs args)
    {
        if (_macroLoading || _macroCurrent < 0 || _macroCurrent >= _macroDefs.Count)
            return;

        MacroDef current = _macroDefs[_macroCurrent];
        string oldName = current.Name;
        string newName = _macroName.Text ?? "";
        if (oldName == newName) return;

        current.Name = newName;
        _macroNames[_macroCurrent] = newName;
        RenameMacroReferences(oldName, newName);
        RefreshMacroDirtyState();
    }

    private void OnMacroStepsChanged(object? sender, TextChangedEventArgs args)
    {
        if (_macroLoading || _macroCurrent < 0 || _macroCurrent >= _macroDefs.Count)
            return;

        _macroDefs[_macroCurrent].Steps = ParseMacroSteps(_macroSteps.Text ?? "");
        RefreshMacroDirtyState();
    }

    private static List<string> ParseMacroSteps(string text) =>
        text.Replace("\r", "").Split('\n').ToList();

    private void RenameMacroReferences(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return;

        foreach (TriggerRow row in _rows)
        {
            if (row.Action == "macro"
                && string.Equals(row.Value, oldName, StringComparison.OrdinalIgnoreCase))
            {
                row.Value = newName;
            }
        }

        if (SelectedTrigger is TriggerRow selected)
            _valueEditor.Content = selected.ValuePreview;
    }

    private void OnMacroAdd(object? sender, RoutedEventArgs args)
    {
        const string baseName = "новый_макрос";
        string name = baseName;
        int suffix = 2;
        var existing = _macroDefs.Select(m => m.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (existing.Contains(name))
            name = $"{baseName}_{suffix++}";

        var macro = new MacroDef
        {
            Name = name,
            Steps = new List<string> { "key Ctrl+S" }
        };
        _macroDefs.Add(macro);
        _macroNames.Add(name);
        _macroList.SelectedIndex = _macroDefs.Count - 1;
        RefreshMacroDirtyState();
        _macroName.Focus();
        _macroName.SelectAll();
    }

    private async void OnMacroDelete(object? sender, RoutedEventArgs args)
    {
        int index = _macroList.SelectedIndex;
        if (index < 0 || index >= _macroDefs.Count) return;

        string name = _macroDefs[index].Name;
        if (!await ConfirmDialog.Show(this, $"Удалить макрос «{name}»?", "Удалить"))
            return;

        _macroLoading = true;
        try
        {
            _macroDefs.RemoveAt(index);
            _macroNames.RemoveAt(index);
            _macroCurrent = -1;
            _macroList.SelectedIndex = _macroDefs.Count == 0
                ? -1
                : Math.Min(index, _macroDefs.Count - 1);
        }
        finally
        {
            _macroLoading = false;
        }

        LoadMacroIntoEditor(_macroList.SelectedIndex);
        RefreshMacroDirtyState();
    }

    private async void OnMacroSave(object? sender, RoutedEventArgs args) =>
        await SaveMacrosAsync();

    private async Task<bool> SaveMacrosAsync()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _macroDefs.Count; i++)
        {
            MacroDef macro = _macroDefs[i];
            string oldName = macro.Name;
            string normalizedName = oldName.Trim();

            if (normalizedName.Length == 0)
            {
                await MessageDialog.Show(this, "Ошибка", "У каждого макроса должно быть имя.");
                return false;
            }

            if (!seen.Add(normalizedName))
            {
                await MessageDialog.Show(this, "Ошибка",
                    $"Имя макроса «{normalizedName}» повторяется. Имена должны быть уникальными.");
                return false;
            }

            if (oldName != normalizedName)
            {
                macro.Name = normalizedName;
                _macroNames[i] = normalizedName;
                RenameMacroReferences(oldName, normalizedName);
            }
        }

        try
        {
            _macros.Save(_macroDefs);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Macro library save failed: {ex.GetType().Name}: {ex.Message}");
            await MessageDialog.Show(this, "Ошибка сохранения",
                "Не удалось сохранить макросы.\n\n" + ex.Message);
            return false;
        }

        _savedMacroState = SettingsStateFingerprint.Macros(_macroDefs);
        RefreshMacroDirtyState();
        _macroStatus.Text = $"Сохранено: {_macroDefs.Count}";
        return true;
    }

    private void RefreshMacroDirtyState()
    {
        if (_macroLoading) return;
        _macroDirty = SettingsStateFingerprint.Macros(_macroDefs) != _savedMacroState;
        _macroBtnSave.IsEnabled = _macroDirty;
        _macroStatus.Text = _macroDirty ? "Не сохранено" : "";
    }

    private async void OnClosingGuard(object? sender, WindowClosingEventArgs args)
    {
        RefreshTriggerDirtyState();
        RefreshMacroDirtyState();

        if (_closeConfirmed || (!_dirty && !_macroDirty))
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

            if (choice == UnsavedChangesChoice.Save)
            {
                if (_macroDirty && !await SaveMacrosAsync())
                    return;
                if (_dirty && !await SaveTriggersAsync())
                    return;
            }

            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closingGuardActive = false;
        }
    }
}
