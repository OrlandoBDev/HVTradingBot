using System.Windows.Input;
using HVTradingBot.App.Core.Docker;
using HVTradingBot.App.Core.Health;
using HVTradingBot.App.Core.Processes;
using HVTradingBot.App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace HVTradingBot.App.ViewModels;

/// <summary>Dashboard page: the WebView URL, the worker status bar and the stack controls.</summary>
public sealed class DashboardViewModel : ObservableObject
{
    private readonly AppSession session;
    private readonly HealthMonitor monitor;
    private readonly AppSettings settings;
    private readonly Navigator navigator;
    private readonly ILogger<DashboardViewModel> logger;
    private CancellationTokenSource? monitoring;

    public DashboardViewModel(AppSession session, HealthMonitor monitor, AppSettings settings, Navigator navigator,
        ILogger<DashboardViewModel> logger)
    {
        this.session = session;
        this.monitor = monitor;
        this.settings = settings;
        this.navigator = navigator;
        this.logger = logger;
        monitor.StatusChanged += (_, status) => MainThread.BeginInvokeOnMainThread(() => Show(status));

        StopTradingCommand = new Command(async () => await StopTradingAsync(), () => !IsBusy);
        StartCommand = new Command(async () => await RunControlAsync("Starting", (s, ct) => s.StartAsync(ct), reload: true), () => !IsBusy);
        RestartWorkerCommand = new Command(async () => await RunControlAsync("Restarting the worker", (s, ct) => s.RestartWorkerAsync(ct), reload: false), () => !IsBusy);
        LogsCommand = new Command(async () => await navigator.PushLogsAsync());
        OpenInBrowserCommand = new Command(async () => await Launcher.Default.OpenAsync(DashboardUri));
        SettingsCommand = new Command(async () => await navigator.PushSettingsAsync());
    }

    /// <summary>Raised when the WebView should reload (after the stack was started again).</summary>
    public event EventHandler? ReloadRequested;

    public Uri DashboardUri => session.ApiBaseUri ?? new Uri("http://localhost:5080/");

    public string DashboardUrl => DashboardUri.ToString();

    public string StatusText { get; private set => Set(ref field, value); } = "Checking the worker…";

    public string StatusDetail { get; private set => Set(ref field, value); } = "";

    public Color StatusColor { get; private set => Set(ref field, value); } = Colors.Gray;

    public string ActionMessage { get; private set => Set(ref field, value); } = "";

    public bool IsBusy
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                foreach (var command in new[] { StopTradingCommand, StartCommand, RestartWorkerCommand })
                {
                    ((Command)command).ChangeCanExecute();
                }
            }
        }
    }

    public ICommand StopTradingCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand RestartWorkerCommand { get; }

    public ICommand LogsCommand { get; }

    public ICommand OpenInBrowserCommand { get; }

    public ICommand SettingsCommand { get; }

    /// <summary>Starts polling <c>/health/ready</c> (startup step 7). Idempotent.</summary>
    public void Activate()
    {
        OnPropertyChanged(nameof(DashboardUrl));
        if (monitoring is not null || session.ApiBaseUri is not { } api)
        {
            return;
        }

        if (session.Startup?.Worker is { } first)
        {
            Show(first);
        }

        monitoring = new CancellationTokenSource();
        _ = MonitorAsync(api, monitoring.Token);
    }

    public void Deactivate()
    {
        monitoring?.Cancel();
        monitoring?.Dispose();
        monitoring = null;
    }

    /// <summary>Quitting cannot be intercepted on Mac Catalyst, so the first dashboard visit explains what quitting does.</summary>
    public async Task ExplainQuitOnceAsync()
    {
        if (settings.QuitExplained)
        {
            return;
        }

        settings.QuitExplained = true;
        await navigator.AlertAsync("Trading continues without the app",
            "Quitting HVTradingBot leaves the Docker containers running, so the bot keeps trading. Use \"Stop trading\" " +
            "to stop it, or turn on \"Stop trading when the app quits\" in Settings.");
    }

    private async Task MonitorAsync(Uri api, CancellationToken cancellationToken)
    {
        try
        {
            await monitor.RunAsync(api, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Deactivated.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health monitoring stopped");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusText = "Status unavailable";
                StatusDetail = ex.Message;
                StatusColor = Colors.Gray;
            });
        }
    }

    private async Task StopTradingAsync()
    {
        if (!await navigator.ConfirmAsync("Stop trading?",
                "This stops the worker, the API and the database containers (docker compose stop). Open positions stay open at the broker.",
                "Stop trading"))
        {
            return;
        }

        await RunControlAsync("Stopping", (s, ct) => s.StopAsync(ct), reload: false);
    }

    private async Task RunControlAsync(string action, Func<StackController, CancellationToken, Task<ProcessResult>> control, bool reload)
    {
        if (session.Stack is not { } stack || session.ApiBaseUri is not { } api)
        {
            return;
        }

        IsBusy = true;
        ActionMessage = action + "…";
        try
        {
            var result = await control(stack, CancellationToken.None);
            ActionMessage = result.Succeeded ? "" : $"{action} failed (exit code {result.ExitCode}): {result.Output.LastOrDefault()}";
            Show(await monitor.PollOnceAsync(api, CancellationToken.None));
            if (result.Succeeded && reload)
            {
                ReloadRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Action} failed", action);
            ActionMessage = $"{action} failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Show(WorkerStatus status)
    {
        StatusText = status.Summary;
        StatusDetail = status.Detail;
        StatusColor = status.State switch
        {
            WorkerState.Running => Color.FromArgb("#22C55E"),
            WorkerState.DataStale or WorkerState.KillSwitchActive => Color.FromArgb("#F59E0B"),
            _ => Color.FromArgb("#EF4444")
        };
    }
}
