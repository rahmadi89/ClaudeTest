import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useAlertAction, useAlerts } from '../api/hooks';
import type { AlertSeverity, AlertView } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Empty, ErrorBox, Loading, Pager } from '../components/Common';
import { Badge, SeverityBadge } from '../components/StatusBadge';
import { dateTime, humanize, timeAgo } from '../lib/format';

export function AlertsPage() {
  return (
    <div>
      <div className="page-head"><h1>Alerts</h1></div>
      <AlertsTable />
    </div>
  );
}

type View = 'active' | 'Open' | 'Acknowledged' | 'Resolved' | 'all';

export function AlertsTable({ atmId }: { atmId?: string }) {
  const [view, setView] = useState<View>('active');
  const [severity, setSeverity] = useState<AlertSeverity | ''>('');
  const [page, setPage] = useState(1);
  const { hasRole } = useAuth();
  const act = useAlertAction();
  const { data, isLoading, error } = useAlerts({
    page, pageSize: 50, atmId, severity,
    active: view === 'active' ? true : undefined,
    status: view === 'Open' || view === 'Acknowledged' || view === 'Resolved' ? view : '',
  });

  const action = (a: AlertView, kind: 'acknowledge' | 'resolve') => {
    const note = kind === 'resolve' ? window.prompt('Resolution note (optional)') ?? undefined : undefined;
    act.mutate({ id: a.id, action: kind, note });
  };

  return (
    <div>
      <div className="toolbar">
        <select value={view} onChange={(e) => { setView(e.target.value as View); setPage(1); }} aria-label="Alert state">
          <option value="active">Active (open + acknowledged)</option>
          <option value="Open">Open</option>
          <option value="Acknowledged">Acknowledged</option>
          <option value="Resolved">Resolved</option>
          <option value="all">All</option>
        </select>
        <select value={severity} onChange={(e) => { setSeverity(e.target.value as AlertSeverity | ''); setPage(1); }} aria-label="Severity">
          <option value="">All severities</option>
          {(['Critical', 'Major', 'Warning', 'Info'] as const).map((s) => <option key={s}>{s}</option>)}
        </select>
      </div>
      <ErrorBox error={error ?? act.error} />
      <div className="card" style={{ padding: 0 }}>
        {isLoading ? <Loading /> : !data?.items.length ? <Empty>No alerts.</Empty> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Severity</th>{!atmId && <th>Terminal</th>}<th>Alert</th><th>Raised</th><th>State</th>{hasRole('Operator') && <th />}</tr></thead>
              <tbody>
                {data.items.map((a) => (
                  <tr key={a.id}>
                    <td><SeverityBadge severity={a.severity} /></td>
                    {!atmId && <td><Link to={`/atms/${a.atmId}?tab=Alerts`}>{a.terminalId}</Link><div className="sub">{a.atmName}</div></td>}
                    <td><strong>{humanize(a.type)}</strong><div>{a.message}</div>{a.note && <div className="sub">Note: {a.note}</div>}</td>
                    <td style={{ whiteSpace: 'nowrap' }} title={dateTime(a.raisedAt)}>{timeAgo(a.raisedAt)}{a.occurrenceCount > 1 && <div className="sub">seen {a.occurrenceCount}×</div>}</td>
                    <td>
                      <Badge tone={a.status === 'Resolved' ? 'good' : a.status === 'Acknowledged' ? 'info' : 'neutral'} label={a.status} />
                      <div className="sub">{a.status === 'Resolved' ? `by ${a.resolvedBy}` : a.acknowledgedBy ? `by ${a.acknowledgedBy}` : ''}</div>
                    </td>
                    {hasRole('Operator') && (
                      <td style={{ whiteSpace: 'nowrap' }}>
                        {a.status === 'Open' && <button className="btn btn-sm" onClick={() => action(a, 'acknowledge')}>Acknowledge</button>}{' '}
                        {a.status !== 'Resolved' && <button className="btn btn-sm" onClick={() => action(a, 'resolve')}>Resolve</button>}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {data && <Pager page={page} pageSize={data.pageSize} total={data.total} onPage={setPage} />}
    </div>
  );
}
