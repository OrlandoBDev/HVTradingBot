using System.Windows.Input;
using HVTradingBot.App.Core.Processes;
using HVTradingBot.App.Core.Startup;
using HVTradingBot.App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;

namespace HVTradingBot.App.ViewModels;

/// <summary>Startup screen: runs <see cref="StartupPipeline"/> and shows each step plus the compose output.</summary>
public sealed class StartupViewModel : ObservableObject
{
    private readonly StartupPipeline pipeline;
    private readonly AppSettings settings;
    private readonly AppSession session;
    private readonly Navigator navigator;
    private readonly ILogger<StartupViewModel> logger;
    private readonly OutputLog output = new();
    private IDispatcherTimer? logTimer;
    private long shownLogVersion = -1;
    private CancellationTokenSource? run;

    public StartupViewModel(StartupPipeline pipeline, AppSettings settings, AppSession session, Navigator navigator,
        ILogger<StartupViewModel> logger)
    {
        this.pipeline = pipeline;
        this.settings = settings;
        this.session = session;
        this.navigator = navigator;
        this.logger = logger;
        pipeline.OutputReceived += (_, line) => output.Append(line);

        RetryCommand = new Command(() => _ = StartAsync(), () => !IsRunning);
        ToggleLogCommand = new Command(() => IsLogExpanded = !IsLogExpanded);
        OpenSettingsCommand = new Command(async () => await navigator.PushSettingsAsync());
        OpenHelpCommand = new Command(async () =>
        {
            if (HelpUrl is not null)
            {
                await Launcher.Default.OpenAsync(new Uri(HelpUrl));
            }
        });
    }

    public IReadOnlyList<StartupStep> Steps => pipeline.Steps;

    public string RepositoryPath => settings.RepositoryPath;

    public bool IsRunning
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                ((Command)RetryCommand).ChangeCanExecute();
            }
        }
    }

    public string? FailureMessage
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(HasFailed));
            }
        }
    }

    public bool HasFailed => FailureMessage is not null;

    public string? HelpUrl
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(HasHelp));
            }
        }
    }

    public bool HasHelp => HelpUrl is not null;

    public bool IsLogExpanded
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(LogToggleText));
            }
        }
    }

    public string LogToggleText => IsLogExpanded ? "Hide log" : "Show log";

    public string LogText { get; private set => Set(ref field, value); } = "";

    public ICommand RetryCommand { get; }

    public ICommand ToggleLogCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenHelpCommand { get; }

    /// <summary>Runs the startup sequence; on success switches to the dashboard.</summary>
    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        FailureMessage = null;
        HelpUrl = null;
        OnPropertyChanged(nameof(RepositoryPath));
        output.Clear();
        LogTimer().Start();
        run?.Dispose();
        run = new CancellationTokenSource();
        try
        {
            var result = await pipeline.RunAsync(new StartupOptions(settings.RepositoryPath), run.Token);
            RefreshLog();
            if (result.Succeeded)
            {
                session.SetStarted(result);
                navigator.ShowDashboard();
                return;
            }

            FailureMessage = result.FailedStep?.Message ?? "Startup failed.";
            HelpUrl = result.FailedStep?.HelpUrl;
            IsLogExpanded = IsLogExpanded || result.FailedStep?.Id == StartupStepId.StartStack;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Startup cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Startup failed unexpectedly");
            FailureMessage = ex.Message;
        }
        finally
        {
            logTimer?.Stop();
            IsRunning = false;
        }
    }

    /// <summary>Cancels a running startup (the page was left, e.g. for Settings).</summary>
    public void Cancel() => run?.Cancel();

    /// <summary>
    /// Output can be thousands of lines during an image build; the label is refreshed a few times a second instead of
    /// once per line. Created on first use, on the UI thread.
    /// </summary>
    private IDispatcherTimer LogTimer()
    {
        if (logTimer is null)
        {
            logTimer = (Dispatcher.GetForCurrentThread() ?? Application.Current!.Dispatcher).CreateTimer();
            logTimer.Interval = TimeSpan.FromMilliseconds(300);
            logTimer.Tick += (_, _) => RefreshLog();
        }

        return logTimer;
    }

    private void RefreshLog()
    {
        if (output.Version == shownLogVersion)
        {
            return;
        }

        shownLogVersion = output.Version;
        LogText = output.Text;
    }
}
