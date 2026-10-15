import { useData } from "../useData";
import { num, signClass } from "../format";
import { Badge, Card, Empty, ErrorNote } from "./Ui";

interface LearningData {
  enabled: boolean;
  lookbackDays: number;
  priorStrength: number;
  maxBoost: number;
  maxPenalty: number;
  disableAfterSamples: number;
  disableBelowR: number;
  outcomes: number;
  combinations: {
    strategy: string;
    regime: string;
    assetClass: string;
    samples: number;
    winRate: number;
    averageR: number;
    shrunkR: number;
    scoreAdjustment: number;
    disabled: boolean;
  }[];
  contextMaxBoost: number;
  contextMaxPenalty: number;
  conditions: {
    strategy: string;
    factor: string;
    condition: string;
    samples: number;
    winRate: number;
    averageR: number;
    baselineR: number;
    shrunkExcessR: number;
    scoreAdjustment: number;
  }[];
}

const conditionLabel: Record<string, string> = {
  Calm: "Quiet news",
  EventRisk: "Release nearby",
  SentimentWith: "Headlines for the trade",
  SentimentAgainst: "Headlines against the trade",
  With: "With the currency trend",
  Against: "Against the currency trend",
  Neutral: "No clear currency trend",
};

export function LearningView({ refreshKey }: { refreshKey: unknown }) {
  const { data, error } = useData<LearningData>("/api/learning", refreshKey);
  return (
    <>
      <ErrorNote error={error} />
      {data && (
        <Card>
          <div className="chips" style={{ marginBottom: 12 }}>
            <Badge tone={data.enabled ? "good" : "neutral"}>{data.enabled ? "learning enabled" : "learning disabled"}</Badge>
            <span className="chip">{data.outcomes.toLocaleString()} resolved setups</span>
            <span className="chip">last {data.lookbackDays} days</span>
            <span className="chip">score range −{data.maxPenalty} … +{data.maxBoost}</span>
            <span className="chip">off after {data.disableAfterSamples} setups below {data.disableBelowR}R</span>
          </div>
          {data.combinations.length === 0 ? (
            <Empty>Nothing learned yet. Virtual trades resolve as markets reach their stops and targets.</Empty>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Strategy</th><th>Regime</th><th>Asset class</th><th className="num">Setups</th><th className="num">Win %</th>
                    <th className="num">Avg R</th><th className="num">Shrunk R</th><th className="num">Score adj.</th><th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {data.combinations.map((c) => (
                    <tr key={`${c.strategy}-${c.regime}-${c.assetClass}`}>
                      <td className="strong">{c.strategy}</td>
                      <td>{c.regime}</td>
                      <td>{c.assetClass}</td>
                      <td className="num">{c.samples}</td>
                      <td className="num">{num(c.winRate, 1)}</td>
                      <td className={`num ${signClass(c.averageR)}`}>{num(c.averageR)}</td>
                      <td className={`num ${signClass(c.shrunkR)}`}>{num(c.shrunkR)}</td>
                      <td className={`num ${signClass(c.scoreAdjustment)}`}>{c.scoreAdjustment > 0 ? "+" : ""}{num(c.scoreAdjustment, 1)}</td>
                      <td>
                        {c.disabled ? <Badge tone="bad">disabled</Badge>
                          : c.samples < 10 ? <Badge tone="neutral">learning</Badge>
                          : c.scoreAdjustment > 0 ? <Badge tone="good">boosted</Badge>
                          : c.scoreAdjustment < 0 ? <Badge tone="warn">penalized</Badge>
                          : <Badge tone="neutral">neutral</Badge>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}
      {data && (
        <Card title="News and trend conditions">
          <p className="muted small" style={{ marginTop: 0 }}>
            How each strategy did under each news and cross-market trend condition, compared with its average. The difference moves new
            setups' scores by −{data.contextMaxPenalty} … +{data.contextMaxBoost}.
          </p>
          {(data.conditions ?? []).length === 0 ? (
            <Empty>Nothing learned about news or trends yet. Setups found from now on record the news around them.</Empty>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Strategy</th><th>Condition</th><th className="num">Setups</th><th className="num">Win %</th>
                    <th className="num">Avg R</th><th className="num">Strategy avg R</th><th className="num">Score adj.</th>
                  </tr>
                </thead>
                <tbody>
                  {data.conditions.map((c) => (
                    <tr key={`${c.strategy}-${c.factor}-${c.condition}`}>
                      <td className="strong">{c.strategy}</td>
                      <td>{conditionLabel[c.condition] ?? c.condition}</td>
                      <td className="num">{c.samples}</td>
                      <td className="num">{num(c.winRate, 1)}</td>
                      <td className={`num ${signClass(c.averageR)}`}>{num(c.averageR)}</td>
                      <td className={`num ${signClass(c.baselineR)}`}>{num(c.baselineR)}</td>
                      <td className={`num ${signClass(c.scoreAdjustment)}`}>{c.scoreAdjustment > 0 ? "+" : ""}{num(c.scoreAdjustment, 1)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Card>
      )}
    </>
  );
}
