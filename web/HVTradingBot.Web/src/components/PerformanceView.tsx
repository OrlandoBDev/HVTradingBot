import type { Performance } from "../types";
import { useData } from "../useData";
import { Card, ErrorNote } from "./Ui";
import { MetricsByGroup, MetricsSummary } from "./MetricsTable";

export function PerformanceView({ refreshKey }: { refreshKey: unknown }) {
  const { data, error } = useData<Performance>("/api/performance", refreshKey);
  return (
    <>
      <ErrorNote error={error} />
      {data && (
        <>
          <div className="two-col">
            <Card title="Paper trading performance"><MetricsSummary m={data.overall} /></Card>
            <Card title="Decisions by outcome">
              <dl className="kv">
                {Object.entries(data.decisionCounts).sort((a, b) => b[1] - a[1]).map(([k, v]) => (
                  <div key={k} className="kv-row"><dt>{k}</dt><dd>{v.toLocaleString()}</dd></div>
                ))}
              </dl>
            </Card>
          </div>
          <Card title="By strategy"><MetricsByGroup groups={data.byStrategy} label="Strategy" /></Card>
          <Card title="By market"><MetricsByGroup groups={data.byInstrument} label="Market" markets /></Card>
        </>
      )}
    </>
  );
}
