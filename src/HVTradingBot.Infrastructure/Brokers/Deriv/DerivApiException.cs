using HVTradingBot.Application.Abstractions;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

/// <summary>The Deriv API answered with an explicit error (the request was received and rejected).</summary>
public sealed class DerivApiException(string code, string message) : Exception($"Deriv {code}: {message}")
{
    public string Code { get; } = code;
}

/// <summary>
/// The outcome of a request is unknown: the connection dropped or no answer arrived in time.
/// For order submission this must be treated as "may or may not have executed".
/// </summary>
public sealed class DerivConnectionException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>No Deriv App ID / token has been configured yet (Settings page or environment).</summary>
public sealed class DerivNotConfiguredException()
    : BrokerUnavailableException("Deriv is not configured. Add your App ID and Personal Access Token on the Settings page.");
