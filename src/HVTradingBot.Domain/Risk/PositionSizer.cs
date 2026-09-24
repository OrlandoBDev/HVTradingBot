namespace HVTradingBot.Domain.Risk;

public sealed record PositionSize(decimal Units, decimal RiskAmount, decimal MaxRiskAmount);

public static class PositionSizer
{
    /// <summary>
    /// Units such that hitting the stop loses at most <see cref="RiskOptions.MaxRiskPerTradePercent"/> of equity,
    /// including expected slippage and round-trip commission, rounded down to <see cref="RiskOptions.UnitStep"/>.
    /// </summary>
    public static PositionSize Calculate(TradeProposal proposal, PortfolioState portfolio, RiskOptions options, decimal expectedSlippagePips,
        decimal commissionPer100K = 0)
    {
        var maxRisk = Math.Round(portfolio.Equity * options.RiskPercentFor(proposal.Instrument) / 100m, 2);
        var stopDistance = proposal.Setup.RiskDistance + proposal.Instrument.FromPips(expectedSlippagePips);
        var rate = portfolio.Converter.QuoteToAccountRate(proposal.Instrument);
        // Commission uses the same basis as PaperExecutionModel.RealizedPnl (account currency per 100k units per side).
        var lossPerUnit = stopDistance * rate + 2m * commissionPer100K / 100_000m;
        if (lossPerUnit <= 0 || maxRisk <= 0)
        {
            return new PositionSize(0, 0, maxRisk);
        }

        // Currency pairs trade in unit steps; other assets (indices, metals, crypto) in fractional units, sized by stake at the broker.
        var step = proposal.Instrument.IsCurrencyPair ? options.UnitStep : 0.000001m;
        var units = Math.Floor(maxRisk / lossPerUnit / step) * step;
        units = Math.Min(units, options.MaxUnits);
        return new PositionSize(units, Math.Round(units * lossPerUnit, 2), maxRisk);
    }
}
