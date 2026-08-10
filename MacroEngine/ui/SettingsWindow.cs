using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
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
            if (Set(ref _value, value))
                Notify(nameof(ValuePreview));
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
        set => Set(ref _action, value);
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
            if (Value.Length == 0) return "Пустое значение";
            string oneLine = Value.Replace("\r", "").Replace("\n", " / ");
            return oneLine.Length > 54 ? oneLine[..54] + "..." : oneLine;
        }
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
/// Compact settings UI: rules are selected from a list and edited in an inspector.
/// Saving remains task-based so validation and close guarding keep their old safety semantics.
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

    private readonly ListBox _triggerList;
    private readonly StackPanel _inspector;
    private readonly TextBlock _inspectorTitle;
    private readonly Button _deleteTrigger;
    private readonly Button _btnSave;
    private readonly WrapPanel _filterPanel;
    private readonly TextBlock _triggerStatus;

    private string _filterContext = "Все";
    private bool _dirty;
    private bool _loading;

    private readonly ListBox _macroList;
    private readonly TextBox _macroName;
    private readonly TextBox _macroSteps;
    private readonly Button _macroBtnSave;
    private readonly TextBlock _macroStatus;

    private int _macroCurrent = -1;
    private bool _macroLoading;
    private bool _macroDirty;

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
        MinWidth = 760;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        _filterPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        _triggerStatus = BuildStatusText();
        _btnSave = new Button
        {
            Content = "Сохранить",
            IsEnabled = false,
            MinWidth = 92,
            Classes = { "accent" }
        };
        _deleteTrigger = new Button
        {
            Content = "Удалить",
            IsEnabled = false,
            MinWidth = 82
        };

        _triggerList = new ListBox
        {
            ItemTemplate = BuildTriggerTemplate()
        };
        _triggerList.SelectionChanged += (_, _) => ShowSelectedTrigger();

        _inspectorTitle = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _inspectorTitle.Bind(TextBlock.TextProperty,
            new Binding(nameof(TriggerRow.DisplayTrigger)));

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
            MinWidth = 92,
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
            Padding = new Thickness(10),
            Child = tabs
        };

        PopulateRows(config.Load());
        PopulateFilters();
        PopulateMacroList();

        Closing += OnClosingGuard;
    }

    private static TextBlock BuildStatusText() => new()
    {
        Opacity = 0.62,
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    private static IDataTemplate BuildTriggerTemplate() =>
        new FuncDataTemplate<TriggerRow>((_, _) =>
        {
            var trigger = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = FontWeight.Medium
            };
            trigger.Bind(TextBlock.TextProperty,
                new Binding(nameof(TriggerRow.DisplayTrigger)));

            var action = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.72,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontFamily = MonoFont,
                FontSize = 12
            };
            action.Bind(TextBlock.TextProperty,
                new Binding(nameof(TriggerRow.Action)));

            var context = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Opacity = 0.58,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontFamily = MonoFont,
                FontSize = 12
            };
            context.Bind(TextBlock.TextProperty,
                new Binding(nameof(TriggerRow.Context)));

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("2*,1.2*,1*"),
                Margin = new Thickness(4, 4)
            };
            grid.Children.Add(trigger);
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
            Grid.SetColumn(context, 2);
            grid.Children.Add(context);
            return grid;
        });

    private StackPanel BuildTriggerInspector()
    {
        var type = new ComboBox
        {
            ItemsSource = TriggerRow.Types,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        type.Bind(ComboBox.SelectedItemProperty,
            new Binding(nameof(TriggerRow.Type)) { Mode = BindingMode.TwoWay });

        var trigger = new TextBox
        {
            Watermark = "Например: !mail"
        };
        trigger.Bind(TextBox.TextProperty,
            new Binding(nameof(TriggerRow.Trigger)) { Mode = BindingMode.TwoWay });
        trigger.Bind(InputElement.IsEnabledProperty,
            new Binding(nameof(TriggerRow.TriggerEditable)));

        var hotkey = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        hotkey.Bind(ContentControl.ContentProperty,
            new Binding(nameof(TriggerRow.HotkeyDisplay)));
        hotkey.Bind(InputElement.IsEnabledProperty,
            new Binding(nameof(TriggerRow.HotkeyEditable)));
        hotkey.Click += async (_, _) =>
        {
            if (_triggerList.SelectedItem is not TriggerRow row) return;
            var recorder = new HotkeyRecorderWindow(row.Type == "Лидер");
            string? combo = await recorder.ShowDialog<string?>(this);
            if (!string.IsNullOrEmpty(combo))
                row.Hotkey = combo;
        };

        var action = new ComboBox
        {
            ItemsSource = TriggerRow.Actions,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontFamily = MonoFont
        };
        action.Bind(ComboBox.SelectedItemProperty,
            new Binding(nameof(TriggerRow.Action)) { Mode = BindingMode.TwoWay });

        var value = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        value.Bind(ContentControl.ContentProperty,
            new Binding(nameof(TriggerRow.ValuePreview)));
        value.Click += async (_, _) =>
        {
            if (_triggerList.SelectedItem is not TriggerRow row) return;

            IReadOnlyList<string>? macroNames = row.Action == "macro"
                ? _macroDefs.Select(m => m.Name.Trim())
                    .Where(n => n.Length > 0)
                    .ToList()
                : null;

            var editor = new ValueEditorWindow(row.Value, row.DisplayTrigger, macroNames);
            string? result = await editor.ShowDialog<string?>(this);
            if (result != null)
                row.Value = result;
        };

        var context = new TextBox
        {
            FontFamily = MonoFont,
            Watermark = "*"
        };
        context.Bind(TextBox.TextProperty,
            new Binding(nameof(TriggerRow.Context)) { Mode = BindingMode.TwoWay });

        return new StackPanel
        {
            Margin = new Thickness(18, 2, 4, 0),
            Spacing = 10,
            Children =
            {
                _inspectorTitle,
                Field("Тип", type),
                Field("Триггер", trigger),
                Field("Сочетание / лидер", hotkey),
                Field("Действие", action),
                Field("Значение", value),
                Field("Контекст", context)
            }
        };
    }

    private static Control Field(string label, Control input) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock
            {
                Text = label,
                Opacity = 0.6,
                FontSize = 12
            },
            input
        }
    };

    private Control BuildTriggersTab()
    {
        var syntax = new Button
        {
            Content = "Синтаксис",
            Padding = new Thickness(8, 4)
        };
        syntax.Click += async (_, _) => await MessageDialog.Show(
            this,
            "Синтаксис",
            "Контекст: * — везде; acad — AutoCAD; !browser — исключение. Значения разделяются запятыми.\n\n" +
            "Токены: {date}, {time}, {datetime:HH:mm}, {clipboard}, {input:подпись}, {choice:a|b|c}, {cursor}.\n\n" +
            "Действия macro/open/launch используют значение правила как имя макроса, путь или команду.");

        var toolbar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 2, 0, 8)
        };
        toolbar.Children.Add(_filterPanel);
        Grid.SetColumn(syntax, 1);
        toolbar.Children.Add(syntax);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,1.2*,1*"),
            Margin = new Thickness(10, 0, 10, 6)
        };
        header.Children.Add(ColumnHeader("Триггер"));
        var actionHeader = ColumnHeader("Действие");
        Grid.SetColumn(actionHeader, 1);
        header.Children.Add(actionHeader);
        var contextHeader = ColumnHeader("Контекст", HorizontalAlignment.Right);
        Grid.SetColumn(contextHeader, 2);
        header.Children.Add(contextHeader);

        var leftContent = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { header, _triggerList }
        };
        Grid.SetRow(_triggerList, 1);

        var left = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Padding = new Thickness(0, 0, 12, 0),
            Child = leftContent
        };

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("5*,4*"),
            Children = { left, _inspector }
        };
        Grid.SetColumn(_inspector, 1);

        var add = new Button { Content = "Добавить", MinWidth = 82 };
        add.Click += OnAdd;
        _deleteTrigger.Click += OnDelete;
        _btnSave.Click += OnSave;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { add, _deleteTrigger, _btnSave }
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 10, 0, 0)
        };
        footer.Children.Add(_triggerStatus);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 6, 0, 0),
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
        Opacity = 0.5,
        FontSize = 11,
        HorizontalAlignment = horizontalAlignment
    };

    private void ShowSelectedTrigger()
    {
        TriggerRow? row = _triggerList.SelectedItem as TriggerRow;
        _inspector.DataContext = row;
        _inspector.IsEnabled = row != null;
        _deleteTrigger.IsEnabled = row != null;
    }

    private void PopulateRows(IEnumerable<TriggerEntry> triggers)
    {
        _loading = true;
        try
        {
            _rows.Clear();
            foreach (var trigger in triggers)
            {
                var row = TriggerRow.From(trigger);
                AttachDirtyTracking(row);
                _rows.Add(row);
            }

            _dirty = false;
            _btnSave.IsEnabled = false;
            _triggerStatus.Text = "";
        }
        finally
        {
            _loading = false;
        }

        ApplyFilter();
    }

    private void AttachDirtyTracking(TriggerRow row)
    {
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(TriggerRow.Type)
                or nameof(TriggerRow.Trigger)
                or nameof(TriggerRow.Hotkey)
                or nameof(TriggerRow.Value)
                or nameof(TriggerRow.Context)
                or nameof(TriggerRow.Action))
            {
                MarkDirty();
            }

            if (!_loading && args.PropertyName == nameof(TriggerRow.Context))
            {
                PopulateFilters();
                if (_filterContext != "Все")
                    ApplyFilter();
            }
        };
    }

    private void PopulateFilters()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            foreach (var part in row.Context.Split(',', StringSplitOptions.TrimEntries))
            {
                string key = part == "*" ? "Глобальные" : part;
                if (key.Length == 0) continue;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        var available = new HashSet<string>(counts.Keys, StringComparer.OrdinalIgnoreCase)
            { "Все" };
        if (!available.Contains(_filterContext))
            _filterContext = "Все";

        _filterPanel.Children.Clear();
        AddFilterChip("Все", _rows.Count);

        if (counts.Remove("Глобальные", out int globalCount))
            AddFilterChip("Глобальные", globalCount);

        foreach (var pair in counts
                     .OrderByDescending(p => p.Value)
                     .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddFilterChip(pair.Key, pair.Value);
        }
    }

    private void AddFilterChip(string label, int count)
    {
        var chip = new ToggleButton
        {
            Content = $"{label} {count}",
            IsChecked = string.Equals(label, _filterContext, StringComparison.OrdinalIgnoreCase),
            Margin = new Thickness(0, 0, 5, 4),
            Padding = new Thickness(8, 3),
            CornerRadius = new CornerRadius(4)
        };
        chip.Click += (_, _) =>
        {
            _filterContext = label;
            foreach (var item in _filterPanel.Children.OfType<ToggleButton>())
                item.IsChecked = ReferenceEquals(item, chip);
            ApplyFilter();
        };
        _filterPanel.Children.Add(chip);
    }

    private void ApplyFilter()
    {
        TriggerRow? selected = _triggerList.SelectedItem as TriggerRow;
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

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        _btnSave.IsEnabled = true;
        _triggerStatus.Text = "Не сохранено";
    }

    private void OnAdd(object? sender, RoutedEventArgs args)
    {
        var row = new TriggerRow { Trigger = "!new", Value = "текст подстановки" };
        AttachDirtyTracking(row);
        _rows.Add(row);
        MarkDirty();

        _filterContext = "Все";
        PopulateFilters();
        ApplyFilter();
        _triggerList.SelectedItem = row;
    }

    private async void OnDelete(object? sender, RoutedEventArgs args)
    {
        if (_triggerList.SelectedItem is not TriggerRow row) return;

        if (!await ConfirmDialog.Show(this, $"Удалить «{row.DisplayTrigger}»?", "Удалить"))
            return;

        _rows.Remove(row);
        MarkDirty();
        PopulateFilters();
        ApplyFilter();
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
                error = $"Строка {i + 1}: поле «Триггер» не может быть пустым.";
            else if (type is "Шорткат" or "Лидер" && hotkey.Length == 0)
                error = $"Строка {i + 1}: сочетание клавиш не записано.";
            else if (type == "Лидер" && trigger.Length == 0)
                error = $"Строка {i + 1}: для лидера укажите остаток в поле «Триггер» (например gm).";
            else if (type == "Шорткат" && !HotkeyRules.TryValidateShortcut(hotkey, out string shortcutError))
                error = $"Строка {i + 1}: {shortcutError}";
            else if (type == "Лидер" && !HotkeyRules.TryValidateLeader(hotkey, out string leaderError))
                error = $"Строка {i + 1}: {leaderError}";
            else if (type == "Шорткат" && SystemHotkeys.IsSystem(hotkey))
                error = $"Строка {i + 1}: «{hotkey}» — системное сочетание, его нельзя назначить.";
            else if (type == "Лидер" && SystemHotkeys.IsSystem($"{hotkey}+{trigger}"))
                error = $"Строка {i + 1}: «{hotkey}+{trigger}» образует системное сочетание.";

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
                "Обнаружены возможные конфликты:\n\n" + string.Join("\n", conflicts));
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

        _dirty = false;
        _btnSave.IsEnabled = false;
        _triggerStatus.Text = $"Сохранено: {entries.Count}";
        PopulateFilters();
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
                    conflicts.Add($"Строки {i + 1} и {j + 1}: триггер «{a.Trigger}»");
                }

                if (!string.IsNullOrEmpty(a.Hotkey)
                    && !string.IsNullOrEmpty(b.Hotkey)
                    && string.Equals(
                        HotkeyRules.Normalize(a.Hotkey),
                        HotkeyRules.Normalize(b.Hotkey),
                        StringComparison.OrdinalIgnoreCase)
                    && ContextsOverlap(a.Context, b.Context))
                {
                    conflicts.Add($"Строки {i + 1} и {j + 1}: шорткат «{a.Hotkey}»");
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
                    conflicts.Add($"Строки {i + 1} и {j + 1}: лидер «{a.Leader}+{a.Trigger}»");
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
            Margin = new Thickness(0, 8, 0, 0),
            Children = { add, delete }
        };

        var leftContent = new DockPanel();
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        leftContent.Children.Add(leftButtons);
        leftContent.Children.Add(_macroList);
        _macroList.SelectionChanged += OnMacroListSelected;

        var left = new Border
        {
            Width = 230,
            Padding = new Thickness(0, 0, 12, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = leftContent
        };

        _macroName.TextChanged += OnMacroNameChanged;
        _macroSteps.TextChanged += OnMacroStepsChanged;

        var syntax = new Button
        {
            Content = "Синтаксис",
            Padding = new Thickness(8, 4),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        syntax.Click += async (_, _) => await MessageDialog.Show(
            this,
            "Синтаксис макросов",
            "Одна команда на строку:\n" +
            "type <текст>\nkey <сочетание>\nsleep <мс>\nclick x,y\ndclick x,y\nrclick x,y\nrun <команда>\n\n" +
            "Строки, начинающиеся с #, игнорируются.");

        var nameRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        nameRow.Children.Add(new TextBlock
        {
            Text = "Имя",
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        Grid.SetColumn(_macroName, 1);
        nameRow.Children.Add(_macroName);

        var editorHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        editorHeader.Children.Add(new TextBlock
        {
            Text = "Шаги",
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(syntax, 1);
        editorHeader.Children.Add(syntax);

        _macroBtnSave.Click += OnMacroSave;
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 10, 0, 0)
        };
        footer.Children.Add(_macroStatus);
        Grid.SetColumn(_macroBtnSave, 1);
        footer.Children.Add(_macroBtnSave);

        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            Margin = new Thickness(16, 0, 0, 0),
            Children = { nameRow, editorHeader, _macroSteps, footer }
        };
        Grid.SetRow(editorHeader, 1);
        Grid.SetRow(_macroSteps, 2);
        Grid.SetRow(footer, 3);

        var root = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
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
            foreach (var macro in _macroDefs)
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

            _macroDirty = false;
            _macroBtnSave.IsEnabled = false;
            _macroStatus.Text = "";
        }
        finally
        {
            _macroLoading = false;
        }
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
        MarkMacroDirty();
    }

    private void OnMacroStepsChanged(object? sender, TextChangedEventArgs args)
    {
        if (_macroLoading || _macroCurrent < 0 || _macroCurrent >= _macroDefs.Count)
            return;

        _macroDefs[_macroCurrent].Steps = ParseMacroSteps(_macroSteps.Text ?? "");
        MarkMacroDirty();
    }

    private static List<string> ParseMacroSteps(string text) =>
        text.Replace("\r", "").Split('\n').ToList();

    private void RenameMacroReferences(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName)) return;

        foreach (var row in _rows)
        {
            if (row.Action == "macro"
                && string.Equals(row.Value, oldName, StringComparison.OrdinalIgnoreCase))
            {
                row.Value = newName;
            }
        }
    }

    private void OnMacroAdd(object? sender, RoutedEventArgs args)
    {
        string baseName = "новый_макрос";
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
        MarkMacroDirty();
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
        MarkMacroDirty();
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

        _macroDirty = false;
        _macroBtnSave.IsEnabled = false;
        _macroStatus.Text = $"Сохранено: {_macroDefs.Count}";
        return true;
    }

    private void MarkMacroDirty()
    {
        if (_macroLoading) return;
        _macroDirty = true;
        _macroBtnSave.IsEnabled = true;
        _macroStatus.Text = "Не сохранено";
    }

    private async void OnClosingGuard(object? sender, WindowClosingEventArgs args)
    {
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
