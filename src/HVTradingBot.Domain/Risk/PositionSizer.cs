namespace HVTradingBot.Domain.Risk;

public sealed record PositionSize(decimal Units, decimal RiskAmount, decimal MaxRiskAmount);

public static class PositionSizer
{
    /// <summary>
    /// Units such that hitting the stop loses at most <see cref="RiskOptions.MaxRiskPerTradePercent"/> of equity,
    /// including expected slippage and the assumed round-trip commission, rounded down to <see cref="RiskOptions.UnitStep"/>.
    /// A news multiplier below 1 (<see cref="TradeProposal.NewsRiskMultiplier"/>) shrinks the risk budget; it never grows it.
    /// </summary>
    public static PositionSize Calculate(TradeProposal proposal, PortfolioState portfolio, RiskOptions options, decimal expectedSlippagePips)
    {
        var newsMultiplier = Math.Clamp(proposal.NewsRiskMultiplier, 0m, 1m);
        var maxRisk = Math.Round(portfolio.Equity * options.RiskPercentFor(proposal.Instrument) / 100m * newsMultiplier, 2);
        var stopDistance = proposal.Setup.RiskDistance + proposal.Instrument.FromPips(expectedSlippagePips);
        var rate = portfolio.Converter.QuoteToAccountRate(proposal.Instrument);
        // Round-trip commission on the position value (units x price in account currency), as in PaperExecutionModel.
        var commissionPerUnit = options.AssumedCommissionPercent / 100m * proposal.Setup.Entry * rate;
        var lossPerUnit = stopDistance * rate + commissionPerUnit;
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
