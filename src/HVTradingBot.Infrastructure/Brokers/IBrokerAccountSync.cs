namespace HVTradingBot.Infrastructure.Brokers;

/// <summary>
/// Copies the whole broker account (positions this app opened and ones opened elsewhere) into broker_contracts for the
/// dashboard. Read-only: nothing here buys or sells.
/// </summary>
public interface IBrokerAccountSync
{
    /// <summary>The broker (<c>IExecutionBroker.Descriptor.Name</c>) this sync reads.</summary>
    string BrokerName { get; }

    /// <summary>Refreshes the open positions. Returns true when one closed since the last refresh.</summary>
    Task<bool> SyncOpenAsync(CancellationToken cancellationToken);

    /// <summary>Adds closed positions.</summary>
    Task SyncHistoryAsync(CancellationToken cancellationToken);
}
