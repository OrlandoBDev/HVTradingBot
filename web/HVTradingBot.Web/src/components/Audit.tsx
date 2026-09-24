import type { AuditEntry } from "../types";
import { time } from "../format";
import { useData } from "../useData";
import { Card, Empty, ErrorNote } from "./Ui";

export function Audit({ refreshKey }: { refreshKey: unknown }) {
  const { data, error } = useData<AuditEntry[]>("/api/audit?limit=200", refreshKey);
  return (
    <Card title="Audit log">
      <ErrorNote error={error} />
      {data && data.length === 0 && <Empty>No audit events yet.</Empty>}
      {data && data.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Time (UTC)</th><th>Actor</th><th>Action</th><th>Details</th></tr></thead>
            <tbody>
              {data.map((a) => (
                <tr key={a.id}>
                  <td className="mono muted">{time(a.timestampUtc)}</td>
                  <td>{a.actor}</td>
                  <td className="strong">{a.action}</td>
                  <td className="reasons">{a.details}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}
