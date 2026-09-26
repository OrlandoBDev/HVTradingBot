import type { SystemStatus } from "../types";
import { money, signClass } from "../format";

/**
 * Always-visible account summary pinned to the bottom of every page: balance, equity and open P/L of the whole broker
 * account, live from the status push. Tapping it opens the open positions.
 */
export function AccountBar({ status, onOpen }: { status: SystemStatus; onOpen: () => void }) {
  const a = status.account;
  return (
    <button className="account-bar" onClick={onOpen} title="Open positions">
      <span className="account-item">
        <span className="account-label">Balance</span>
        <span className="account-value">{money(a.balance, a.currency)}</span>
      </span>
      <span className="account-item">
        <span className="account-label">Equity</span>
        <span className="account-value">{money(a.equity, a.currency)}</span>
      </span>
      <span className="account-item">
        <span className="account-label">Open P/L · {status.openPositions} open</span>
        <span className={`account-value ${signClass(a.unrealizedPnl)}`}>{money(a.unrealizedPnl, a.currency)}</span>
      </span>
    </button>
  );
}
