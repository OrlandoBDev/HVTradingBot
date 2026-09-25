export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

const SIZES = [25, 50, 100];

/** "Showing 51–100 of 12,345" with first/previous/next/last and a page-size choice. */
export function Pagination({
  page,
  pageSize,
  total,
  onPage,
  onPageSize,
}: {
  page: number;
  pageSize: number;
  total: number;
  onPage: (page: number) => void;
  onPageSize: (size: number) => void;
}) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const to = Math.min(total, page * pageSize);
  return (
    <div className="pagination">
      <span className="muted small">
        {total === 0 ? "No rows" : `Showing ${from.toLocaleString()}–${to.toLocaleString()} of ${total.toLocaleString()}`}
      </span>
      <div className="pagination-controls">
        <label className="muted small">
          Rows{" "}
          <select value={pageSize} onChange={(e) => onPageSize(Number(e.target.value))}>
            {SIZES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </label>
        <button className="small-button" onClick={() => onPage(1)} disabled={page <= 1} aria-label="First page">«</button>
        <button className="small-button" onClick={() => onPage(page - 1)} disabled={page <= 1} aria-label="Previous page">‹</button>
        <span className="small">Page {page.toLocaleString()} of {pages.toLocaleString()}</span>
        <button className="small-button" onClick={() => onPage(page + 1)} disabled={page >= pages} aria-label="Next page">›</button>
        <button className="small-button" onClick={() => onPage(pages)} disabled={page >= pages} aria-label="Last page">»</button>
      </div>
    </div>
  );
}
