import { useEffect, useState } from "react";
import type { Position } from "../types";
import { api } from "../api";
import type { PageId } from "../pages";
import { money, num, price, signClass, time } from "../format";
import { useData } from "../useData";
import { Badge, Card, Empty, ErrorNote, MarketName, Segmented } from "./Ui";
import { useMarketNames } from "../useMarketNames";
import { TestTrade } from "./TestTrade";
import { Pagination, type Paged } from "./Pagination";

type Navigate = (page: PageId, section?: string) => void;

/** Buy/Sell for multiplier contracts; other broker contract types by name. */
const side = (p: Position) => (p.direction === "Long" ? "Buy" : p.direction === "Short" ? "Sell" : p.contractType ?? p.direction);
const external = (p: Position) => p.source === "External";

/** Trades this app placed vs. contracts opened elsewhere on the broker account (Deriv's website, another app). */
function SourceBadge({ p }: { p: Position }) {
  return external(p)
    ? <span title="Opened outside this app on the same broker account"><Badge tone="neutral">External</Badge></span>
    : <span title="Placed by this app"><Badge tone="info">App</Badge></span>;
}

/** Trades page: open positions and closed-trade history. */
export function Trades({ refreshKey, view, navigate }: { refreshKey: unknown; view?: string; navigate: Navigate }) {
  const current = view === "history" ? "history" : "open";
  return (
    <div className="stack">
      <TestTrade />
      <Segmented
        value={current}
        options={[{ value: "open", label: "Open positions" }, { value: "history", label: "Trade history" }]}
        onChange={(v) => navigate("trades", v === "open" ? undefined : v)}
      />
      {current === "open" ? <OpenPositions refreshKey={refreshKey} /> : <TradeHistory refreshKey={refreshKey} />}
    </div>
  );
}

export function OpenPositions({ refreshKey, compact = false, onMore }: { refreshKey: unknown; compact?: boolean; onMore?: () => void }) {
  const marketName = useMarketNames();
  const [tick, setTick] = useState(0);
  const [closeError, setCloseError] = useState<string | null>(null);
  const { data, error } = useData<Position[]>("/api/positions/open", `${refreshKey}|${tick}`);

  // While a close is in progress, refresh every 2 seconds until it is recorded.
  const closing = data?.some((p) => p.closeStatus === "Pending" || p.closeStatus === "Closing") ?? false;
  useEffect(() => {
    if (!closing) return;
    const id = window.setInterval(() => setTick((t) => t + 1), 2000);
    return () => window.clearInterval(id);
  }, [closing]);

  const close = async (p: Position) => {
    if (external(p)) return;
    const pnl = p.unrealizedPnl == null ? "" : `\nOpen P&L now: ${money(p.unrealizedPnl)}.`;
    if (!window.confirm(`Close ${side(p).toUpperCase()} ${marketName(p.instrument)} now at the market price?${pnl}\n\nThis sells the position before its stop loss or take profit.`)) return;
    setCloseError(null);
    try {
      await api.post(`/api/positions/${p.id}/close`, {});
      setTick((t) => t + 1);
    } catch (e) {
      setCloseError((e as Error).message);
    }
  };
  return (
    <Card title={compact ? "Open positions" : undefined} actions={onMore && <button className="link" onClick={onMore}>All trades →</button>}>
      <ErrorNote error={error} />
      <ErrorNote error={closeError} />
      {data && data.length === 0 && <Empty>No open positions on the account. The engine trades only when a setup scores 75+ and every risk rule passes.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Market</th><th>Side</th><th>Source</th>
                {!compact && <th className="num" title="Units for app trades, stake for external contracts">Size</th>}
                <th className="num">Entry</th><th className="num">Stop</th><th className="num">Target</th>
                {!compact && <th className="num">Current</th>}
                <th className="num">Open P&L</th>
                {!compact && <th className="num" title="Money at risk to the stop (app trades) or stake paid (external)">Risk / stake</th>}
                <th>Strategy</th>
                {!compact && <th className="num">Score</th>}
                {!compact && <th>Opened</th>}
                <th />
              </tr>
            </thead>
            <tbody>
              {data.map((p) => (
                <tr key={p.contractId ?? p.id}>
                  <td><MarketName symbol={p.instrument} /></td>
                  <td><Badge tone={p.direction === "Long" ? "good" : p.direction === "Short" ? "bad" : "neutral"}>{side(p)}</Badge></td>
                  <td><SourceBadge p={p} /></td>
                  {!compact && <td className="num">{external(p) ? money(p.stake) : num(p.units, p.units < 10 ? 4 : 0)}</td>}
                  <td className="num mono">{price(p.entryPrice, p.instrument)}</td>
                  <td className="num mono">{price(p.stopLoss, p.instrument)}</td>
                  <td className="num mono">{price(p.takeProfit, p.instrument)}</td>
                  {!compact && <td className="num mono">{price(p.currentPrice, p.instrument)}</td>}
                  <td className={`num ${signClass(p.unrealizedPnl)}`}>{money(p.unrealizedPnl)}</td>
                  {!compact && <td className="num">{money(external(p) ? p.stake : p.initialRiskAmount)}</td>}
                  <td>{external(p) ? <span className="muted">{p.contractType ?? "—"}</span> : p.strategy}</td>
                  {!compact && <td className="num">{external(p) ? "—" : p.score}</td>}
                  {!compact && <td className="mono muted">{time(p.openedAtUtc)}</td>}
                  <td className="actions">
                    {p.closeStatus === "Pending" || p.closeStatus === "Closing" ? (
                      <span title={p.closeMessage ?? ""}><Badge tone="warn">closing…</Badge></span>
                    ) : (
                      <>
                        {p.closeStatus === "Failed" && <span title={p.closeMessage ?? ""}><Badge tone="bad">close failed</Badge> </span>}
                        {!external(p) && (
                          <button className="small-button" onClick={() => close(p)} title="Close this position now at the market price">
                            Close
                          </button>
                        )}
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

export function TradeHistory({ refreshKey }: { refreshKey: unknown }) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const { data: paged, error } = useData<Paged<Position>>(`/api/trades/paged?page=${page}&pageSize=${pageSize}`, refreshKey);
  const data = paged?.items;
  return (
    <Card>
      <ErrorNote error={error} />
      {data && data.length === 0 && <Empty>No closed trades on the account yet.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Closed</th><th>Market</th><th>Side</th><th>Source</th><th>Strategy</th><th className="num">Entry</th><th className="num">Exit</th>
                <th>Exit</th><th className="num">P&L</th>
                <th className="num" title="Commission the broker charged for this trade. It is already taken out of the P&L.">Broker fee</th>
                <th className="num">R</th><th className="num">MAE / MFE</th>
              </tr>
            </thead>
            <tbody>
              {data.map((p) => (
                <tr key={p.contractId ?? p.id}>
                  <td className="mono muted">{time(p.closedAtUtc)}</td>
                  <td><MarketName symbol={p.instrument} /></td>
                  <td>{side(p)}</td>
                  <td><SourceBadge p={p} /></td>
                  <td>{external(p) ? <span className="muted">{p.contractType ?? "—"}</span> : p.strategy}</td>
                  <td className="num mono">{price(p.entryPrice, p.instrument)}</td>
                  <td className="num mono">{price(p.exitPrice, p.instrument)}</td>
                  <td><Badge tone={p.exitReason === "TakeProfit" ? "good" : p.exitReason === "StopLoss" ? "bad" : "neutral"}>{p.exitReason}</Badge></td>
                  <td className={`num ${signClass(p.realizedPnl)}`}>{money(p.realizedPnl)}</td>
                  <td className="num muted">{p.commission == null ? "—" : money(p.commission)}</td>
                  <td className={`num ${signClass(p.rMultiple)}`}>{external(p) ? "—" : num(p.rMultiple)}</td>
                  <td className="num">{external(p) ? "—" : `${num(p.maePips, 1)} / ${num(p.mfePips, 1)}`}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {paged && paged.total > 0 && (
        <Pagination page={paged.page} pageSize={paged.pageSize} total={paged.total} onPage={setPage}
          onPageSize={(size) => { setPageSize(size); setPage(1); }} />
      )}
    </Card>
  );
}
