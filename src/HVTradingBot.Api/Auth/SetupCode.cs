using System.Security.Cryptography;

namespace HVTradingBot.Api.Auth;

/// <summary>
/// One-time code needed to create the owner account. It exists only in memory and in the API log, so the account can
/// be claimed only by someone who can read the server's log, never by a visitor who finds the dashboard first.
/// </summary>
public sealed class SetupCode(ILogger<SetupCode> logger)
{
    private readonly Lock _gate = new();
    private string? _code;

    /// <summary>Returns the current code, creating (and logging) one if needed.</summary>
    public string Current()
    {
        lock (_gate)
        {
            if (_code is null)
            {
                _code = $"{RandomNumberGenerator.GetHexString(4)}-{RandomNumberGenerator.GetHexString(4)}".ToUpperInvariant();
                logger.LogWarning("No dashboard login exists yet. Open the dashboard and create it with setup code {SetupCode}", _code);
            }

            return _code;
        }
    }

    public bool Matches(string? candidate)
    {
        var expected = Current();
        return candidate is not null && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(candidate.Trim().ToUpperInvariant()));
    }

    /// <summary>Used once the account exists; a new code is created if the login is ever reset.</summary>
    public void Consume()
    {
        lock (_gate)
        {
            _code = null;
        }
    }
}
