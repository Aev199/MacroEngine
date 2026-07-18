using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace MacroEngine.UI;

/// <summary>
/// Avalonia application: dark Fluent theme with a teal accent
/// (matches the tray icon). The app has no main window — it lives
/// in the tray via <see cref="AppController"/>.
/// </summary>
internal sealed class App : Application
{
    private AppController? _controller;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://MacroEngine"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });

        RequestedThemeVariant = ThemeVariant.Dark;

        // Teal accent instead of the OS accent color.
        Resources["SystemAccentColor"] = Color.Parse("#00D2A0");
        Resources["SystemAccentColorDark1"] = Color.Parse("#00B78C");
        Resources["SystemAccentColorDark2"] = Color.Parse("#009C78");
        Resources["SystemAccentColorDark3"] = Color.Parse("#008164");
        Resources["SystemAccentColorLight1"] = Color.Parse("#2BDCAF");
        Resources["SystemAccentColorLight2"] = Color.Parse("#56E5BE");
        Resources["SystemAccentColorLight3"] = Color.Parse("#81EECD");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            _controller = new AppController(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
