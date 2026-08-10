using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace MacroEngine.UI;

/// <summary>
/// Avalonia application shell. MacroEngine deliberately stays close to native
/// Fluent controls, with a restrained accent reserved for primary actions and selection.
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

        // Muted teal: enough to identify the product without making every control decorative.
        Resources["SystemAccentColor"] = Color.Parse("#52B8A2");
        Resources["SystemAccentColorDark1"] = Color.Parse("#459E8B");
        Resources["SystemAccentColorDark2"] = Color.Parse("#398373");
        Resources["SystemAccentColorDark3"] = Color.Parse("#2E685C");
        Resources["SystemAccentColorLight1"] = Color.Parse("#70C6B4");
        Resources["SystemAccentColorLight2"] = Color.Parse("#8DD3C4");
        Resources["SystemAccentColorLight3"] = Color.Parse("#AADFD4");
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
