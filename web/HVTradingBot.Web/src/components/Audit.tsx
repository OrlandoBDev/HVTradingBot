import { useState } from "react";
import type { AuditEntry } from "../types";
import { Pagination, type Paged } from "./Pagination";
import { time } from "../format";
import { useData } from "../useData";
import { Card, Empty, ErrorNote } from "./Ui";

export function Audit({ refreshKey }: { refreshKey: unknown }) {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const { data: paged, error } = useData<Paged<AuditEntry>>(`/api/audit/paged?page=${page}&pageSize=${pageSize}`, refreshKey);
  const data = paged?.items;
  return (
    <Card>
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
      {paged && paged.total > 0 && (
        <Pagination page={paged.page} pageSize={paged.pageSize} total={paged.total} onPage={setPage}
          onPageSize={(size) => { setPageSize(size); setPage(1); }} />
      )}
    </Card>
  );
}
