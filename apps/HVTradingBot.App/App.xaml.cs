using HVTradingBot.App.Pages;
using HVTradingBot.App.Services;

namespace HVTradingBot.App;

public partial class App : Application
{
    private readonly StartupPage startupPage;
    private readonly QuitHandler quitHandler;

    public App(StartupPage startupPage, QuitHandler quitHandler)
    {
        InitializeComponent();
        this.startupPage = startupPage;
        this.quitHandler = quitHandler;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Open at the preferred size, but never larger than 90 % of the screen (small laptops, scaled displays).
        var screen = DeviceDisplay.Current.MainDisplayInfo;
        var density = screen.Density > 0 ? screen.Density : 1;
        var width = Math.Min(1360, screen.Width / density * 0.9);
        var height = Math.Min(900, screen.Height / density * 0.9);

        var window = new Window(new NavigationPage(startupPage))
        {
            Title = "HVTradingBot",
            Width = width > 0 ? width : 1360,
            Height = height > 0 ? height : 900,
            // Small enough that the dashboard can switch to its compact layout (sidebar behind the menu button).
            MinimumWidth = 480,
            MinimumHeight = 480
        };
        // On Mac Catalyst closing the only window quits the app.
        window.Destroying += (_, _) => quitHandler.OnQuit();
        return window;
    }
}
