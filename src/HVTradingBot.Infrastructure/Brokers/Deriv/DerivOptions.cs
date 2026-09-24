using System.ComponentModel.DataAnnotations;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

public enum DerivAccountType
{
    Demo,
    Real
}

/// <summary>
/// Deriv API settings. The App ID, token and account are normally entered on the dashboard's Settings page and stored
/// (token encrypted) in PostgreSQL. <see cref="ApiToken"/>/<see cref="AppId"/> here are an optional fallback from
/// environment variables (Deriv__ApiToken, Deriv__AppId) and are never put in committed files.
/// </summary>
public sealed class DerivOptions
{
    public const string SectionName = "Deriv";

    [Required, Url] public string RestBaseUrl { get; set; } = "https://api.derivws.com";

    [Required] public string PublicWebSocketUrl { get; set; } = "wss://api.derivws.com/trading/v1/options/ws/public";

    /// <summary>App ID registered at developers.deriv.com (sent as the Deriv-App-ID header).</summary>
    public string? AppId { get; set; }

    /// <summary>Personal Access Token from the Deriv dashboard.</summary>
    public string? ApiToken { get; set; }

    /// <summary>Demo is the default. Real-money accounts are refused in this release.</summary>
    public DerivAccountType AccountType { get; set; } = DerivAccountType.Demo;

    /// <summary>Optional: a specific account id (e.g. "DOT90004580"). Defaults to the first account of <see cref="AccountType"/>.</summary>
    public string? AccountId { get; set; }

    /// <summary>Stake limits for multiplier contracts; the broker's own limits (from each proposal) still apply.</summary>
    [Range(0.01, 100_000)] public decimal MinStake { get; set; } = 1m;

    [Range(1, 100_000)] public decimal MaxStake { get; set; } = 500m;

    /// <summary>Commission as a fraction of notional, used to place stop/target amounts (observed ≈ 2.13 per 10,000).</summary>
    [Range(0, 0.01)] public decimal CommissionRate { get; set; } = 0.000213m;

    /// <summary>The broker's stop-loss price must lie within this fraction of the strategy's stop distance, or the order is not sent.</summary>
    [Range(0.01, 1)] public decimal StopPriceTolerance { get; set; } = 0.25m;

    [Range(2, 120)] public int RequestTimeoutSeconds { get; set; } = 15;
}
