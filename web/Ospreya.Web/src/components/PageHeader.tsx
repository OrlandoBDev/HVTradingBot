import type { ReactNode } from "react";
import type { PageInfo } from "../pages";

/** Page title, one-line purpose, and an expandable "What this page does". */
export function PageHeader({ page, actions }: { page: PageInfo; actions?: ReactNode }) {
  return (
    <header className="page-header">
      <div className="page-title-row">
        <div>
          <h2 className="page-title"><span className="page-icon">{page.icon}</span>{page.label}</h2>
          <p className="page-summary">{page.summary}</p>
        </div>
        {actions}
      </div>
      <details className="page-help">
        <summary>What this page does</summary>
        <ul>
          {page.details.map((d) => <li key={d}>{d}</li>)}
        </ul>
      </details>
    </header>
  );
}
