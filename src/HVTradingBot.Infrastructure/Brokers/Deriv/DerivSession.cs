using HVTradingBot.Application.Abstractions;
using HVTradingBot.Infrastructure.Settings;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

/// <summary>
/// Owns the authenticated WebSocket for the configured Deriv account. Credentials come from the Settings page (or
/// environment); when they change, the next call reconnects with a fresh OTP. Enforces the demo-account guard twice:
/// on the account type reported by the REST API and on the WebSocket URL. Every connection attempt is reported to
/// <see cref="IDerivStatusSink"/> so the Settings page can show the result.
/// </summary>
public sealed class DerivSession(
    DerivOptions options,
    DerivRestClient rest,
    IDerivSocketFactory socketFactory,
    IDerivCredentialsProvider credentialsProvider,
    IDerivStatusSink statusSink,
    IClock clock,
    ILogger<DerivSession> logger) : IAsyncDisposable
{
    private static readonly TimeSpan SettingsCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetiredSocketGrace = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private IDerivSocket? _socket;
    private int? _connectedVersion;
    private DateTime _lastSettingsCheck = DateTime.MinValue;

    public DerivAccount? Account { get; private set; }

    public async Task<IDerivSocket> GetSocketAsync(CancellationToken cancellationToken)
    {
        if (_socket is { IsConnected: true } current && clock.UtcNow - _lastSettingsCheck < SettingsCheckInterval)
        {
            return current;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            _lastSettingsCheck = clock.UtcNow;
            var credentials = await credentialsProvider.GetAsync(cancellationToken);
            if (credentials is null)
            {
                await RetireSocketAsync();
                Account = null;
                _connectedVersion = null;
                await ReportAsync(DerivConnectionState.NotConfigured, "Add your Deriv App ID and Personal Access Token.", 0, [], cancellationToken);
                throw new DerivNotConfiguredException();
            }

            if (_socket is { IsConnected: true } connected && _connectedVersion == credentials.Version)
            {
                return connected;
            }

            if (_connectedVersion is not null && _connectedVersion != credentials.Version)
            {
                logger.LogInformation("Deriv settings changed (version {Old} -> {New}); reconnecting", _connectedVersion, credentials.Version);
            }

            await RetireSocketAsync();
            IReadOnlyList<DerivAccount> accounts = [];
            try
            {
                accounts = await rest.GetAccountsAsync(credentials, cancellationToken);
                var account = SelectAccount(accounts, options.AccountType, credentials.AccountId);
                var url = await rest.CreateWebSocketUrlAsync(credentials, account.AccountId, cancellationToken);
                EnsureUrlMatchesAccountType(url, options.AccountType);
                _socket = await socketFactory.ConnectAsync(url, TimeSpan.FromSeconds(options.RequestTimeoutSeconds), cancellationToken);
                Account = account;
                _connectedVersion = credentials.Version;
            }
            catch (Exception ex) when (ex is DerivApiException or DerivConnectionException or InvalidOperationException)
            {
                Account = null;
                _connectedVersion = null;
                await ReportAsync(DerivConnectionState.Failed, ex.Message, credentials.Version, accounts, cancellationToken);
                throw new BrokerUnavailableException($"Deriv connection failed: {ex.Message}", ex);
            }

            logger.LogInformation("Connected to Deriv {AccountType} account {AccountId} ({Currency})", Account.AccountType, Account.AccountId, Account.Currency);
            await ReportAsync(DerivConnectionState.Connected, $"Connected to {Account.AccountType} account {Account.AccountId} ({Account.Currency}).",
                credentials.Version, accounts, cancellationToken);
            return _socket;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public static DerivAccount SelectAccount(IReadOnlyList<DerivAccount> accounts, DerivAccountType accountType, string? accountId)
    {
        if (accountType != DerivAccountType.Demo)
        {
            throw new InvalidOperationException("Only Deriv demo accounts are supported in this release.");
        }

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            var chosen = accounts.FirstOrDefault(a => string.Equals(a.AccountId, accountId, StringComparison.OrdinalIgnoreCase))
                         ?? throw new InvalidOperationException($"Deriv account '{accountId}' is not available for this token.");
            return chosen.IsDemo
                ? chosen
                : throw new InvalidOperationException($"Deriv account '{chosen.AccountId}' is a {chosen.AccountType} account; only demo accounts are allowed.");
        }

        return accounts.FirstOrDefault(a => a.IsDemo)
               ?? throw new InvalidOperationException(
                   $"No Deriv demo account found for this token (accounts: {string.Join(", ", accounts.Select(a => $"{a.AccountId}:{a.AccountType}"))}).");
    }

    public static void EnsureUrlMatchesAccountType(Uri url, DerivAccountType accountType)
    {
        var expected = accountType == DerivAccountType.Demo ? "/ws/demo" : "/ws/real";
        if (!url.AbsolutePath.EndsWith(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Deriv returned a WebSocket for '{url.AbsolutePath}', expected '{expected}'. Refusing to connect.");
        }
    }

    private Task ReportAsync(DerivConnectionState state, string message, int version, IReadOnlyList<DerivAccount> accounts, CancellationToken cancellationToken) =>
        statusSink.WriteStatusAsync(new DerivConnectionStatus(state, message, version, Account?.AccountId,
            accounts.Select(a => new DerivAccountSummary(a.AccountId, a.AccountType, a.Currency)).ToList(), clock.UtcNow), cancellationToken);

    /// <summary>Stops using the current socket; requests already in flight on it get time to finish before it closes.</summary>
    private Task RetireSocketAsync()
    {
        if (_socket is null)
        {
            return Task.CompletedTask;
        }

        var retired = _socket;
        _socket = null;
        _ = Task.Delay(RetiredSocketGrace).ContinueWith(_ => retired.DisposeAsync().AsTask(), TaskScheduler.Default);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket is not null)
        {
            await _socket.DisposeAsync();
        }

        _connectLock.Dispose();
    }
}
