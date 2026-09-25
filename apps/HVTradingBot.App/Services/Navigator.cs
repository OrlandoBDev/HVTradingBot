using HVTradingBot.App.Pages;
using HVTradingBot.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HVTradingBot.App.Services;

/// <summary>Switches the window between the startup screen and the dashboard, and shows dialogs.</summary>
public sealed class Navigator(IServiceProvider services)
{
    private static Window Window => Application.Current?.Windows.FirstOrDefault()
        ?? throw new InvalidOperationException("The app has no window.");

    private static Page CurrentPage => Window.Page?.Navigation.NavigationStack.LastOrDefault() ?? Window.Page
        ?? throw new InvalidOperationException("The window has no page.");

    /// <summary>Shows the startup screen, which runs the startup sequence again.</summary>
    public void ShowStartup()
    {
        services.GetRequiredService<DashboardViewModel>().Deactivate();
        Window.Page = new NavigationPage(services.GetRequiredService<StartupPage>());
    }

    public void ShowDashboard()
    {
        Window.Page = new NavigationPage(services.GetRequiredService<DashboardPage>());
        services.GetRequiredService<DashboardViewModel>().Activate();
    }

    public Task PushSettingsAsync() => CurrentPage.Navigation.PushAsync(services.GetRequiredService<SettingsPage>());

    public Task PushLogsAsync() => CurrentPage.Navigation.PushAsync(services.GetRequiredService<LogsPage>());

    public Task PopAsync() => CurrentPage.Navigation.PopAsync();

    public Task AlertAsync(string title, string message) => CurrentPage.DisplayAlert(title, message, "OK");

    public Task<bool> ConfirmAsync(string title, string message, string accept) =>
        CurrentPage.DisplayAlert(title, message, accept, "Cancel");
}
