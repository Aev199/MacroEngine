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

/// <summary>Editable view-model for one trigger row.</summary>
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
        }
    }

    public string Trigger
    {
        get => _trigger;
        set => Set(ref _trigger, value);
    }

    public string Hotkey
    {
        get => _hotkey;
        set
        {
            if (Set(ref _hotkey, value))
                Notify(nameof(HotkeyDisplay));
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

    public string HotkeyDisplay => !HotkeyEditable
        ? "—"
        : Hotkey.Length > 0 ? "⌨  " + Hotkey : "⌨  записать…";

    public string ValuePreview
    {
        get
        {
            string oneLine = Value.Replace("\r", "").Replace("\n", " ⏎ ");
            return oneLine.Length > 60 ? oneLine[..60] + "…" : oneLine;
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
/// Settings window for triggers and named macros. Saving is task-based so the
/// close guard cannot dismiss the window before validation and persistence finish.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly List<MacroDef> _macroDefs;

    private readonly ObservableCollection<TriggerRow> _rows = new();
    private readonly ObservableCollection<string> _macroNames = new();

    private readonly DataGrid _grid;
    private readonly Button _btnSave;
    private readonly WrapPanel _filterPanel;
    private readonly TextBlock _hint;
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
        Width = 940;
        Height = 600;
        MinWidth = 720;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = AppIcon.Get();

        _filterPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        _hint = new TextBlock
        {
            Text = "Тип: Текст — набранный триггер · Шорткат — прямое сочетание · " +
                   "Лидер — удерживаемый аккорд (Ctrl/Alt/Shift) + клавиши из поля «Триггер» (например gm).\n" +
                   "Контекст: * = везде, acad = AutoCAD, !browser = не в браузере; значения разделяются запятыми.\n" +
                   "Токены: {date} {time} {datetime:HH:mm} {clipboard} {input:подпись} {choice:a|b|c} {cursor}.\n" +
                   "Действия: macro — именованный макрос; open — открыть папку/файл; " +
                   "launch — запустить приложение (путь с пробелами указывается в кавычках).",
            Opacity = 0.65,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 4)
        };
        _triggerStatus = BuildStatusText();
        _grid = BuildGrid();
        _btnSave = new Button
        {
            Content = "Сохранить",
            IsEnabled = false,
            Classes = { "accent" }
        };

        _macroList = new ListBox { ItemsSource = _macroNames };
        _macroName = new TextBox();
        _macroSteps = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace")
        };
        _macroBtnSave = new Button
        {
            Content = "Сохранить макросы",
            IsEnabled = false,
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
        Content = new Border { Padding = new Thickness(12), Child = tabs };

        PopulateGrid(config.Load());
        PopulateFilters();
        PopulateMacroList();

        Closing += OnClosingGuard;
    }

    private static TextBlock BuildStatusText() => new()
    {
        Opacity = 0.65,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right
    };

    private DataGrid BuildGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserReorderColumns = false,
            CanUserSortColumns = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            RowHeight = 36
        };

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Тип",
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((_, _) =>
            {
                var combo = new ComboBox
                {
                    ItemsSource = TriggerRow.Types,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0)
                };
                combo.Bind(ComboBox.SelectedItemProperty,
                    new Binding(nameof(TriggerRow.Type)) { Mode = BindingMode.TwoWay });
                return combo;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Триггер",
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((_, _) =>
            {
                var text = new TextBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                    Watermark = "!new"
                };
                text.Bind(TextBox.TextProperty,
                    new Binding(nameof(TriggerRow.Trigger)) { Mode = BindingMode.TwoWay });
                text.Bind(InputElement.IsEnabledProperty,
                    new Binding(nameof(TriggerRow.TriggerEditable)));
                return text;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Шорткат / Лидер",
            Width = new DataGridLength(1.6, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((_, _) =>
            {
                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                    Background = Brushes.Transparent
                };
                button.Bind(ContentControl.ContentProperty,
                    new Binding(nameof(TriggerRow.HotkeyDisplay)));
                button.Bind(InputElement.IsEnabledProperty,
                    new Binding(nameof(TriggerRow.HotkeyEditable)));
                button.Click += async (_, _) =>
                {
                    if (button.DataContext is not TriggerRow row) return;
                    var recorder = new HotkeyRecorderWindow(row.Type == "Лидер");
                    string? combo = await recorder.ShowDialog<string?>(this);
                    if (!string.IsNullOrEmpty(combo))
                        row.Hotkey = combo;
                };
                return button;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Значение",
            Width = new DataGridLength(3.0, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((_, _) =>
            {
                var button = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                    Background = Brushes.Transparent
                };
                button.Bind(ContentControl.ContentProperty,
                    new Binding(nameof(TriggerRow.ValuePreview)));
                button.Click += async (_, _) =>
                {
                    if (button.DataContext is not TriggerRow row) return;

                    IReadOnlyList<string>? macroNames = row.Action == "macro"
                        ? _macroDefs.Select(m => m.Name.Trim())
                            .Where(n => n.Length > 0)
                            .ToList()
                        : null;

                    string rowName = row.Trigger.Length > 0 ? row.Trigger : row.Hotkey;
                    var editor = new ValueEditorWindow(row.Value, rowName, macroNames);
                    string? result = await editor.ShowDialog<string?>(this);
                    if (result != null)
                        row.Value = result;
                };
                return button;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Контекст",
            Width = new DataGridLength(1.3, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((_, _) =>
            {
                var text = new TextBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0)
                };
                text.Bind(TextBox.TextProperty,
                    new Binding(nameof(TriggerRow.Context)) { Mode = BindingMode.TwoWay });
                return text;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Действие",
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((_, _) =>
            {
                var combo = new ComboBox
                {
                    ItemsSource = TriggerRow.Actions,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0)
                };
                combo.Bind(ComboBox.SelectedItemProperty,
                    new Binding(nameof(TriggerRow.Action)) { Mode = BindingMode.TwoWay });
                return combo;
            })
        });

        return grid;
    }

    private Control BuildTriggersTab()
    {
        var hintToggle = new Button
        {
            Content = "Справка по синтаксису ▸",
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 2),
            Margin = new Thickness(0, 6, 0, 0)
        };
        hintToggle.Click += (_, _) =>
        {
            _hint.IsVisible = !_hint.IsVisible;
            hintToggle.Content = _hint.IsVisible
                ? "Справка по синтаксису ▾"
                : "Справка по синтаксису ▸";
        };

        var add = new Button { Content = "＋ Добавить" };
        var delete = new Button { Content = "Удалить" };
        add.Click += OnAdd;
        delete.Click += OnDelete;
        _btnSave.Click += OnSave;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { add, delete, _btnSave }
        };

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Left);
        footer.Children.Add(buttons);
        footer.Children.Add(_triggerStatus);

        var root = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_filterPanel, Dock.Top);
        DockPanel.SetDock(hintToggle, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(_filterPanel);
        root.Children.Add(hintToggle);
        root.Children.Add(_hint);
        root.Children.Add(footer);
        root.Children.Add(_grid);
        return root;
    }

    private void PopulateGrid(IEnumerable<TriggerEntry> triggers)
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
            Content = $"{label} ({count})",
            IsChecked = string.Equals(label, _filterContext, StringComparison.OrdinalIgnoreCase),
            Margin = new Thickness(0, 0, 6, 4),
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(12)
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
        if (_filterContext == "Все")
        {
            _grid.ItemsSource = _rows;
            return;
        }

        _grid.ItemsSource = _rows.Where(row =>
        {
            var parts = row.Context.Split(',', StringSplitOptions.TrimEntries);
            return _filterContext == "Глобальные"
                ? parts.Any(p => p == "*")
                : parts.Any(p => string.Equals(
                    p, _filterContext, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        _btnSave.IsEnabled = true;
        _triggerStatus.Text = "Есть несохранённые изменения";
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
        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row, null);
    }

    private async void OnDelete(object? sender, RoutedEventArgs args)
    {
        if (_grid.SelectedItem is not TriggerRow row) return;

        string name = row.Trigger.Length > 0 ? row.Trigger : row.Hotkey;
        if (!await ConfirmDialog.Show(this, $"Удалить триггер «{name}»?"))
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
        _triggerStatus.Text = $"Сохранено триггеров: {entries.Count}";
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
                    && string.Equals(a.Hotkey, b.Hotkey, StringComparison.OrdinalIgnoreCase)
                    && ContextsOverlap(a.Context, b.Context))
                {
                    conflicts.Add($"Строки {i + 1} и {j + 1}: шорткат «{a.Hotkey}»");
                }

                if (!string.IsNullOrEmpty(a.Leader)
                    && !string.IsNullOrEmpty(b.Leader)
                    && string.Equals(a.Leader, b.Leader, StringComparison.OrdinalIgnoreCase)
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
        var add = new Button { Content = "＋" };
        var delete = new Button { Content = "－" };
        add.Click += OnMacroAdd;
        delete.Click += OnMacroDelete;

        var leftButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { add, delete }
        };

        var left = new DockPanel { Width = 220 };
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        left.Children.Add(_macroList);
        _macroList.SelectionChanged += OnMacroListSelected;

        var nameLabel = new TextBlock
        {
            Text = "Имя:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(nameLabel, Dock.Left);
        nameRow.Children.Add(nameLabel);
        nameRow.Children.Add(_macroName);

        _macroName.TextChanged += OnMacroNameChanged;
        _macroSteps.TextChanged += OnMacroStepsChanged;

        var help = new TextBlock
        {
            Text = "Шаги (по одному на строку): type <текст> · key <сочетание> · " +
                   "sleep <мс> · click/dclick/rclick x,y · run <команда>. " +
                   "Строки, начинающиеся с #, игнорируются.",
            Opacity = 0.6,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };

        _macroBtnSave.Click += OnMacroSave;
        var saveButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _macroBtnSave }
        };
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(saveButtons, Dock.Left);
        footer.Children.Add(saveButtons);
        footer.Children.Add(_macroStatus);

        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(nameRow, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        DockPanel.SetDock(help, Dock.Bottom);
        right.Children.Add(nameRow);
        right.Children.Add(footer);
        right.Children.Add(help);
        right.Children.Add(_macroSteps);

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
        if (!await ConfirmDialog.Show(this, $"Удалить макрос «{name}»?"))
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
        _macroStatus.Text = $"Сохранено макросов: {_macroDefs.Count}";
        return true;
    }

    private void MarkMacroDirty()
    {
        if (_macroLoading) return;
        _macroDirty = true;
        _macroBtnSave.IsEnabled = true;
        _macroStatus.Text = "Есть несохранённые изменения";
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
            bool save = await ConfirmDialog.Show(this,
                "Есть несохранённые изменения. Сохранить перед закрытием?",
                yes: "Сохранить",
                no: "Не сохранять");

            if (save)
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
