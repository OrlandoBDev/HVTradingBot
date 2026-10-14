using System.Text.RegularExpressions;

namespace HVTradingBot.Domain.News;

/// <summary>
/// Deterministic, dictionary-based headline scoring: which currencies a headline is about and whether it is
/// supportive (+) or negative (−) for each. Deliberately simple and auditable rather than clever: it reads words
/// like "hawkish", "beats", "slumps" and pair quotes like "EUR/USD rises". Headlines it cannot read score 0.
/// </summary>
public static partial class HeadlineSentiment
{
    private static readonly (string Currency, string[] Terms)[] CurrencyTerms =
    [
        ("USD", ["dollar", "greenback", "fed", "fomc", "powell", "u.s.", "us ", "nfp", "nonfarm", "non-farm", "treasury", "treasuries", "usd"]),
        ("EUR", ["euro", "ecb", "lagarde", "eurozone", "euro area", "eur"]),
        ("GBP", ["pound", "sterling", "boe", "bank of england", "bailey", "cable", "uk ", "gbp"]),
        ("JPY", ["yen", "boj", "bank of japan", "ueda", "japan", "jpy"]),
        ("AUD", ["aussie", "australian dollar", "rba", "australia", "aud"]),
        ("NZD", ["kiwi", "rbnz", "new zealand", "nzd"]),
        ("CAD", ["loonie", "canadian dollar", "boc", "bank of canada", "canada", "cad"]),
        ("CHF", ["franc", "snb", "swiss", "chf"]),
        ("CNY", ["yuan", "renminbi", "pboc", "china", "cny"])
    ];

    private static readonly string[] Positive =
    [
        "hawkish", "rate hike", "hikes", "raises rates", "beats", "beat expectations", "tops", "strong", "stronger", "strengthens",
        "surges", "surge", "soars", "rallies", "rally", "rises", "climbs", "gains", "jumps", "firmer", "robust", "upbeat",
        "higher", "boost", "rebounds", "outperforms", "hot", "accelerates", "upgrade", "record high"
    ];

    private static readonly string[] Negative =
    [
        "dovish", "rate cut", "cuts rates", "cuts", "misses", "miss", "weak", "weaker", "weakens", "slumps", "slump", "plunges",
        "tumbles", "falls", "drops", "slides", "declines", "sinks", "lower", "softer", "recession", "contraction", "contracts",
        "slowdown", "downgrade", "retreats", "crisis", "default", "cools", "record low", "sell-off", "selloff"
    ];

    private static readonly string[] Opposition = [" against ", " vs ", " vs. ", " versus "];

    /// <summary>Sentiment per currency mentioned in <paramref name="title"/>; empty when no known currency is mentioned.</summary>
    public static IReadOnlyDictionary<string, decimal> Score(string title)
    {
        var text = $" {title.ToLowerInvariant()} ";
        var tone = Tone(text);
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        // "EUR/USD rises": the base currency gains, the quote currency loses.
        var pair = PairPattern().Match(title);
        if (pair.Success && NewsCurrencies.Known.Contains(pair.Groups[1].Value) && NewsCurrencies.Known.Contains(pair.Groups[2].Value))
        {
            result[pair.Groups[1].Value.ToUpperInvariant()] = tone;
            result[pair.Groups[2].Value.ToUpperInvariant()] = -tone;
            return result;
        }

        // "Dollar rises against the yen": currencies after "against" move the other way.
        var split = Opposition.Select(o => text.IndexOf(o, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        foreach (var (currency, terms) in CurrencyTerms)
        {
            var positions = terms.Select(t => Find(text, t)).Where(i => i >= 0).ToList();
            if (positions.Count == 0)
            {
                continue;
            }

            var first = positions.Min();
            result[currency] = split >= 0 && first > split ? -tone : tone;
        }

        return result;
    }

    /// <summary>(positive − negative) / (positive + negative) keyword hits, in [-1, 1].</summary>
    public static decimal Tone(string lowerText)
    {
        var positive = Positive.Count(w => Find(lowerText, w) >= 0);
        var negative = Negative.Count(w => Find(lowerText, w) >= 0);
        return positive + negative == 0 ? 0 : Math.Round((decimal)(positive - negative) / (positive + negative), 3);
    }

    /// <summary>Whole-word (or whole-phrase) position of <paramref name="term"/>, or -1.</summary>
    private static int Find(string text, string term)
    {
        var start = 0;
        while ((start = text.IndexOf(term, start, StringComparison.Ordinal)) >= 0)
        {
            var before = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
            var end = start + term.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]) || !char.IsLetterOrDigit(term[^1]);
            if (before && after)
            {
                return start;
            }

            start++;
        }

        return -1;
    }

    [GeneratedRegex(@"\b([A-Za-z]{3})\s?/\s?([A-Za-z]{3})\b")]
    private static partial Regex PairPattern();
}
