using HVTradingBot.Infrastructure.Brokers.Deriv;
using HVTradingBot.Infrastructure.MarketData;
using Microsoft.Extensions.Options;

namespace HVTradingBot.Infrastructure.Configuration;

public enum BrokerProvider
{
    /// <summary>Deriv multiplier contracts on a demo account (default).</summary>
    Deriv,

    /// <summary>Local simulated fills (no broker).</summary>
    Paper
}

public sealed class BrokerOptions
{
    public const string SectionName = "Broker";

    public BrokerProvider Provider { get; set; } = BrokerProvider.Deriv;
}

/// <summary>Startup checks for the live trading loop (worker only; the API never talks to a broker).</summary>
public sealed class LiveTradingOptionsValidator(IOptions<BrokerOptions> broker, IOptions<MarketDataOptions> marketData)
    : IValidateOptions<DerivOptions>
{
    public ValidateOptionsResult Validate(string? name, DerivOptions options)
    {
        var failures = new List<string>();
        var usesDeriv = broker.Value.Provider == BrokerProvider.Deriv;

        if (usesDeriv && marketData.Value.Provider != MarketDataProvider.Deriv)
        {
            failures.Add("Broker:Provider Deriv requires MarketData:Provider Deriv so that signals use the broker's own prices.");
        }

        // App ID and token are entered on the Settings page; the worker waits for them instead of failing here.

        if (options.AccountType != DerivAccountType.Demo)
        {
            failures.Add("Deriv:AccountType Real is not enabled in this release. Only demo accounts can be used.");
        }

        if (options.MinStake > options.MaxStake)
        {
            failures.Add("Deriv:MinStake must not exceed Deriv:MaxStake.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
