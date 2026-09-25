using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HVTradingBot.App.Core.Startup;

public enum StepState
{
    Pending,
    Running,
    Succeeded,
    Failed
}

public enum StartupStepId
{
    FindDocker,
    DockerRunning,
    Repository,
    Conflicts,
    StartStack,
    WaitForApi,
    WatchWorker
}

/// <summary>One line on the startup screen. Observable so the MAUI page can bind to it directly.</summary>
public sealed class StartupStep(StartupStepId id, string title) : INotifyPropertyChanged
{
    public StartupStepId Id { get; } = id;

    public string Title { get; } = title;

    public StepState State
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>Progress or result detail; the reason when <see cref="State"/> is <see cref="StepState.Failed"/>.</summary>
    public string? Message
    {
        get;
        private set => Set(ref field, value);
    }

    /// <summary>A page that helps fix a failure (e.g. the Docker Desktop download page), or null.</summary>
    public string? HelpUrl
    {
        get;
        private set => Set(ref field, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Reset() => Update(StepState.Pending, null);

    internal void Update(StepState state, string? message, string? helpUrl = null)
    {
        State = state;
        Message = message;
        HelpUrl = helpUrl;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
