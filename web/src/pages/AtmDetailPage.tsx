import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useAtm, useAtmAdminAction, useAtmLogs, useTelemetry } from '../api/hooks';
import type { AtmDetail, LogSeverity } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { CommandDialog } from '../components/CommandDialog';
import { Empty, ErrorBox, Loading, Pager } from '../components/Common';
import { AtmStatusBadge, Badge, cassetteTone, componentTone } from '../components/StatusBadge';
import { TelemetryCharts } from '../components/TelemetryCharts';
import { bytes, dateTime, duration, humanize, money, percent, timeAgo } from '../lib/format';
import { AlertsTable } from './AlertsPage';
import { CommandsTable } from './CommandsPage';
import { AtmFormDialog } from './AtmListPage';

const tabs = ['Overview', 'Cash', 'Telemetry', 'Logs', 'Commands', 'Alerts'] as const;
type Tab = (typeof tabs)[number];

export function AtmDetailPage() {
  const { id = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const tab = (tabs.includes(params.get('tab') as Tab) ? params.get('tab') : 'Overview') as Tab;
  const { data: atm, isLoading, error } = useAtm(id);
  const { hasRole } = useAuth();
  const [commandOpen, setCommandOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);

  if (isLoading) return <Loading />;
  if (error || !atm) return <ErrorBox error={error ?? new Error('Terminal not found')} />;

  return (
    <div>
      <div className="page-head">
        <div>
          <div className="sub"><Link to="/atms">Terminals</Link> /</div>
          <h1>{atm.terminalId} <span className="sub" style={{ fontWeight: 400 }}>{atm.name !== atm.terminalId && atm.name}</span></h1>
        </div>
        <AtmStatusBadge status={atm.status} />
        <span className="pill">{humanize(atm.mode)}</span>
        {!atm.isEnabled && <span className="pill">Disabled</span>}
        <span className="spacer" />
        <span className="sub" title={dateTime(atm.lastSeenAt)}>Last seen {timeAgo(atm.lastSeenAt)}</span>
        {hasRole('Operator') && (
          <button className="btn btn-primary" disabled={!atm.isEnrolled || !atm.isEnabled} onClick={() => setCommandOpen(true)}>Send command</button>
        )}
        {hasRole('Admin') && <button className="btn" onClick={() => setEditOpen(true)}>Edit</button>}
      </div>

      <div className="tabs" role="tablist">
        {tabs.map((t) => (
          <button key={t} role="tab" className="tab" aria-selected={t === tab} onClick={() => setParams({ tab: t }, { replace: true })}>{t}</button>
        ))}
      </div>

      {tab === 'Overview' && <Overview atm={atm} />}
      {tab === 'Cash' && <Cash atm={atm} />}
      {tab === 'Telemetry' && <Telemetry id={atm.id} />}
      {tab === 'Logs' && <Logs id={atm.id} />}
      {tab === 'Commands' && <CommandsTable atmId={atm.id} />}
      {tab === 'Alerts' && <AlertsTable atmId={atm.id} />}

      <CommandDialog atmId={atm.id} terminalId={atm.terminalId} open={commandOpen} onClose={() => setCommandOpen(false)} />
      {editOpen && (
        <AtmFormDialog open id={atm.id} onClose={() => setEditOpen(false)}
          initial={{ terminalId: atm.terminalId, name: atm.name, branch: atm.branch, address: atm.address, city: atm.city, latitude: atm.latitude,
            longitude: atm.longitude, vendor: atm.vendor, model: atm.model, serialNumber: atm.serialNumber }} />
      )}
    </div>
  );
}

function Overview({ atm }: { atm: AtmDetail }) {
  const { hasRole } = useAuth();
  const n = atm.network;
  const s = atm.system;
  return (
    <div className="grid grid-2">
      <div className="card">
        <h2>Devices</h2>
        {atm.components.length === 0 ? <Empty>No device data reported yet.</Empty> : (
          <table>
            <tbody>
              {atm.components.map((c) => (
                <tr key={c.type}>
                  <td style={{ whiteSpace: 'nowrap', width: '40%' }}>{humanize(c.type)}</td>
                  <td>
                    <Badge tone={componentTone(c.state)} label={c.state} />
                    {c.state !== 'Ok' && (c.errorCode || c.description) && (
                      <div className="sub" style={{ marginTop: 4 }}>
                        {c.errorCode && <code>{c.errorCode}</code>} {c.description} · since {timeAgo(c.stateChangedAt)}
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
      <div className="stack">
        <div className="card">
          <h2>Network</h2>
          {!n ? <Empty>No network data.</Empty> : (
            <dl className="kv">
              <dt>Transaction host</dt><dd><Badge tone={n.hostReachable ? 'good' : 'critical'} label={n.hostReachable ? 'Reachable' : 'Unreachable'} /></dd>
              <dt>Latency</dt><dd>{n.hostLatencyMs !== null ? `${n.hostLatencyMs} ms` : '—'}</dd>
              <dt>Packet loss</dt><dd>{percent(n.packetLossPercent, 1)}</dd>
              <dt>Interface</dt><dd>{n.interfaceName ?? '—'} ({n.interfaceUp ? 'up' : 'down'}){n.linkSpeedMbps ? `, ${n.linkSpeedMbps} Mbit/s` : ''}</dd>
              <dt>IP address</dt><dd className="mono">{n.localIpAddress ?? '—'}</dd>
            </dl>
          )}
        </div>
        <div className="card">
          <h2>System</h2>
          {!s ? <Empty>No system data.</Empty> : (
            <dl className="kv">
              <dt>CPU</dt><dd>{percent(s.cpuPercent, 1)}</dd>
              <dt>Memory</dt><dd>{percent(s.memoryUsedPercent, 1)}</dd>
              <dt>Disk</dt><dd>{percent(s.diskUsedPercent, 1)} used, {bytes(s.diskFreeBytes)} free</dd>
              <dt>Uptime</dt><dd>{duration(s.uptimeSeconds)}</dd>
              <dt>OS</dt><dd>{s.osDescription}</dd>
            </dl>
          )}
        </div>
      </div>
      <div className="card">
        <h2>Terminal</h2>
        <dl className="kv">
          <dt>Branch</dt><dd>{atm.branch ?? '—'}</dd>
          <dt>Address</dt><dd>{[atm.address, atm.city].filter(Boolean).join(', ') || '—'}</dd>
          <dt>Hardware</dt><dd>{[atm.vendor, atm.model].filter(Boolean).join(' ') || '—'}{atm.serialNumber && ` · S/N ${atm.serialNumber}`}</dd>
          <dt>Machine</dt><dd>{atm.machineName ?? '—'}</dd>
          <dt>Agent</dt><dd>{atm.agentVersion ?? '—'} · {atm.isConnected ? 'connected' : 'disconnected'}</dd>
          <dt>Enrolled</dt><dd>{atm.isEnrolled ? dateTime(atm.enrolledAt) : 'Not enrolled'}</dd>
          <dt>Status since</dt><dd>{dateTime(atm.statusChangedAt)}</dd>
        </dl>
      </div>
      {hasRole('Admin') && <AdminActions atm={atm} />}
    </div>
  );
}

function AdminActions({ atm }: { atm: AtmDetail }) {
  const action = useAtmAdminAction();
  const navigate = useNavigate();
  const run = async (a: 'enable' | 'disable' | 'revoke-agent' | 'delete', confirmText: string) => {
    if (!window.confirm(confirmText)) return;
    await action.mutateAsync({ id: atm.id, action: a });
    if (a === 'delete') navigate('/atms');
  };
  return (
    <div className="card">
      <h2>Administration</h2>
      <div className="toolbar">
        {atm.isEnabled
          ? <button className="btn" onClick={() => run('disable', `Disable ${atm.terminalId}? Its agent will be disconnected and rejected.`)}>Disable terminal</button>
          : <button className="btn" onClick={() => run('enable', `Enable ${atm.terminalId}?`)}>Enable terminal</button>}
        <button className="btn btn-danger" disabled={!atm.isEnrolled} onClick={() => run('revoke-agent', `Revoke the agent key for ${atm.terminalId}? The terminal must re-enroll.`)}>Revoke agent key</button>
        <button className="btn btn-danger" onClick={() => run('delete', `Permanently delete ${atm.terminalId} and all its history?`)}>Delete</button>
      </div>
      <ErrorBox error={action.error} />
    </div>
  );
}

function Cash({ atm }: { atm: AtmDetail }) {
  if (atm.cassettes.length === 0) return <Empty>No cassette data reported.</Empty>;
  return (
    <div className="grid grid-2">
      <div className="card">
        <h2>Cassettes</h2>
        {atm.cassettes.map((c) => (
          <div className="cassette" key={c.cassetteId}>
            <div><strong>{c.cassetteId}</strong><div className="sub">{c.type}</div></div>
            <div>
              <div className="bar" role="meter" aria-valuenow={c.fillPercent} aria-valuemin={0} aria-valuemax={100} aria-label={`${c.cassetteId} fill`}>
                <span style={{ width: `${Math.min(100, c.fillPercent)}%` }} />
              </div>
              <div className="sub" style={{ marginTop: 4 }}>
                {c.count.toLocaleString()} / {c.capacity.toLocaleString()} notes ({c.fillPercent}%)
                {c.denomination > 0 && ` · ${money(c.denomination, c.currency)} notes · ${money(c.value, c.currency)}`}
              </div>
            </div>
            <Badge tone={cassetteTone(c.status)} label={c.status} />
          </div>
        ))}
      </div>
      <div className="card">
        <h2>Available cash</h2>
        {Object.entries(atm.availableCash).map(([cur, amount]) => (
          <div key={cur} className="kpi-value">{money(amount, cur)}</div>
        ))}
        <div className="kpi-foot">Dispense and recycle cassettes only. Reject/retract bins excluded.</div>
      </div>
    </div>
  );
}

function Telemetry({ id }: { id: string }) {
  const [hours, setHours] = useState(6);
  const { data, isLoading, error } = useTelemetry(id, hours);
  return (
    <div>
      <div className="toolbar">
        <span className="sub">Range</span>
        {[1, 6, 24, 24 * 7].map((h) => (
          <button key={h} className="btn btn-sm" aria-pressed={h === hours} style={h === hours ? { borderColor: 'var(--accent)' } : undefined}
            onClick={() => setHours(h)}>{h < 24 ? `${h}h` : `${h / 24}d`}</button>
        ))}
      </div>
      <ErrorBox error={error} />
      {isLoading ? <Loading /> : <TelemetryCharts data={data ?? []} />}
    </div>
  );
}

const severities: LogSeverity[] = ['Information', 'Warning', 'Error', 'Critical'];

function Logs({ id }: { id: string }) {
  const [page, setPage] = useState(1);
  const [minSeverity, setMinSeverity] = useState<LogSeverity | ''>('');
  const [search, setSearch] = useState('');
  const { data, isLoading, error } = useAtmLogs(id, { page, pageSize: 100, minSeverity, search });
  return (
    <div>
      <div className="toolbar">
        <select value={minSeverity} onChange={(e) => { setMinSeverity(e.target.value as LogSeverity | ''); setPage(1); }} aria-label="Minimum severity">
          <option value="">All severities</option>
          {severities.map((s) => <option key={s} value={s}>{s} and above</option>)}
        </select>
        <input type="search" placeholder="Search messages…" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} aria-label="Search logs" />
      </div>
      <ErrorBox error={error} />
      <div className="card" style={{ padding: 0 }}>
        {isLoading ? <Loading /> : !data?.items.length ? <Empty>No log entries.</Empty> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Time</th><th>Severity</th><th>Source</th><th>Message</th></tr></thead>
              <tbody>
                {data.items.map((l) => (
                  <tr key={l.id}>
                    <td style={{ whiteSpace: 'nowrap' }}>{dateTime(l.timestamp)}</td>
                    <td><Badge tone={l.severity === 'Error' || l.severity === 'Critical' ? 'critical' : l.severity === 'Warning' ? 'warning' : 'neutral'} label={l.severity} /></td>
                    <td>{l.source}</td>
                    <td className="mono" style={{ overflowWrap: 'anywhere' }}>{l.message}</td>
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
