import { Fragment, useState } from 'react';
import { Link } from 'react-router-dom';
import { useCancelCommand, useCommands } from '../api/hooks';
import type { CommandStatus } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Empty, ErrorBox, Loading, Pager } from '../components/Common';
import { CommandStatusBadge } from '../components/StatusBadge';
import { dateTime, humanize, timeAgo } from '../lib/format';

const statuses: CommandStatus[] = ['Pending', 'Sent', 'Acknowledged', 'Running', 'Succeeded', 'Failed', 'TimedOut', 'Cancelled', 'Rejected'];

export function CommandsPage() {
  return (
    <div>
      <div className="page-head"><h1>Commands</h1><span className="sub">Every remote action, who requested it, and what the terminal answered.</span></div>
      <CommandsTable />
    </div>
  );
}

export function CommandsTable({ atmId }: { atmId?: string }) {
  const [status, setStatus] = useState<CommandStatus | ''>('');
  const [page, setPage] = useState(1);
  const [expanded, setExpanded] = useState<string | null>(null);
  const { hasRole } = useAuth();
  const cancel = useCancelCommand();
  const { data, isLoading, error } = useCommands({ page, pageSize: 50, status, atmId });

  return (
    <div>
      <div className="toolbar">
        <select value={status} onChange={(e) => { setStatus(e.target.value as CommandStatus | ''); setPage(1); }} aria-label="Status">
          <option value="">All statuses</option>
          {statuses.map((s) => <option key={s} value={s}>{humanize(s)}</option>)}
        </select>
      </div>
      <ErrorBox error={error ?? cancel.error} />
      <div className="card" style={{ padding: 0 }}>
        {isLoading ? <Loading /> : !data?.items.length ? <Empty>No commands yet.</Empty> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Command</th>{!atmId && <th>Terminal</th>}<th>Status</th><th>Requested</th><th>Completed</th><th /></tr></thead>
              <tbody>
                {data.items.map((c) => (
                  <Fragment key={c.id}>
                    <tr>
                      <td>
                        <strong>{humanize(c.type)}</strong>
                        {Object.keys(c.parameters).length > 0 && <div className="sub mono">{Object.entries(c.parameters).map(([k, v]) => `${k}=${v}`).join(' ')}</div>}
                        {c.reason && <div className="sub">“{c.reason}”</div>}
                      </td>
                      {!atmId && <td><Link to={`/atms/${c.atmId}?tab=Commands`}>{c.terminalId}</Link></td>}
                      <td><CommandStatusBadge status={c.status} /></td>
                      <td title={dateTime(c.createdAt)}>{timeAgo(c.createdAt)}<div className="sub">by {c.requestedBy}</div></td>
                      <td>{c.completedAt ? dateTime(c.completedAt) : '—'}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        {(c.output || c.error) && (
                          <button className="btn btn-sm" aria-expanded={expanded === c.id} onClick={() => setExpanded(expanded === c.id ? null : c.id)}>
                            {expanded === c.id ? 'Hide' : 'Output'}
                          </button>
                        )}{' '}
                        {hasRole('Operator') && (c.status === 'Pending' || c.status === 'Sent') && (
                          <button className="btn btn-sm btn-danger" onClick={() => cancel.mutate(c.id)}>Cancel</button>
                        )}
                      </td>
                    </tr>
                    {expanded === c.id && (
                      <tr>
                        <td colSpan={atmId ? 5 : 6}>
                          {c.error && <pre className="output" style={{ color: 'var(--danger-ink)' }}>{c.error}</pre>}
                          {c.output && <pre className="output">{c.output}</pre>}
                        </td>
                      </tr>
                    )}
                  </Fragment>
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
