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
}

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
    </>
  );
}
