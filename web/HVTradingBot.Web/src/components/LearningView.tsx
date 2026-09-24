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
        <Card title="Adaptive learning">
          <p className="hint">
            Every setup the engine finds — traded or not — is followed as a virtual trade until its stop, target or {""}
            expiry. Results per strategy, regime and asset class adjust future scores between −{data.maxPenalty} and +{data.maxBoost} points,
            shrunk towards zero by {data.priorStrength} neutral samples so a few lucky trades cannot dominate. A combination is switched off
            after {data.disableAfterSamples} setups if its expectancy stays below {data.disableBelowR}R. Learning never changes position size or
            risk limits. Window: last {data.lookbackDays} days · {data.outcomes.toLocaleString()} resolved setups ·{" "}
            <Badge tone={data.enabled ? "good" : "neutral"}>{data.enabled ? "enabled" : "disabled"}</Badge>
          </p>
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
