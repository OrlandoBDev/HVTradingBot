using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using HVTradingBot.Domain.MarketData;
using HVTradingBot.Infrastructure.Markets;
using HVTradingBot.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Logging;

namespace HVTradingBot.Infrastructure.Brokers.Deriv;

/// <summary>
/// Reads Deriv's market list (public API, no authentication) and records, per market, whether multiplier contracts
/// exist. Markets without multipliers (e.g. stock indices) are kept as analysis-only.
/// </summary>
public sealed class DerivMarketDiscovery(
    DerivOptions options,
    IDerivSocketFactory socketFactory,
    MarketCatalogStore store,
    ILogger<DerivMarketDiscovery> logger)
{
    private static readonly Dictionary<string, AssetClass> Markets = new()
    {
        ["forex"] = AssetClass.Forex,
        ["commodities"] = AssetClass.Commodity,
        ["cryptocurrency"] = AssetClass.Crypto,
        ["synthetic_index"] = AssetClass.SyntheticIndex,
        ["indices"] = AssetClass.StockIndex
    };

    public async Task<int> RefreshAsync(CancellationToken cancellationToken)
    {
        await using var socket = await socketFactory.ConnectAsync(new Uri(options.PublicWebSocketUrl),
            TimeSpan.FromSeconds(options.RequestTimeoutSeconds), cancellationToken);

        var symbols = (await socket.SendAsync(new JsonObject { ["active_symbols"] = "brief" }, cancellationToken)).GetProperty("active_symbols");
        var markets = new List<MarketEntity>();
        foreach (var s in symbols.EnumerateArray())
        {
            var brokerSymbol = s.GetProperty("underlying_symbol").GetString()!;
            var market = s.GetProperty("market").GetString() ?? "";
            if (!Markets.TryGetValue(market, out var assetClass))
            {
                continue;
            }

            var multipliers = await MultipliersAsync(socket, brokerSymbol, cancellationToken);
            var entity = ToEntity(s, brokerSymbol, market, assetClass, multipliers);
            if (entity is not null)
            {
                markets.Add(entity);
            }
        }

        await store.SaveCatalogAsync(markets, cancellationToken);
        logger.LogInformation("Deriv market catalog: {Total} markets, {Tradable} with multiplier contracts", markets.Count, markets.Count(m => m.IsTradable));
        return markets.Count;
    }

    private static async Task<IReadOnlyList<int>> MultipliersAsync(IDerivSocket socket, string brokerSymbol, CancellationToken cancellationToken)
    {
        try
        {
            var response = await socket.SendAsync(new JsonObject { ["contracts_for"] = brokerSymbol }, cancellationToken);
            return response.GetProperty("contracts_for").GetProperty("available").EnumerateArray()
                .Where(a => a.TryGetProperty("contract_type", out var t) && t.GetString() == "MULTUP" && a.TryGetProperty("multiplier_range", out _))
                .SelectMany(a => a.GetProperty("multiplier_range").EnumerateArray().Select(m => m.GetInt32()))
                .Distinct().Order().ToList();
        }
        catch (DerivApiException)
        {
            return []; // e.g. contracts not offered right now: analysis-only.
        }
    }

    public static MarketEntity? ToEntity(JsonElement s, string brokerSymbol, string market, AssetClass assetClass, IReadOnlyList<int> multipliers)
    {
        string symbol, baseCurrency, quoteCurrency;
        switch (assetClass)
        {
            case AssetClass.Forex or AssetClass.Commodity when brokerSymbol.StartsWith("frx", StringComparison.Ordinal) && brokerSymbol.Length == 9:
                baseCurrency = brokerSymbol[3..6];
                quoteCurrency = brokerSymbol[6..9];
                symbol = $"{baseCurrency}/{quoteCurrency}";
                break;
            case AssetClass.Crypto when brokerSymbol.StartsWith("cry", StringComparison.Ordinal) && brokerSymbol.EndsWith("USD", StringComparison.Ordinal):
                baseCurrency = brokerSymbol[3..^3];
                quoteCurrency = "USD";
                symbol = $"{baseCurrency}/USD";
                break;
            case AssetClass.SyntheticIndex or AssetClass.StockIndex:
                symbol = brokerSymbol;
                baseCurrency = brokerSymbol;
                quoteCurrency = "USD";
                break;
            default:
                return null;
        }

        var pip = s.TryGetProperty("pip_size", out var p) ? p.GetDecimal() : 0.01m;
        return new MarketEntity
        {
            BrokerSymbol = brokerSymbol,
            Symbol = symbol,
            Name = s.TryGetProperty("underlying_symbol_name", out var n) ? n.GetString() ?? symbol : symbol,
            Market = market,
            Submarket = s.TryGetProperty("submarket", out var sm) ? sm.GetString() ?? "" : "",
            AssetClass = assetClass.ToString(),
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            PipSize = pip,
            PriceDecimals = Math.Max(0, (int)Math.Round(-Math.Log10((double)pip))),
            Multipliers = JsonSerializer.Serialize(multipliers),
            IsTradable = multipliers.Count > 0,
            IsOpen = s.TryGetProperty("exchange_is_open", out var o) && o.ValueKind == JsonValueKind.Number && o.GetInt32() == 1
        };
    }
}
