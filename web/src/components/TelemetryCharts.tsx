import { useState } from 'react';
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis, Area, AreaChart } from 'recharts';
import type { TelemetryPoint } from '../api/types';
import { dateTime, money } from '../lib/format';

interface SeriesDef { key: keyof TelemetryPoint; label: string; color: string; unit: string }

const tick = { fill: 'var(--muted)', fontSize: 11 };
const timeFmt = (v: number) => new Date(v).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });

interface TipProps {
  active?: boolean;
  label?: number;
  payload?: Array<{ dataKey?: string | number; value?: number | null; color?: string }>;
  series: SeriesDef[];
  format?: (v: number) => string;
}

function ChartTooltip({ active, label, payload, series, format }: TipProps) {
  if (!active || !payload?.length || label === undefined) return null;
  return (
    <div className="chart-tip">
      <div style={{ color: 'var(--text-2)', marginBottom: 4 }}>{dateTime(new Date(label).toISOString())}</div>
      {payload.map((p) => {
        const s = series.find((x) => x.key === p.dataKey);
        if (!s || p.value === null || p.value === undefined) return null;
        return (
          <div className="row" key={String(p.dataKey)}>
            <span><span className="sw" style={{ background: s.color }} />{s.label}</span>
            <strong>{format ? format(p.value) : `${p.value.toFixed(1)}${s.unit}`}</strong>
          </div>
        );
      })}
    </div>
  );
}

function Legend({ series }: { series: SeriesDef[] }) {
  return (
    <div className="legend" aria-hidden="true">
      {series.map((s) => <span key={s.key}><span className="sw" style={{ background: s.color }} />{s.label}</span>)}
    </div>
  );
}

const resources: SeriesDef[] = [
  { key: 'cpuPercent', label: 'CPU', color: 'var(--series-1)', unit: '%' },
  { key: 'memoryUsedPercent', label: 'Memory', color: 'var(--series-2)', unit: '%' },
  { key: 'diskUsedPercent', label: 'Disk', color: 'var(--series-3)', unit: '%' },
];
const latency: SeriesDef[] = [{ key: 'hostLatencyMs', label: 'Host latency', color: 'var(--series-1)', unit: ' ms' }];
const cash: SeriesDef[] = [{ key: 'availableCash', label: 'Available cash', color: 'var(--series-1)', unit: '' }];

/**
 * One measure per chart (no dual axes): resources share a % scale; latency and cash each get their own.
 * A table view is available for exact values and as the accessibility fallback.
 */
export function TelemetryCharts({ data }: { data: TelemetryPoint[] }) {
  const [asTable, setAsTable] = useState(false);
  const points = data.map((d) => ({ ...d, t: new Date(d.timestamp).getTime() }));

  if (points.length === 0) {
    return <div className="empty">No telemetry in this range yet.</div>;
  }

  return (
    <div className="stack">
      <div className="toolbar" style={{ justifyContent: 'flex-end', marginBottom: 0 }}>
        <button className="btn btn-sm" onClick={() => setAsTable((v) => !v)} aria-pressed={asTable}>
          {asTable ? 'Show charts' : 'Show table'}
        </button>
      </div>
      {asTable ? (
        <div className="table-wrap card" style={{ maxHeight: 480, overflow: 'auto' }}>
          <table>
            <thead><tr><th>Time</th><th className="num">CPU %</th><th className="num">Memory %</th><th className="num">Disk %</th><th className="num">Latency ms</th><th className="num">Loss %</th><th className="num">Cash</th></tr></thead>
            <tbody>
              {[...data].reverse().map((d) => (
                <tr key={d.timestamp}>
                  <td>{dateTime(d.timestamp)}</td>
                  <td className="num">{d.cpuPercent?.toFixed(1) ?? '—'}</td>
                  <td className="num">{d.memoryUsedPercent?.toFixed(1) ?? '—'}</td>
                  <td className="num">{d.diskUsedPercent?.toFixed(1) ?? '—'}</td>
                  <td className="num">{d.hostLatencyMs?.toFixed(1) ?? '—'}</td>
                  <td className="num">{d.packetLossPercent?.toFixed(1) ?? '—'}</td>
                  <td className="num">{d.availableCash !== null ? money(d.availableCash) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="grid grid-2">
          <div className="card">
            <h2>Resource utilization</h2>
            <Legend series={resources} />
            <ResponsiveContainer width="100%" height={220}>
              <LineChart data={points} margin={{ top: 8, right: 8, bottom: 0, left: -12 }}>
                <CartesianGrid stroke="var(--grid)" vertical={false} />
                <XAxis dataKey="t" type="number" domain={['dataMin', 'dataMax']} scale="time" tickFormatter={timeFmt} tick={tick} stroke="var(--axis)" />
                <YAxis domain={[0, 100]} tick={tick} stroke="var(--axis)" unit="%" />
                <Tooltip content={<ChartTooltip series={resources} />} cursor={{ stroke: 'var(--axis)' }} />
                {resources.map((s) => (
                  <Line key={s.key} dataKey={s.key} name={s.label} stroke={s.color} strokeWidth={2} dot={false} isAnimationActive={false} connectNulls />
                ))}
              </LineChart>
            </ResponsiveContainer>
          </div>
          <div className="card">
            <h2>Host latency (ms)</h2>
            <ResponsiveContainer width="100%" height={242}>
              <LineChart data={points} margin={{ top: 8, right: 8, bottom: 0, left: -12 }}>
                <CartesianGrid stroke="var(--grid)" vertical={false} />
                <XAxis dataKey="t" type="number" domain={['dataMin', 'dataMax']} scale="time" tickFormatter={timeFmt} tick={tick} stroke="var(--axis)" />
                <YAxis tick={tick} stroke="var(--axis)" />
                <Tooltip content={<ChartTooltip series={latency} />} cursor={{ stroke: 'var(--axis)' }} />
                <Line dataKey="hostLatencyMs" stroke="var(--series-1)" strokeWidth={2} dot={false} isAnimationActive={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
          <div className="card" style={{ gridColumn: '1 / -1' }}>
            <h2>Available cash</h2>
            <ResponsiveContainer width="100%" height={220}>
              <AreaChart data={points} margin={{ top: 8, right: 8, bottom: 0, left: 8 }}>
                <CartesianGrid stroke="var(--grid)" vertical={false} />
                <XAxis dataKey="t" type="number" domain={['dataMin', 'dataMax']} scale="time" tickFormatter={timeFmt} tick={tick} stroke="var(--axis)" />
                <YAxis tick={tick} stroke="var(--axis)" tickFormatter={(v: number) => money(v, 'USD', true)} />
                <Tooltip content={<ChartTooltip series={cash} format={(v) => money(v)} />} cursor={{ stroke: 'var(--axis)' }} />
                <Area dataKey="availableCash" stroke="var(--series-1)" strokeWidth={2} fill="var(--series-1)" fillOpacity={0.12} isAnimationActive={false} />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>
      )}
    </div>
  );
}
