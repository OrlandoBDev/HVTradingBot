using System.Windows.Input;
using HVTradingBot.App.Core.Docker;
using HVTradingBot.App.Services;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.App.ViewModels;

/// <summary>The last 200 lines of <c>docker compose logs</c> for the worker or the API.</summary>
public sealed class LogsViewModel : ObservableObject
{
    private readonly AppSession session;
    private readonly ILogger<LogsViewModel> logger;

    public LogsViewModel(AppSession session, ILogger<LogsViewModel> logger)
    {
        this.session = session;
        this.logger = logger;
        RefreshCommand = new Command(async () => await RefreshAsync(), () => !IsLoading);
    }

    public string[] Services { get; } = [ComposeCommands.WorkerService, ComposeCommands.ApiService];

    public string SelectedService
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                _ = RefreshAsync();
            }
        }
    } = ComposeCommands.WorkerService;

    public string LogText { get; private set => Set(ref field, value); } = "";

    public bool IsLoading
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                ((Command)RefreshCommand).ChangeCanExecute();
            }
        }
    }

    public ICommand RefreshCommand { get; }

    public async Task RefreshAsync()
    {
        if (session.Stack is not { } stack || IsLoading)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var result = await stack.LogsAsync(SelectedService, CancellationToken.None);
            LogText = result.Succeeded
                ? string.Join('\n', result.Output)
                : $"docker compose logs failed (exit code {result.ExitCode}):\n{string.Join('\n', result.Output)}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading the {Service} logs failed", SelectedService);
            LogText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
