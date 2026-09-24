import type { RiskStatus } from "../types";
import type { PageId } from "../pages";
import { useData } from "../useData";
import { Badge, Card, ErrorNote } from "./Ui";

export function Risk({ refreshKey, navigate }: { refreshKey: unknown; navigate: (page: PageId, section?: string) => void }) {
  const { data, error } = useData<RiskStatus>("/api/risk", refreshKey);
  return (
    <>
      <ErrorNote error={error} />
      {data && (
        <>
          <Card title="Can the engine trade right now?" actions={<button className="link" onClick={() => navigate("settings", "risk")}>Edit limits →</button>}>
            <p>
              New trades:{" "}
              <Badge tone={data.newTradesAllowed ? "good" : "bad"}>{data.newTradesAllowed ? "allowed" : "blocked"}</Badge>
            </p>
            {data.blockingReasons.length > 0 && (
              <ul className="reasons-list">
                {data.blockingReasons.map((r) => <li key={r}>{r}</li>)}
              </ul>
            )}
          </Card>
          <Card title="Limits">
            <div className="table-wrap">
              <table>
                <thead><tr><th>Rule</th><th className="num">Current</th><th className="num">Limit</th><th>Status</th></tr></thead>
                <tbody>
                  {data.limits.map((l) => (
                    <tr key={l.name}>
                      <td className="strong">{l.name}</td>
                      <td className="num">{l.current}</td>
                      <td className="num">{l.limit}</td>
                      <td><Badge tone={l.breached ? "bad" : "good"}>{l.breached ? "breached" : "ok"}</Badge></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
          <Card title="Exposure by currency / asset (net same-direction positions)">
            {Object.keys(data.currencyExposure).length === 0 ? (
              <p className="empty">No exposure.</p>
            ) : (
              <div className="chips">
                {Object.entries(data.currencyExposure).map(([ccy, n]) => (
                  <span key={ccy} className="chip">{ccy} <b className={n > 0 ? "pos" : n < 0 ? "neg" : ""}>{n > 0 ? `+${n}` : n}</b></span>
                ))}
              </div>
            )}
          </Card>
        </>
      )}
    </>
  );
}
