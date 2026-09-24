import { useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAtms, useSaveAtm } from '../api/hooks';
import type { AtmStatus, UpsertAtmRequest } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Empty, ErrorBox, Loading, Modal, Pager } from '../components/Common';
import { AtmStatusBadge } from '../components/StatusBadge';
import { humanize, money, timeAgo } from '../lib/format';

const statuses: AtmStatus[] = ['Online', 'Degraded', 'OutOfService', 'Offline', 'Maintenance', 'Unknown'];

export function AtmListPage() {
  const [params, setParams] = useSearchParams();
  const navigate = useNavigate();
  const { hasRole } = useAuth();
  const [creating, setCreating] = useState(false);
  const status = (params.get('status') ?? '') as AtmStatus | '';
  const search = params.get('q') ?? '';
  const page = Number(params.get('page') ?? 1);
  const { data, isLoading, error, isFetching } = useAtms({ page, pageSize: 50, status, search });

  const update = (patch: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v); else next.delete(k);
    }
    if (!('page' in patch)) next.delete('page');
    setParams(next, { replace: true });
  };

  return (
    <div>
      <div className="page-head">
        <h1>Terminals</h1>
        <span className="spacer" />
        {hasRole('Admin') && <button className="btn btn-primary" onClick={() => setCreating(true)}>Register terminal</button>}
      </div>
      <div className="toolbar">
        <SearchBox initial={search} onSearch={(q) => update({ q })} />
        <select value={status} onChange={(e) => update({ status: e.target.value })} aria-label="Status filter">
          <option value="">All statuses</option>
          {statuses.map((s) => <option key={s} value={s}>{humanize(s)}</option>)}
        </select>
        {isFetching && <span className="sub">Updating…</span>}
      </div>
      <ErrorBox error={error} />
      <div className="card" style={{ padding: 0 }}>
        {isLoading ? <Loading /> : !data?.items.length ? <Empty>No terminals match. Enroll an agent to get started.</Empty> : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr><th>Terminal</th><th>Status</th><th>Location</th><th className="num">Cash</th><th className="num">Alerts</th><th>Last seen</th><th>Agent</th></tr>
              </thead>
              <tbody>
                {data.items.map((a) => (
                  <tr key={a.id} className="row-link" onClick={() => navigate(`/atms/${a.id}`)}>
                    <td><a href={`/atms/${a.id}`} onClick={(e) => e.preventDefault()}><strong>{a.terminalId}</strong></a><div className="sub">{a.name}</div></td>
                    <td><AtmStatusBadge status={a.status} />{!a.isEnabled && <div className="sub">Disabled</div>}</td>
                    <td>{a.branch ?? '—'}<div className="sub">{a.city}</div></td>
                    <td className="num">{money(a.availableCash)}{a.lowCassettes > 0 && <div className="sub">{a.lowCassettes} low</div>}</td>
                    <td className="num">{a.openAlerts || '—'}</td>
                    <td title={a.lastSeenAt ?? ''}>{timeAgo(a.lastSeenAt)}</td>
                    <td className="sub">{a.isEnrolled ? a.agentVersion ?? 'enrolled' : 'not enrolled'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {data && <Pager page={page} pageSize={data.pageSize} total={data.total} onPage={(p) => update({ page: String(p) })} />}
      <AtmFormDialog open={creating} onClose={() => setCreating(false)} onSaved={(id) => navigate(`/atms/${id}`)} />
    </div>
  );
}

/** Debounced so typing does not fire a request per keystroke. */
function SearchBox({ initial, onSearch }: { initial: string; onSearch: (q: string) => void }) {
  const [value, setValue] = useState(initial);
  const latest = useRef(onSearch);
  useEffect(() => { latest.current = onSearch; });
  useEffect(() => {
    if (value === initial) return;
    const t = setTimeout(() => latest.current(value.trim()), 300);
    return () => clearTimeout(t);
  }, [value, initial]);
  return <input type="search" placeholder="Search terminal, name, branch…" value={value} aria-label="Search"
    onChange={(e) => setValue(e.target.value)} style={{ width: 280 }} />;
}

export function AtmFormDialog({ open, onClose, onSaved, initial, id }: {
  open: boolean; onClose: () => void; onSaved?: (id: string) => void; initial?: UpsertAtmRequest; id?: string;
}) {
  const save = useSaveAtm();
  const [form, setForm] = useState<UpsertAtmRequest>(initial ?? { terminalId: '', name: '' });
  const field = (k: keyof UpsertAtmRequest, label: string, props: React.InputHTMLAttributes<HTMLInputElement> = {}) => (
    <label className="field">{label}
      <input value={(form[k] as string | number | null | undefined) ?? ''} {...props}
        onChange={(e) => setForm({ ...form, [k]: e.target.value === '' ? null : props.type === 'number' ? Number(e.target.value) : e.target.value })} />
    </label>
  );

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const saved = await save.mutateAsync({ id, body: form });
    onSaved?.(saved.id);
    onClose();
  };

  return (
    <Modal open={open} onClose={onClose} title={id ? 'Edit terminal' : 'Register terminal'}>
      <form onSubmit={submit} className="grid" style={{ gap: 10, gridTemplateColumns: '1fr 1fr' }}>
        {field('terminalId', 'Terminal ID', { required: true, pattern: '[A-Za-z0-9_\\-]{1,32}' })}
        {field('name', 'Display name', { required: true, maxLength: 128 })}
        {field('branch', 'Branch')}
        {field('city', 'City')}
        <div style={{ gridColumn: '1 / -1' }}>{field('address', 'Address')}</div>
        {field('vendor', 'Vendor')}
        {field('model', 'Model')}
        {field('serialNumber', 'Serial number')}
        <span />
        {field('latitude', 'Latitude', { type: 'number', step: 'any', min: -90, max: 90 })}
        {field('longitude', 'Longitude', { type: 'number', step: 'any', min: -180, max: 180 })}
        <div style={{ gridColumn: '1 / -1' }}><ErrorBox error={save.error} /></div>
        <div className="dialog-actions" style={{ gridColumn: '1 / -1' }}>
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn btn-primary" disabled={save.isPending}>Save</button>
        </div>
      </form>
    </Modal>
  );
}
