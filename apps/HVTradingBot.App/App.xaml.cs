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
        var window = new Window(new NavigationPage(startupPage))
        {
            Title = "HVTradingBot",
            Width = 1360,
            Height = 900,
            MinimumWidth = 900,
            MinimumHeight = 600
        };
        // On Mac Catalyst closing the only window quits the app.
        window.Destroying += (_, _) => quitHandler.OnQuit();
        return window;
    }
}
