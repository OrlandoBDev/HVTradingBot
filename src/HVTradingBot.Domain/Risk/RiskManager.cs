using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.Execution;

namespace HVTradingBot.Domain.Risk;

/// <summary>
/// Deterministic risk engine with final authority. Every rule is evaluated (not short-circuited) so the journal
/// records all reasons a proposal was rejected. Limits are read from one snapshot per evaluation.
/// </summary>
public sealed class RiskManager(IRiskOptionsSource source, ExecutionCostOptions costs) : IRiskManager
{
    public RiskManager(RiskOptions options, ExecutionCostOptions costs) : this(new FixedRiskOptions(options), costs)
    {
    }

    public Task<RiskDecision> EvaluateAsync(TradeProposal proposal, PortfolioState portfolio, CancellationToken cancellationToken) =>
        new RiskRules(source.Current, costs).EvaluateAsync(proposal, portfolio, cancellationToken);
}

internal sealed class RiskRules(RiskOptions options, ExecutionCostOptions costs)
{
    public Task<RiskDecision> EvaluateAsync(TradeProposal proposal, PortfolioState portfolio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var size = PositionSizer.Calculate(proposal, portfolio, options, costs.SlippagePips);
        var overrideCheck = HighScoreOverride(proposal, portfolio, size);
        var checks = new List<RiskCheck>
        {
            TradingMode(portfolio),
            KillSwitch(portfolio),
            MarketDataFreshness(portfolio),
            RewardToRisk(proposal),
            StopDistance(proposal),
            Spread(proposal),
            Slippage(),
            OpenPositions(portfolio, overrideCheck),
            DerivedPositions(proposal, portfolio, overrideCheck),
            DuplicateInstrument(proposal, portfolio, overrideCheck),
            DailyLoss(portfolio),
            DerivedDailyLoss(proposal, portfolio),
            WeeklyLoss(portfolio),
            Cooldown(portfolio),
            CurrencyExposure(proposal, portfolio),
            Tradable(proposal),
            Sizing(proposal, size)
        };
        if (overrideCheck is not null)
        {
            checks.Add(overrideCheck);
        }

        var approved = checks.All(c => c.Passed);
        return Task.FromResult(new RiskDecision(approved, approved ? size.Units : 0, approved ? size.RiskAmount : 0, checks));
    }

    private static RiskCheck TradingMode(PortfolioState p) => p.Mode == Common.TradingMode.Paper
        ? Pass(nameof(TradingMode), "PAPER mode.")
        : Fail(nameof(TradingMode), $"{p.Mode} execution is not enabled in this release.");

    private static RiskCheck KillSwitch(PortfolioState p) => p.KillSwitchActive
        ? Fail(nameof(KillSwitch), $"Kill switch active: {p.KillSwitchReason}")
        : Pass(nameof(KillSwitch), "Inactive.");

    private RiskCheck MarketDataFreshness(PortfolioState p) =>
        p.MarketData.IsStale(p.WallClockUtc, TimeSpan.FromSeconds(options.MaxMarketDataAgeSeconds))
            ? Fail(nameof(MarketDataFreshness), $"No market data for more than {options.MaxMarketDataAgeSeconds}s.")
            : Pass(nameof(MarketDataFreshness), "Fresh.");

    private RiskCheck RewardToRisk(TradeProposal proposal) => proposal.Setup.RewardToRisk >= options.MinRewardToRisk
        ? Pass(nameof(RewardToRisk), $"R:R {proposal.Setup.RewardToRisk:F2}.")
        : Fail(nameof(RewardToRisk), $"R:R {proposal.Setup.RewardToRisk:F2} below minimum {options.MinRewardToRisk:F2}.");

    private RiskCheck StopDistance(TradeProposal proposal)
    {
        var spreads = proposal.Quote.Spread == 0 ? decimal.MaxValue : proposal.Setup.RiskDistance / proposal.Quote.Spread;
        return spreads >= options.MinStopSpreadMultiple
            ? Pass(nameof(StopDistance), spreads == decimal.MaxValue ? "No spread." : $"Stop is {spreads:F1} spreads away.")
            : Fail(nameof(StopDistance), $"Stop is only {spreads:F1} spreads away (minimum {options.MinStopSpreadMultiple:F0}).");
    }

    private RiskCheck Spread(TradeProposal proposal)
    {
        var pips = proposal.Instrument.ToPips(proposal.Quote.Spread);
        // The absolute pip cap applies to currency pairs; other assets are checked against their own recent spread.
        if (proposal.Instrument.IsCurrencyPair && pips > options.MaxSpreadPips)
        {
            return Fail(nameof(Spread), $"Spread {pips:F1} pips exceeds {options.MaxSpreadPips:F1}.");
        }

        if (proposal.AverageSpread > 0 && proposal.Quote.Spread > proposal.AverageSpread * options.MaxSpreadToAverageMultiple)
        {
            return Fail(nameof(Spread), $"Spread {pips:F1} pips is abnormal vs recent average.");
        }

        return Pass(nameof(Spread), $"{pips:F1} pips.");
    }

    private RiskCheck Slippage() => costs.SlippagePips <= options.MaxSlippagePips
        ? Pass(nameof(Slippage), $"Expected {costs.SlippagePips:F1} pips.")
        : Fail(nameof(Slippage), $"Expected slippage {costs.SlippagePips:F1} pips exceeds {options.MaxSlippagePips:F1}.");

    /// <summary>
    /// Forex slots: Derived positions beyond <see cref="RiskOptions.MaxDerivedOpenPositions"/> (high-score extras) do not
    /// count, so they can never take a slot Forex needs.
    /// </summary>
    private RiskCheck OpenPositions(PortfolioState p, RiskCheck? highScoreOverride)
    {
        if (highScoreOverride is { Passed: true } && p.DerivedOpenPositions >= options.MaxDerivedOpenPositions)
        {
            return Pass(nameof(OpenPositions), "High-score Derived extra; does not use a Forex slot.");
        }

        var used = p.OpenPositions.Count - p.DerivedOpenPositions + Math.Min(p.DerivedOpenPositions, options.MaxDerivedOpenPositions);
        return used < options.MaxOpenPositions
            ? Pass(nameof(OpenPositions), $"{used}/{options.MaxOpenPositions} open.")
            : Fail(nameof(OpenPositions), $"Maximum of {options.MaxOpenPositions} open positions reached.");
    }

    private RiskCheck DerivedPositions(TradeProposal proposal, PortfolioState p, RiskCheck? highScoreOverride)
    {
        if (!RiskOptions.IsDerived(proposal.Instrument))
        {
            return Pass(nameof(DerivedPositions), "Not a Derived market.");
        }

        if (p.DerivedOpenPositions < options.MaxDerivedOpenPositions)
        {
            return Pass(nameof(DerivedPositions), $"{p.DerivedOpenPositions}/{options.MaxDerivedOpenPositions} Derived open.");
        }

        return highScoreOverride is { Passed: true }
            ? Pass(nameof(DerivedPositions), $"{p.DerivedOpenPositions} Derived open; allowed as a high-score extra.")
            : Fail(nameof(DerivedPositions), $"Maximum of {options.MaxDerivedOpenPositions} Derived position(s) reached; remaining slots are kept for Forex.");
    }

    /// <summary>
    /// Present only when a Derived proposal is blocked by the Derived position limit or an open position on the same
    /// market. Passes when the score is high enough, an extra slot is free and every open Derived position plus this
    /// one could hit its stop without Derived losing more than its daily limit, so Forex's loss budget is untouched.
    /// </summary>
    private RiskCheck? HighScoreOverride(TradeProposal proposal, PortfolioState p, PositionSize size)
    {
        var sameMarketOpen = p.OpenPositions.Any(x => x.Instrument == proposal.Instrument);
        if (!RiskOptions.IsDerived(proposal.Instrument) || (p.DerivedOpenPositions < options.MaxDerivedOpenPositions && !sameMarketOpen))
        {
            return null;
        }

        const string rule = nameof(HighScoreOverride);
        if (options.MaxExtraDerivedPositions == 0)
        {
            return Fail(rule, "High-score override is off.");
        }

        if (proposal.Score < options.HighScoreOverrideMinScore)
        {
            return Fail(rule, $"Score {proposal.Score} is below the override minimum {options.HighScoreOverrideMinScore}.");
        }

        var limit = options.MaxDerivedOpenPositions + options.MaxExtraDerivedPositions;
        if (p.DerivedOpenPositions >= limit)
        {
            return Fail(rule, $"All {options.MaxExtraDerivedPositions} extra Derived position(s) are in use.");
        }

        var budget = p.Balance * options.MaxDerivedDailyLossPercent / 100m - Math.Max(0m, -p.DerivedDailyRealizedPnl);
        var openRisk = p.OpenPositions.Where(x => RiskOptions.IsDerived(x.Instrument)).Sum(x => x.InitialRiskAmount);
        var total = openRisk + size.RiskAmount;
        return total <= budget
            ? Pass(rule, $"Score {proposal.Score} ≥ {options.HighScoreOverrideMinScore}: extra Derived position allowed; Derived risk {total:F2} of remaining budget {Math.Max(0m, budget):F2}.")
            : Fail(rule, $"Score {proposal.Score} qualifies, but open Derived risk {openRisk:F2} + {size.RiskAmount:F2} would exceed today's remaining Derived loss budget {Math.Max(0m, budget):F2}.");
    }

    private RiskCheck DerivedDailyLoss(TradeProposal proposal, PortfolioState p)
    {
        if (!RiskOptions.IsDerived(proposal.Instrument))
        {
            return Pass(nameof(DerivedDailyLoss), "Not a Derived market.");
        }

        var limit = p.Balance * options.MaxDerivedDailyLossPercent / 100m;
        return -p.DerivedDailyRealizedPnl >= limit
            ? Fail(nameof(DerivedDailyLoss), $"Derived loss today {-p.DerivedDailyRealizedPnl:F2} reached its limit {limit:F2}; Derived pauses until tomorrow.")
            : Pass(nameof(DerivedDailyLoss), $"Derived P&L today {p.DerivedDailyRealizedPnl:F2}.");
    }

    private static RiskCheck DuplicateInstrument(TradeProposal proposal, PortfolioState p, RiskCheck? highScoreOverride)
    {
        if (p.OpenPositions.Any(x => x.ClientOrderId == proposal.ClientOrderId))
        {
            return Fail(nameof(DuplicateInstrument), "This signal was already executed.");
        }

        if (!p.OpenPositions.Any(x => x.Instrument == proposal.Instrument))
        {
            return Pass(nameof(DuplicateInstrument), "No existing position.");
        }

        return highScoreOverride is { Passed: true }
            ? Pass(nameof(DuplicateInstrument), $"A position on {proposal.Instrument} is open; adding one as a high-score extra.")
            : Fail(nameof(DuplicateInstrument), $"A position on {proposal.Instrument} is already open.");
    }

    private RiskCheck DailyLoss(PortfolioState p)
    {
        var limit = p.Balance * options.MaxDailyLossPercent / 100m;
        return -p.DailyRealizedPnl >= limit
            ? Fail(nameof(DailyLoss), $"Daily loss {-p.DailyRealizedPnl:F2} reached limit {limit:F2}.")
            : Pass(nameof(DailyLoss), $"Daily P&L {p.DailyRealizedPnl:F2}.");
    }

    private RiskCheck WeeklyLoss(PortfolioState p)
    {
        var limit = p.Balance * options.MaxWeeklyLossPercent / 100m;
        return -p.WeeklyRealizedPnl >= limit
            ? Fail(nameof(WeeklyLoss), $"Weekly loss {-p.WeeklyRealizedPnl:F2} reached limit {limit:F2}.")
            : Pass(nameof(WeeklyLoss), $"Weekly P&L {p.WeeklyRealizedPnl:F2}.");
    }

    private static RiskCheck Cooldown(PortfolioState p) => p.CooldownUntilUtc is { } until && until > p.MarketTimeUtc
        ? Fail(nameof(Cooldown), $"Cooling down after {p.ConsecutiveLosses} consecutive losses until {until:u}.")
        : Pass(nameof(Cooldown), $"{p.ConsecutiveLosses} consecutive losses.");

    private RiskCheck CurrencyExposure(TradeProposal proposal, PortfolioState p)
    {
        var exposure = new Dictionary<string, int>(p.CurrencyExposure());
        PortfolioState.Add(exposure, proposal.Instrument, proposal.Setup.Direction);
        var breached = exposure.Where(e => Math.Abs(e.Value) > options.MaxCurrencyExposure).Select(e => $"{e.Key} {e.Value:+#;-#;0}").ToList();
        return breached.Count > 0
            ? Fail(nameof(CurrencyExposure), $"Correlated exposure too high: {string.Join(", ", breached)}.")
            : Pass(nameof(CurrencyExposure), "Within limits.");
    }

    private RiskCheck Tradable(TradeProposal proposal) => proposal.Instrument.IsTradable
        ? Pass(nameof(Tradable), "Tradable at the broker.")
        : Fail(nameof(Tradable), $"{proposal.Instrument.DisplayName} is analysis-only: the broker offers no contract type this app can trade.");

    private RiskCheck Sizing(TradeProposal proposal, PositionSize size) =>
        size.Units > 0 && (!proposal.Instrument.IsCurrencyPair || size.Units >= options.MinUnits)
        ? Pass(nameof(Sizing), $"{size.Units:N0} units risking {size.RiskAmount:F2} (max {size.MaxRiskAmount:F2}).")
        : Fail(nameof(Sizing), $"Position size {size.Units:N0} below minimum {options.MinUnits:N0}; stop too wide for risk budget.");

    private static RiskCheck Pass(string rule, string detail) => new(rule, true, detail);

    private static RiskCheck Fail(string rule, string detail) => new(rule, false, detail);
}
