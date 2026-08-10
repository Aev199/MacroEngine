using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace MacroEngine.UI;

/// <summary>
/// Avalonia application shell. MacroEngine intentionally follows the native
/// Windows/Fluent visual language and leaves accent selection to the system theme.
/// </summary>
internal sealed class App : Application
{
    private AppController? _controller;
    private SupportUiController? _supportUi;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://MacroEngine"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });

        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            _controller = new AppController(desktop);
            _supportUi = new SupportUiController();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
