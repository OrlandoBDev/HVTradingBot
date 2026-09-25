using System.Windows.Input;
using HVTradingBot.App.Services;

namespace HVTradingBot.App.ViewModels;

/// <summary>Repository folder and "Stop trading when the app quits".</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings settings;

    public SettingsViewModel(AppSettings settings, Navigator navigator)
    {
        this.settings = settings;
        SaveCommand = new Command(async () =>
        {
            var repositoryChanged = RepositoryPath.Trim() != settings.RepositoryPath;
            settings.RepositoryPath = RepositoryPath;
            settings.StopTradingOnQuit = StopTradingOnQuit;
            if (repositoryChanged)
            {
                // A different checkout means a different stack: run the startup sequence again.
                navigator.ShowStartup();
            }
            else
            {
                await navigator.PopAsync();
            }
        });
    }

    public string RepositoryPath { get; set => Set(ref field, value); } = "";

    public bool StopTradingOnQuit { get; set => Set(ref field, value); }

    public ICommand SaveCommand { get; }

    /// <summary>Loads the stored values (the page may have been left without saving).</summary>
    public void Load()
    {
        RepositoryPath = settings.RepositoryPath;
        StopTradingOnQuit = settings.StopTradingOnQuit;
    }
}
