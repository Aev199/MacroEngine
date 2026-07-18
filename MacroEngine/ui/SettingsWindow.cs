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

/// <summary>One row of the trigger grid (editable view-model over TriggerEntry).</summary>
internal sealed class TriggerRow : INotifyPropertyChanged
{
    public static readonly string[] Types = { "Текст", "Шорткат", "Лидер" };
    public static readonly string[] Actions = { "text", "richtext", "script", "lisp", "macro", "open", "launch" };

    private string _type = "Текст";
    private string _trigger = "";
    private string _hotkey = "";
    private string _value = "";
    private string _context = "*";
    private string _action = "text";

    public string Type    { get => _type;    set { if (Set(ref _type, value)) { Notify(nameof(TriggerEditable)); Notify(nameof(HotkeyEditable)); Notify(nameof(HotkeyDisplay)); } } }
    public string Trigger { get => _trigger; set => Set(ref _trigger, value); }
    public string Hotkey  { get => _hotkey;  set { if (Set(ref _hotkey, value)) Notify(nameof(HotkeyDisplay)); } }
    public string Value   { get => _value;   set { if (Set(ref _value, value)) Notify(nameof(ValuePreview)); } }
    public string Context { get => _context; set => Set(ref _context, value); }
    public string Action  { get => _action;  set => Set(ref _action, value); }

    public bool TriggerEditable => Type != "Шорткат";
    public bool HotkeyEditable  => Type != "Текст";
    public string HotkeyDisplay => !HotkeyEditable ? "—"
        : Hotkey.Length > 0 ? "⌨  " + Hotkey : "⌨  записать…";
    public string ValuePreview
    {
        get
        {
            string oneLine = Value.Replace("\n", " ⏎ ");
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

    public static TriggerRow From(TriggerEntry t) => new()
    {
        Type = !string.IsNullOrWhiteSpace(t.Hotkey) ? "Шорткат"
             : !string.IsNullOrWhiteSpace(t.Leader) ? "Лидер" : "Текст",
        Trigger = t.Trigger,
        Hotkey = t.Hotkey ?? t.Leader ?? "",
        Value = t.Value,
        Context = t.Context,
        Action = t.Action
    };

    public TriggerEntry ToEntry() => new()
    {
        Trigger = Type == "Шорткат" ? "" : Trigger.Trim(),
        Hotkey  = Type == "Шорткат" ? Hotkey.Trim() : null,
        Leader  = Type == "Лидер"   ? Hotkey.Trim() : null,
        Value   = Value,
        Context = Context.Trim().Length > 0 ? Context.Trim() : "*",
        Action  = Action.Trim()
    };
}

/// <summary>
/// Settings window with two tabs:
///   «Триггеры» — trigger grid (type / trigger / hotkey / value / context / action)
///                with context filter chips and conflict detection on save;
///   «Макросы»  — named reusable macros (list + step editor).
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly TriggerConfig _config;
    private readonly MacroLibrary _macros;
    private readonly List<MacroDef> _macroDefs;

    private readonly ObservableCollection<TriggerRow> _rows = new();
    private readonly DataGrid _grid;
    private readonly Button _btnSave;
    private readonly WrapPanel _filterPanel;
    private readonly TextBlock _hint;

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

        // ── Triggers tab controls ─────────────────────────────────
        _filterPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        _hint = new TextBlock
        {
            Text = "Тип: Текст — набранный триггер · Шорткат — прямое сочетание · " +
                   "Лидер — удерживаемый аккорд (Ctrl/Alt) + клавиши «остатка» из поля «Триггер» (напр. gm).\n" +
                   "Контекст: * = везде, acad = AutoCAD, !browser = не в браузере (через запятую).\n" +
                   "Токены: {date} {time} {datetime:HH:mm} {clipboard} {input:подпись} {choice:a|b|c} {cursor}.\n" +
                   "Действия: macro — макрос из вкладки «Макросы»; open — открыть папку/файл; " +
                   "launch — запустить приложение (путь с пробелами — в кавычках).",
            Opacity = 0.65,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 4)
        };
        _grid = BuildGrid();
        _btnSave = new Button { Content = "Сохранить", IsEnabled = false, Classes = { "accent" } };

        _macroList = new ListBox();
        _macroName = new TextBox();
        _macroSteps = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace")
        };
        _macroBtnSave = new Button { Content = "Сохранить макросы", IsEnabled = false, Classes = { "accent" } };

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Триггеры", Content = BuildTriggersTab() },
                new TabItem { Header = "Макросы",  Content = BuildMacrosTab() }
            }
        };
        Content = new Border { Padding = new Thickness(12), Child = tabs };

        PopulateGrid(config.Load());
        PopulateFilters();
        PopulateMacroList();

        Closing += OnClosingGuard;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Triggers tab
    // ═══════════════════════════════════════════════════════════════

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
            CellTemplate = new FuncDataTemplate<TriggerRow>((row, _) =>
            {
                var combo = new ComboBox
                {
                    ItemsSource = TriggerRow.Types,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0)
                };
                combo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(TriggerRow.Type)) { Mode = BindingMode.TwoWay });
                return combo;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Триггер",
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((row, _) =>
            {
                var tb = new TextBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                    Watermark = "!new"
                };
                tb.Bind(TextBox.TextProperty, new Binding(nameof(TriggerRow.Trigger)) { Mode = BindingMode.TwoWay });
                tb.Bind(InputElement.IsEnabledProperty, new Binding(nameof(TriggerRow.TriggerEditable)));
                return tb;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Шорткат / Лидер",
            Width = new DataGridLength(1.6, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((row, _) =>
            {
                var btn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                    Background = Brushes.Transparent
                };
                btn.Bind(ContentControl.ContentProperty, new Binding(nameof(TriggerRow.HotkeyDisplay)));
                btn.Bind(InputElement.IsEnabledProperty, new Binding(nameof(TriggerRow.HotkeyEditable)));
                btn.Click += async (_, _) =>
                {
                    if (btn.DataContext is not TriggerRow r) return;
                    var recorder = new HotkeyRecorderWindow(leaderMode: r.Type == "Лидер");
                    string? combo = await recorder.ShowDialog<string?>(this);
                    if (!string.IsNullOrEmpty(combo))
                        r.Hotkey = combo;
                };
                return btn;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Значение",
            Width = new DataGridLength(3.0, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((row, _) =>
            {
                var btn = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                    Background = Brushes.Transparent
                };
                btn.Bind(ContentControl.ContentProperty, new Binding(nameof(TriggerRow.ValuePreview)));
                btn.Click += async (_, _) =>
                {
                    if (btn.DataContext is not TriggerRow r) return;
                    var macroNames = r.Action == "macro"
                        ? _macroDefs.Select(m => m.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                        : null;
                    var editor = new ValueEditorWindow(r.Value, r.Trigger.Length > 0 ? r.Trigger : r.Hotkey, macroNames);
                    string? result = await editor.ShowDialog<string?>(this);
                    if (result != null)
                        r.Value = result;
                };
                return btn;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Контекст",
            Width = new DataGridLength(1.3, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((row, _) =>
            {
                var tb = new TextBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0)
                };
                tb.Bind(TextBox.TextProperty, new Binding(nameof(TriggerRow.Context)) { Mode = BindingMode.TwoWay });
                return tb;
            })
        });

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Действие",
            Width = new DataGridLength(1.2, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TriggerRow>((row, _) =>
            {
                var combo = new ComboBox
                {
                    ItemsSource = TriggerRow.Actions,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0)
                };
                combo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(TriggerRow.Action)) { Mode = BindingMode.TwoWay });
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
            hintToggle.Content = _hint.IsVisible ? "Справка по синтаксису ▾" : "Справка по синтаксису ▸";
        };

        var btnAdd = new Button { Content = "＋ Добавить" };
        var btnDelete = new Button { Content = "Удалить" };
        btnAdd.Click += OnAdd;
        btnDelete.Click += OnDelete;
        _btnSave.Click += OnSave;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { btnAdd, btnDelete, _btnSave }
        };

        var root = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(_filterPanel, Dock.Top);
        DockPanel.SetDock(hintToggle, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(_filterPanel);
        root.Children.Add(hintToggle);
        root.Children.Add(_hint);
        root.Children.Add(buttons);
        root.Children.Add(_grid);
        return root;
    }

    private void PopulateGrid(List<TriggerEntry> triggers)
    {
        _loading = true;
        try
        {
            _rows.Clear();
            foreach (var t in triggers)
            {
                var row = TriggerRow.From(t);
                AttachDirtyTracking(row);
                _rows.Add(row);
            }
            _dirty = false;
            _btnSave.IsEnabled = false;
        }
        finally { _loading = false; }
        ApplyFilter();
    }

    /// <summary>
    /// Row property setters only raise PropertyChanged on a real value change,
    /// so this fires on user edits — not when cell bindings initialize.
    /// </summary>
    private void AttachDirtyTracking(TriggerRow row)
    {
        row.PropertyChanged += (_, ev) =>
        {
            if (ev.PropertyName is nameof(TriggerRow.Type) or nameof(TriggerRow.Trigger)
                or nameof(TriggerRow.Hotkey) or nameof(TriggerRow.Value)
                or nameof(TriggerRow.Context) or nameof(TriggerRow.Action))
                MarkDirty();
        };
    }

    // ── Context filter chips ────────────────────────────────────────

    private void PopulateFilters()
    {
        _filterPanel.Children.Clear();

        var counts = new Dictionary<string, int>();
        foreach (var r in _rows)
        {
            foreach (var part in r.Context.Split(',', StringSplitOptions.TrimEntries))
            {
                string key = part == "*" ? "Глобальные" : part;
                if (key.Length == 0) continue;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        AddFilterChip("Все", _rows.Count);
        if (counts.TryGetValue("Глобальные", out int g)) { AddFilterChip("Глобальные", g); counts.Remove("Глобальные"); }
        foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            AddFilterChip(kv.Key, kv.Value);
    }

    private void AddFilterChip(string label, int count)
    {
        var chip = new ToggleButton
        {
            Content = $"{label} ({count})",
            IsChecked = label == _filterContext,
            Margin = new Thickness(0, 0, 6, 4),
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(12)
        };
        chip.Click += (_, _) =>
        {
            _filterContext = label;
            foreach (var c in _filterPanel.Children.OfType<ToggleButton>())
                c.IsChecked = ReferenceEquals(c, chip);
            ApplyFilter();
        };
        _filterPanel.Children.Add(chip);
    }

    private void ApplyFilter()
    {
        // DataGrid has no per-row visibility — swap the ItemsSource instead.
        if (_filterContext == "Все")
        {
            _grid.ItemsSource = _rows;
            return;
        }

        _grid.ItemsSource = _rows.Where(r =>
        {
            var parts = r.Context.Split(',', StringSplitOptions.TrimEntries);
            return _filterContext == "Глобальные"
                ? parts.Contains("*")
                : parts.Any(c => c == _filterContext);
        }).ToList();
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        _btnSave.IsEnabled = true;
    }

    // ── Add / delete / save ─────────────────────────────────────────

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var row = new TriggerRow { Trigger = "!new", Value = "текст подстановки" };
        AttachDirtyTracking(row);
        _rows.Add(row);
        MarkDirty();

        // Reset the filter so the new row is visible.
        _filterContext = "Все";
        foreach (var c in _filterPanel.Children.OfType<ToggleButton>())
            c.IsChecked = (c.Content as string)?.StartsWith("Все ") == true;
        ApplyFilter();
        _grid.SelectedItem = row;
        _grid.ScrollIntoView(row, null);
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_grid.SelectedItem is not TriggerRow row) return;

        string name = row.Trigger.Length > 0 ? row.Trigger : row.Hotkey;
        bool ok = await ConfirmDialog.Show(this, $"Удалить триггер «{name}»?");
        if (!ok) return;

        _rows.Remove(row);
        MarkDirty();
        ApplyFilter();
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var list = new List<TriggerEntry>();

        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            string type = r.Type;
            string trigger = r.Trigger.Trim();
            string hotkey = r.Hotkey.Trim();

            string? error = null;
            if (type == "Текст" && trigger.Length == 0)
                error = $"Строка {i + 1}: поле «Триггер» не может быть пустым.";
            else if (type is "Шорткат" or "Лидер" && hotkey.Length == 0)
                error = $"Строка {i + 1}: сочетание клавиш не записано. Нажмите на ячейку «Шорткат / Лидер».";
            else if (type == "Лидер" && trigger.Length == 0)
                error = $"Строка {i + 1}: для лидера в поле «Триггер» укажите «остаток» — клавиши, которые набираются при зажатом аккорде (напр. gm).";
            else if (type == "Шорткат" && SystemHotkeys.IsSystem(hotkey))
                error = $"Строка {i + 1}: «{hotkey}» — системное сочетание, его нельзя назначить.";
            else if (type == "Лидер" && SystemHotkeys.IsSystem($"{hotkey}+{trigger}"))
                error = $"Строка {i + 1}: аккорд «{hotkey}» + «{trigger}» образует системное сочетание.";

            if (error != null)
            {
                await MessageDialog.Show(this, "Ошибка", error);
                return;
            }

            list.Add(r.ToEntry());
        }

        // ── Conflict detection ────────────────────────────────────
        var conflicts = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i]; var b = list[j];
                if (a.Trigger.Length > 0 && a.Trigger == b.Trigger
                    && string.IsNullOrEmpty(a.Hotkey) && string.IsNullOrEmpty(b.Hotkey)
                    && string.IsNullOrEmpty(a.Leader) && string.IsNullOrEmpty(b.Leader)
                    && ContextsOverlap(a.Context, b.Context))
                    conflicts.Add($"Строки {i + 1} и {j + 1}: триггер «{a.Trigger}»");

                if (!string.IsNullOrEmpty(a.Hotkey) && !string.IsNullOrEmpty(b.Hotkey)
                    && string.Equals(a.Hotkey, b.Hotkey, StringComparison.OrdinalIgnoreCase)
                    && ContextsOverlap(a.Context, b.Context))
                    conflicts.Add($"Строки {i + 1} и {j + 1}: шорткат «{a.Hotkey}»");

                if (!string.IsNullOrEmpty(a.Leader) && !string.IsNullOrEmpty(b.Leader)
                    && string.Equals(a.Leader, b.Leader, StringComparison.OrdinalIgnoreCase)
                    && a.Trigger == b.Trigger
                    && ContextsOverlap(a.Context, b.Context))
                    conflicts.Add($"Строки {i + 1} и {j + 1}: лидер «{a.Leader}+{a.Trigger}»");
            }
        }
        if (conflicts.Count > 0)
        {
            await MessageDialog.Show(this, "Предупреждение",
                "Обнаружены возможные конфликты:\n\n" + string.Join("\n", conflicts));
        }

        _config.Save(list);
        _dirty = false;
        _btnSave.IsEnabled = false;

        PopulateFilters();
    }

    private static bool ContextsOverlap(string a, string b)
    {
        var pa = a.Split(',', StringSplitOptions.TrimEntries).Where(s => !s.StartsWith('!')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pb = b.Split(',', StringSplitOptions.TrimEntries).Where(s => !s.StartsWith('!')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pa.Contains("*") || pb.Contains("*")) return true;
        return pa.Overlaps(pb);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Macros tab
    // ═══════════════════════════════════════════════════════════════

    private Control BuildMacrosTab()
    {
        var btnMacroAdd = new Button { Content = "＋" };
        var btnMacroDel = new Button { Content = "－" };
        btnMacroAdd.Click += OnMacroAdd;
        btnMacroDel.Click += OnMacroDelete;

        var leftButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { btnMacroAdd, btnMacroDel }
        };

        var left = new DockPanel { Width = 220 };
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        left.Children.Add(_macroList);
        _macroList.SelectionChanged += OnMacroListSelected;

        var nameRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var nameLbl = new TextBlock
        {
            Text = "Имя:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(nameLbl, Dock.Left);
        nameRow.Children.Add(nameLbl);
        nameRow.Children.Add(_macroName);
        _macroName.TextChanged += (_, _) => { if (!_macroLoading) MarkMacroDirty(); };
        _macroSteps.TextChanged += (_, _) => { if (!_macroLoading) MarkMacroDirty(); };

        var help = new TextBlock
        {
            Text = "Шаги (по одному на строку): type <текст> · key <сочетание> (Ctrl+S, Enter, F5) · " +
                   "sleep <мс> · click/dclick/rclick x,y · run <команда>. Строки с # игнорируются.",
            Opacity = 0.6,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var saveRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { _macroBtnSave }
        };
        _macroBtnSave.Click += OnMacroSave;

        var right = new DockPanel { Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(nameRow, Dock.Top);
        DockPanel.SetDock(saveRow, Dock.Bottom);
        DockPanel.SetDock(help, Dock.Bottom);
        right.Children.Add(nameRow);
        right.Children.Add(saveRow);
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
            _macroList.ItemsSource = null;
            _macroList.ItemsSource = _macroDefs.Select(m => m.Name).ToList();

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
        }
        finally { _macroLoading = false; }
    }

    private void OnMacroListSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_macroLoading) return;
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
        finally { _macroLoading = prev; }
    }

    private void FlushEditorToCurrent()
    {
        if (_macroCurrent < 0 || _macroCurrent >= _macroDefs.Count) return;

        var def = _macroDefs[_macroCurrent];
        def.Name = (_macroName.Text ?? "").Trim();
        def.Steps = (_macroSteps.Text ?? "")
            .Replace("\r", "")
            .Split('\n')
            .ToList();

        // Keep the list label in sync without re-triggering selection logic.
        _macroLoading = true;
        try
        {
            int sel = _macroList.SelectedIndex;
            _macroList.ItemsSource = _macroDefs.Select(m => m.Name).ToList();
            _macroList.SelectedIndex = sel;
        }
        finally { _macroLoading = false; }
    }

    private void OnMacroAdd(object? sender, RoutedEventArgs e)
    {
        FlushEditorToCurrent();

        var def = new MacroDef { Name = "новый_макрос", Steps = new List<string> { "key Ctrl+S" } };
        _macroDefs.Add(def);

        _macroLoading = true;
        try { _macroList.ItemsSource = _macroDefs.Select(m => m.Name).ToList(); }
        finally { _macroLoading = false; }

        _macroList.SelectedIndex = _macroDefs.Count - 1; // fires selection → loads editor
        MarkMacroDirty();
        _macroName.Focus();
        _macroName.SelectAll();
    }

    private async void OnMacroDelete(object? sender, RoutedEventArgs e)
    {
        int idx = _macroList.SelectedIndex;
        if (idx < 0 || idx >= _macroDefs.Count) return;

        bool ok = await ConfirmDialog.Show(this, $"Удалить макрос «{_macroDefs[idx].Name}»?");
        if (!ok) return;

        _macroDefs.RemoveAt(idx);

        _macroLoading = true;
        try { _macroList.ItemsSource = _macroDefs.Select(m => m.Name).ToList(); }
        finally { _macroLoading = false; }

        _macroCurrent = -1;
        if (_macroDefs.Count > 0)
            _macroList.SelectedIndex = Math.Min(idx, _macroDefs.Count - 1);
        else
            LoadMacroIntoEditor(-1);

        MarkMacroDirty();
    }

    private async void OnMacroSave(object? sender, RoutedEventArgs e)
    {
        FlushEditorToCurrent();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _macroDefs)
        {
            string? error = null;
            if (string.IsNullOrWhiteSpace(m.Name))
                error = "У каждого макроса должно быть имя.";
            else if (!seen.Add(m.Name))
                error = $"Имя макроса «{m.Name}» повторяется. Имена должны быть уникальными.";

            if (error != null)
            {
                await MessageDialog.Show(this, "Ошибка", error);
                return;
            }
        }

        _macros.Save(_macroDefs);
        _macroDirty = false;
        _macroBtnSave.IsEnabled = false;
    }

    private void MarkMacroDirty()
    {
        _macroDirty = true;
        _macroBtnSave.IsEnabled = true;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Close guard
    // ═══════════════════════════════════════════════════════════════

    private bool _closeConfirmed;

    private async void OnClosingGuard(object? sender, WindowClosingEventArgs e)
    {
        FlushEditorToCurrent();
        if (_closeConfirmed || (!_dirty && !_macroDirty)) return;

        e.Cancel = true;
        var result = await ConfirmDialog.Show(this,
            "Есть несохранённые изменения. Сохранить перед закрытием?",
            yes: "Сохранить", no: "Не сохранять");

        if (result)
        {
            if (_macroDirty) OnMacroSave(this, new RoutedEventArgs());
            if (_dirty) OnSave(this, new RoutedEventArgs());
        }
        _closeConfirmed = true;
        Close();
    }
}
