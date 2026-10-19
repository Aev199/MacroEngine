using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace MacroEngine.UI;

/// <summary>
/// Avalonia application shell. Visuals follow the design language shared with the
/// GeoBase / plx-construct / SoilStiffness tools (light, blue-grey, compact) — see UiTheme.
/// </summary>
internal sealed class App : Application
{
    private AppController? _controller;
    private SupportUiController? _supportUi;

    public override void Initialize()
    {
        UiTheme.Apply(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            _controller = new AppController(desktop);
            _supportUi = new SupportUiController(_controller.TryRestartForUpdate);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
