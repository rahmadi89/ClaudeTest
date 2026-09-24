import { Link } from 'react-router-dom';
import { useDashboard } from '../api/hooks';
import type { AtmStatus } from '../api/types';
import { Empty, ErrorBox, Loading } from '../components/Common';
import { Badge, SeverityBadge, atmTone } from '../components/StatusBadge';
import { humanize, money, timeAgo } from '../lib/format';

const statusOrder: AtmStatus[] = ['Online', 'Degraded', 'OutOfService', 'Offline', 'Maintenance', 'Unknown'];

export function DashboardPage() {
  const { data, isLoading, error } = useDashboard();
  if (isLoading) return <Loading />;
  if (error || !data) return <ErrorBox error={error} />;

  const online = data.byStatus.Online ?? 0;
  const availability = data.totalAtms ? (100 * online) / data.totalAtms : 0;
  const alerts = data.openAlertsBySeverity;
  const openAlerts = Object.values(alerts).reduce((a, b) => a + (b ?? 0), 0);

  return (
    <div className="stack">
      <div className="page-head"><h1>Fleet overview</h1></div>

      <div className="grid grid-kpi">
        <div className="card">
          <div className="kpi-label">Terminals online</div>
          <div className="kpi-value">{online} <span className="sub">/ {data.totalAtms}</span></div>
          <div className="kpi-foot">{availability.toFixed(1)}% fully available</div>
        </div>
        <div className="card">
          <div className="kpi-label">Open alerts</div>
          <div className="kpi-value">{openAlerts}</div>
          <div className="kpi-foot">{alerts.Critical ?? 0} critical · {alerts.Major ?? 0} major · {alerts.Warning ?? 0} warning</div>
        </div>
        <div className="card">
          <div className="kpi-label">Cash in fleet</div>
          <div className="kpi-value">
            {Object.entries(data.cashByCurrency).map(([cur, amt]) => <div key={cur}>{money(amt, cur, true)}</div>)}
            {Object.keys(data.cashByCurrency).length === 0 && '—'}
          </div>
          <div className="kpi-foot">{data.atmsWithLowCash} terminal(s) low or empty</div>
        </div>
        <div className="card">
          <div className="kpi-label">Commands in flight</div>
          <div className="kpi-value">{data.pendingCommands}</div>
          <div className="kpi-foot"><Link to="/commands">View commands</Link></div>
        </div>
      </div>

      <div className="grid grid-2">
        <div className="card">
          <h2>Terminals by status</h2>
          <table>
            <tbody>
              {statusOrder.filter((s) => (data.byStatus[s] ?? 0) > 0 || s === 'Online').map((s) => {
                const n = data.byStatus[s] ?? 0;
                const pct = data.totalAtms ? (100 * n) / data.totalAtms : 0;
                return (
                  <tr key={s}>
                    <td style={{ width: 140 }}><Link to={`/atms?status=${s}`}><Badge tone={atmTone(s)} label={humanize(s)} /></Link></td>
                    <td><div className="bar" aria-hidden="true"><span style={{ width: `${pct}%`, background: `var(--${atmTone(s)})` }} /></div></td>
                    <td className="num" style={{ width: 90 }}>{n} <span className="sub">({pct.toFixed(0)}%)</span></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <div className="card">
          <h2>Latest open alerts</h2>
          {data.recentAlerts.length === 0 ? <Empty>No open alerts. All clear.</Empty> : (
            <table>
              <tbody>
                {data.recentAlerts.map((a) => (
                  <tr key={a.id}>
                    <td style={{ width: 100 }}><SeverityBadge severity={a.severity} /></td>
                    <td><Link to={`/atms/${a.atmId}`}>{a.terminalId}</Link><div className="sub">{a.message}</div></td>
                    <td className="sub" style={{ whiteSpace: 'nowrap' }}>{timeAgo(a.raisedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <div style={{ marginTop: 8 }}><Link to="/alerts">All alerts →</Link></div>
        </div>
      </div>
    </div>
  );
}
