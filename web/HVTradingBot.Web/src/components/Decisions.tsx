import { Fragment, useState } from "react";
import type { Decision } from "../types";
import { api } from "../api";
import { price, time } from "../format";
import { useData } from "../useData";
import { Badge, Card, Empty, ErrorNote, RegimeBadge, stateTone } from "./Ui";

const FILTERS: Record<string, string> = {
  "Candidates & trades": "Candidate,Observe,RejectedByRisk,Executed,Expired",
  "Rejected by risk": "RejectedByRisk",
  Executed: "Executed",
  "All (incl. NO_TRADE)": "",
};

export function Decisions({ refreshKey }: { refreshKey: unknown }) {
  const [filter, setFilter] = useState(Object.keys(FILTERS)[0]);
  const [detail, setDetail] = useState<unknown>(null);
  const [selected, setSelected] = useState<string | null>(null);
  const { data, error } = useData<Decision[]>(`/api/decisions?limit=200&state=${encodeURIComponent(FILTERS[filter])}`, refreshKey);

  const open = async (id: string) => {
    if (selected === id) {
      setSelected(null);
      return;
    }
    setSelected(id);
    setDetail(null);
    setDetail(await api.get(`/api/decisions/${id}`).catch((e: Error) => ({ error: e.message })));
  };

  return (
    <Card
      title="Trade decisions"
      actions={
        <div className="segmented">
          {Object.keys(FILTERS).map((f) => (
            <button key={f} className={f === filter ? "active" : ""} onClick={() => setFilter(f)}>{f}</button>
          ))}
        </div>
      }
    >
      <p className="hint">Every evaluation (each 5-minute close) is journaled, including NO_TRADE and rejections. Click a row for the full audit record.</p>
      <ErrorNote error={error} />
      {data && data.length === 0 && <Empty>No decisions in this view yet. Strategies start evaluating once indicators have warmed up.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Market time</th><th>Instrument</th><th>State</th><th>Regime</th><th>Strategy</th><th>Side</th>
                <th className="num">Score</th><th className="num">Entry</th><th className="num">R:R</th><th>Reasons</th>
              </tr>
            </thead>
            <tbody>
              {data.map((d) => (
                <Fragment key={d.id}>
                  <tr className="clickable" onClick={() => open(d.id)}>
                    <td className="mono muted">{time(d.marketTimeUtc)}</td>
                    <td className="strong">{d.instrument}</td>
                    <td><Badge tone={stateTone(d.state)}>{d.state}</Badge></td>
                    <td><RegimeBadge regime={d.regime} /></td>
                    <td>{d.strategy ?? "—"}</td>
                    <td>{d.direction ?? "—"}</td>
                    <td className="num">{d.score ?? "—"}</td>
                    <td className="num mono">{price(d.entry, d.instrument)}</td>
                    <td className="num">{d.rewardToRisk ?? "—"}</td>
                    <td className="reasons">{d.reasons}</td>
                  </tr>
                  {selected === d.id && (
                    <tr className="detail-row">
                      <td colSpan={10}>
                        {detail ? <pre>{JSON.stringify(detail, null, 2)}</pre> : <span className="muted">Loading…</span>}
                      </td>
                    </tr>
                  )}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
