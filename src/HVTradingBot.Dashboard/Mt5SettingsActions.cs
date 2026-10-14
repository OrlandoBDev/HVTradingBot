using System.Text.RegularExpressions;
using HVTradingBot.Application.Abstractions;
using HVTradingBot.Contracts;
using HVTradingBot.Infrastructure.Bridge;
using HVTradingBot.Infrastructure.Settings;

namespace HVTradingBot.Dashboard;

/// <summary>
/// Settings › Broker account › MetaTrader 5, reached through MetaApi's cloud or the Expert Advisor bridge on the computer
/// running the bot. The dashboard only stores the settings; the worker switches brokers when they change (it restarts)
/// and reports whether the account connects.
/// </summary>
public sealed partial class Mt5SettingsActions(Mt5SettingsStore store, ITradingStateStore state, IDecisionJournal journal, Mt5BridgeStore? bridge = null)
{
    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex AccountIdPattern();

    [GeneratedRegex(@"^\S{20,8000}$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._#-]{0,16}$")]
    private static partial Regex SuffixPattern();

    public async Task<Mt5SettingsDto> GetAsync(CancellationToken ct) => await ToDtoAsync(await store.GetViewAsync(ct), ct);

    public async Task<DashboardResult> SaveAsync(Mt5SettingsRequest request, DashboardCaller caller, CancellationToken ct)
    {
        var current = await store.GetViewAsync(ct);
        var connection = request.Connection ?? current.Connection;
        var bridgeMode = connection == Mt5SettingsStore.BridgeConnection;
        var token = string.IsNullOrWhiteSpace(request.Token) ? null : request.Token.Trim();
        var accountId = request.AccountId?.Trim();
        var region = string.IsNullOrWhiteSpace(request.Region) ? Mt5SettingsStore.MetaApiRegionDefault : request.Region.Trim();
        var suffix = request.SymbolSuffix?.Trim() ?? "";
        var commission = request.CommissionPercent ?? Mt5SettingsStore.DefaultCommissionPercent;

        var errors = new Dictionary<string, string[]>();
        if (connection is not (Mt5SettingsStore.MetaApiConnection or Mt5SettingsStore.BridgeConnection)) errors["connection"] = ["Choose MetaApi or MT5 on this computer."];
        if (token is not null && !TokenPattern().IsMatch(token)) errors["token"] = ["Paste the whole MetaApi token (no spaces)."];
        if (!string.IsNullOrEmpty(accountId) && !AccountIdPattern().IsMatch(accountId)) errors["accountId"] = ["The MetaApi account id has letters, digits and dashes only."];
        if (!Mt5SettingsStore.Regions.Contains(region)) errors["region"] = [$"Choose one of: {string.Join(", ", Mt5SettingsStore.Regions)}."];
        if (!SuffixPattern().IsMatch(suffix)) errors["symbolSuffix"] = ["Up to 16 characters without spaces, e.g. .r or leave empty."];
        if (commission is < 0 or > 0.1m) errors["commissionPercent"] = ["Between 0% and 0.1% of the position (round trip)."];
        if (request.Enabled && !bridgeMode && token is null && !current.TokenConfigured) errors["token"] = ["Enter the MetaApi token to switch MT5 on."];
        if (request.Enabled && !bridgeMode && string.IsNullOrEmpty(accountId)) errors["accountId"] = ["Enter the MetaApi account id to switch MT5 on."];
        if (errors.Count > 0)
        {
            return DashboardResult.Invalid(errors);
        }

        var saved = await store.SaveAsync(request.Enabled, token, accountId, region, suffix, commission, caller.Actor, ct, connection,
            request.NewBridgeKey);
        await journal.RecordAuditAsync(caller.Actor, "Mt5SettingsUpdated",
            $"MT5 {(saved.Enabled ? "on" : "off")} via {(bridgeMode ? "the Expert Advisor bridge" : "MetaApi")}; " +
            (bridgeMode ? "" : $"account {saved.AccountId ?? "-"}; region {saved.Region}; ") +
            $"suffix '{saved.SymbolSuffix}'; commission {saved.CommissionPercent}%{(token is null ? "" : "; token replaced")}" +
            $"{(request.NewBridgeKey ? "; new bridge key" : "")} (version {saved.Version})", caller.CorrelationId, ct);
        return DashboardResult.Ok(await ToDtoAsync(saved, ct));
    }

    public async Task<Mt5SettingsDto> ClearAsync(DashboardCaller caller, CancellationToken ct)
    {
        await store.ClearAsync(caller.Actor, ct);
        await journal.RecordAuditAsync(caller.Actor, "Mt5SettingsCleared", "MT5 switched off (MetaApi token removed); Forex back on Deriv multipliers.",
            caller.CorrelationId, ct);
        return await GetAsync(ct);
    }

    private async Task<Mt5SettingsDto> ToDtoAsync(Mt5SettingsView v, CancellationToken ct)
    {
        var active = (await state.GetAsync(ct)).BrokerName == Mt5SettingsStore.BrokerName;
        var bridgeState = bridge is null || v.Connection != Mt5SettingsStore.BridgeConnection ? null : await bridge.GetStateAsync(ct);
        return new Mt5SettingsDto(v.Enabled, v.TokenConfigured, v.TokenHint, v.AccountId, v.Region, v.SymbolSuffix, v.CommissionPercent, v.Version,
            v.UpdatedAtUtc, v.UpdatedBy, active,
            v.Status is { } s
                ? new Mt5StatusDto(s.State, s.Message, s.SettingsVersion == v.Version, s.Login, s.Server, s.Broker, s.IsDemo, s.Balance, s.Currency,
                    s.CheckedAtUtc)
                : null,
            Mt5SettingsStore.Regions, v.Connection, v.Connection == Mt5SettingsStore.BridgeConnection ? v.BridgeKey : null,
            bridgeState?.LastSeenUtc, bridgeState?.Version);
    }
}
