import { useData } from "../useData";
import { num, signClass, time } from "../format";
import { Badge, Card, Empty, ErrorNote, MarketName } from "./Ui";

interface EventItem {
  currency: string;
  title: string;
  timeUtc: string;
  impact: string;
}

interface NewsData {
  enabled: boolean;
  provider: string;
  active: boolean;
  source: string;
  updatedUtc: string | null;
  calendarAvailable: boolean;
  asOfUtc: string;
  rules: {
    blackoutMinutesBefore: number;
    blackoutMinutesAfter: number;
    cautionMinutes: number;
    cautionRiskMultiplier: number;
    opposedSentimentRiskMultiplier: number;
    maxSentimentBoost: number;
    maxTrendBoost: number;
    maxSentimentPenalty: number;
    maxTrendPenalty: number;
  };
  markets: {
    instrument: string;
    displayName: string;
    currencies: string[];
    status: "blocked" | "reduced" | "clear" | "unaffected";
    riskMultiplier: number;
    longSentiment: number | null;
    note: string | null;
    nextEvent: { currency: string; title: string; timeUtc: string; impact: string } | null;
  }[];
  events: EventItem[];
  currencies: { currency: string; sentiment: number; headlines: number }[];
  headlines: { publishedUtc: string; source: string; title: string; sentiment: Record<string, number> }[];
}

const statusBadge = (status: NewsData["markets"][number]["status"], multiplier: number) =>
  status === "blocked" ? <Badge tone="bad">no new trades</Badge>
  : status === "reduced" ? <Badge tone="warn">size × {num(multiplier, 2)}</Badge>
  : status === "clear" ? <Badge tone="good">clear</Badge>
  : <Badge tone="neutral">not news-driven</Badge>;

const impactTone = (impact: string) => (impact === "High" ? "bad" : impact === "Medium" ? "warn" : "neutral");

const signed = (value: number, digits = 2) => `${value > 0 ? "+" : ""}${num(value, digits)}`;

export function NewsView({ refreshKey }: { refreshKey: unknown }) {
  const { data, error } = useData<NewsData>("/api/news", refreshKey);
  return (
    <>
      <ErrorNote error={error} />
      {data && (
        <>
          <Card>
            <div className="chips">
              <Badge tone={data.active ? "good" : "neutral"}>{data.active ? `news: ${data.provider.toLowerCase()}` : "news off"}</Badge>
              {data.active && (
                <Badge tone={data.calendarAvailable ? "good" : "warn"}>
                  {data.calendarAvailable ? "economic calendar loaded" : "economic calendar unavailable"}
                </Badge>
              )}
              <span className="chip">updated {time(data.updatedUtc)}</span>
              <span className="chip">no trades {data.rules.blackoutMinutesBefore}m before → {data.rules.blackoutMinutesAfter}m after high-impact releases</span>
              <span className="chip">size × {num(data.rules.cautionRiskMultiplier, 2)} within {data.rules.cautionMinutes}m of other releases</span>
              <span className="chip">
                score: news −{data.rules.maxSentimentPenalty} … +{data.rules.maxSentimentBoost}, trend −{data.rules.maxTrendPenalty} … +{data.rules.maxTrendBoost}
              </span>
            </div>
          </Card>

          <Card title="Your markets now">
            {data.markets.length === 0 ? (
              <Empty>No markets selected.</Empty>
            ) : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th>Market</th><th>News status</th><th className="num">Sentiment (buy)</th><th>Next release</th><th>Why</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.markets.map((m) => (
                      <tr key={m.instrument}>
                        <td><MarketName symbol={m.instrument} /></td>
                        <td>{statusBadge(m.status, m.riskMultiplier)}</td>
                        <td className={`num ${signClass(m.longSentiment)}`}>{m.longSentiment == null ? "—" : signed(m.longSentiment)}</td>
                        <td>{m.nextEvent ? <>{time(m.nextEvent.timeUtc)} <span className="muted">{m.nextEvent.title}</span></> : <span className="muted">—</span>}</td>
                        <td className="muted small">{m.note ?? ""}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <div className="two-col">
            <Card title="Economic calendar">
              {data.events.length === 0 ? (
                <Empty>{data.calendarAvailable ? "No medium- or high-impact releases in the next 36 hours." : "The calendar could not be loaded."}</Empty>
              ) : (
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr><th>Time</th><th>Currency</th><th>Release</th><th>Impact</th></tr>
                    </thead>
                    <tbody>
                      {data.events.map((e) => (
                        <tr key={`${e.currency}-${e.timeUtc}-${e.title}`} className={e.timeUtc < data.asOfUtc ? "muted" : ""}>
                          <td>{time(e.timeUtc)}</td>
                          <td className="strong">{e.currency}</td>
                          <td>{e.title}</td>
                          <td><Badge tone={impactTone(e.impact)}>{e.impact}</Badge></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>

            <Card title="Currency sentiment">
              {data.currencies.length === 0 ? (
                <Empty>No recent headlines about the major currencies.</Empty>
              ) : (
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr><th>Currency</th><th className="num">Sentiment</th><th className="num">Headlines</th></tr>
                    </thead>
                    <tbody>
                      {data.currencies.map((c) => (
                        <tr key={c.currency}>
                          <td className="strong">{c.currency}</td>
                          <td className={`num ${signClass(c.sentiment)}`}>{signed(c.sentiment)}</td>
                          <td className="num">{c.headlines}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </div>

          <Card title="Recent headlines">
            {data.headlines.length === 0 ? (
              <Empty>No headlines yet.</Empty>
            ) : (
              <div className="table-wrap">
                <table>
                  <thead>
                    <tr><th>Time</th><th>Headline</th><th>Reads as</th><th>Source</th></tr>
                  </thead>
                  <tbody>
                    {data.headlines.map((h) => (
                      <tr key={`${h.publishedUtc}-${h.title}`}>
                        <td>{time(h.publishedUtc)}</td>
                        <td>{h.title}</td>
                        <td>
                          <div className="chips">
                            {Object.entries(h.sentiment).map(([currency, value]) => (
                              <Badge key={currency} tone={value > 0 ? "good" : value < 0 ? "bad" : "neutral"}>
                                {currency} {value > 0 ? "▲" : value < 0 ? "▼" : "•"}
                              </Badge>
                            ))}
                          </div>
                        </td>
                        <td className="muted small">{h.source}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </>
      )}
    </>
  );
}
