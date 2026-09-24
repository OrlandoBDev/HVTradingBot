using HVTradingBot.Application.Trading;
using HVTradingBot.Domain.Common;
using HVTradingBot.Domain.MarketData;
using Microsoft.Extensions.Options;

namespace HVTradingBot.Infrastructure.Configuration;

/// <summary>Enforces the safety rules that can be checked at startup (docs/SECURITY.md, ADR-002).</summary>
public sealed class TradingOptionsValidator : IValidateOptions<TradingEngineOptions>
{
    public ValidateOptionsResult Validate(string? name, TradingEngineOptions options)
    {
        var failures = new List<string>();

        if (options.Mode != TradingMode.Paper)
        {
            failures.Add($"Trading:Mode '{options.Mode}' is not available in this release. Only Paper is supported until broker integration and approval workflows are implemented.");
        }

        foreach (var symbol in options.Instruments)
        {
            if (!Instruments.All.Any(i => string.Equals(i.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"Trading:Instruments contains unknown instrument '{symbol}'. Supported: {string.Join(", ", Instruments.All.Select(i => i.Symbol))}.");
            }
        }

        if (options.Instruments.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Instruments.Count)
        {
            failures.Add("Trading:Instruments contains duplicates.");
        }

        var currencies = Instruments.All.SelectMany(i => new[] { i.BaseCurrency, i.QuoteCurrency }).ToHashSet();
        if (!currencies.Contains(options.AccountCurrency))
        {
            failures.Add($"Trading:AccountCurrency '{options.AccountCurrency}' is not supported.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
