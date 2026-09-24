import { useState } from 'react';
import { useAudit, useCreateEnrollmentToken, useEnrollmentTokens, useResetPassword, useRevokeEnrollmentToken, useSaveUser, useUsers } from '../api/hooks';
import type { UserRole, UserView } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { Empty, ErrorBox, Loading, Modal, Pager } from '../components/Common';
import { Badge } from '../components/StatusBadge';
import { dateTime, timeAgo } from '../lib/format';

export function EnrollmentPage() {
  const { data, isLoading, error } = useEnrollmentTokens();
  const create = useCreateEnrollmentToken();
  const revoke = useRevokeEnrollmentToken();
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({ description: '', maxUses: 1, validHours: 72 });
  const [secret, setSecret] = useState<string | null>(null);
  const [now] = useState(() => Date.now());

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const res = await create.mutateAsync(form);
    setSecret(res.secret);
  };

  const close = () => { setOpen(false); setSecret(null); create.reset(); setForm({ description: '', maxUses: 1, validHours: 72 }); };

  return (
    <div>
      <div className="page-head">
        <h1>Agent enrollment</h1>
        <span className="spacer" />
        <button className="btn btn-primary" onClick={() => setOpen(true)}>Create enrollment token</button>
      </div>
      <p className="sub" style={{ maxWidth: 720 }}>
        An agent uses an enrollment token once to obtain its own per-terminal key. Configure the token as <code>Agent:EnrollmentToken</code> on the
        terminal (or pass it to the installer), start the service, then remove it. Prefer single-use, short-lived tokens.
      </p>
      <ErrorBox error={error ?? revoke.error} />
      <div className="card" style={{ padding: 0 }}>
        {isLoading ? <Loading /> : !data?.length ? <Empty>No tokens yet.</Empty> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Description</th><th>State</th><th className="num">Uses</th><th>Expires</th><th>Created</th><th /></tr></thead>
              <tbody>
                {data.map((t) => {
                  const expired = new Date(t.expiresAt).getTime() < now;
                  const usable = !t.isRevoked && !expired && t.useCount < t.maxUses;
                  return (
                    <tr key={t.id}>
                      <td>{t.description}</td>
                      <td><Badge tone={usable ? 'good' : 'neutral'} label={t.isRevoked ? 'Revoked' : expired ? 'Expired' : t.useCount >= t.maxUses ? 'Used up' : 'Active'} /></td>
                      <td className="num">{t.useCount} / {t.maxUses}</td>
                      <td>{dateTime(t.expiresAt)}</td>
                      <td>{timeAgo(t.createdAt)} <span className="sub">by {t.createdBy}</span></td>
                      <td>{usable && <button className="btn btn-sm btn-danger" onClick={() => window.confirm('Revoke this token?') && revoke.mutate(t.id)}>Revoke</button>}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
      <Modal open={open} onClose={close} title="Create enrollment token">
        {secret ? (
          <>
            <p>Copy this token now. It is shown only once and stored only as a hash.</p>
            <div className="secret">{secret}</div>
            <div className="dialog-actions">
              <button className="btn" onClick={() => navigator.clipboard?.writeText(secret)}>Copy</button>
              <button className="btn btn-primary" onClick={close}>Done</button>
            </div>
          </>
        ) : (
          <form onSubmit={submit} className="stack" style={{ gap: 12 }}>
            <label className="field">Description<input required maxLength={256} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} placeholder="Rollout batch – Downtown branches" /></label>
            <label className="field">Maximum uses<input type="number" min={1} max={10000} value={form.maxUses} onChange={(e) => setForm({ ...form, maxUses: Number(e.target.value) })} /></label>
            <label className="field">Valid for (hours)<input type="number" min={1} max={720} value={form.validHours} onChange={(e) => setForm({ ...form, validHours: Number(e.target.value) })} /></label>
            <ErrorBox error={create.error} />
            <div className="dialog-actions">
              <button type="button" className="btn" onClick={close}>Cancel</button>
              <button className="btn btn-primary" disabled={create.isPending}>Create</button>
            </div>
          </form>
        )}
      </Modal>
    </div>
  );
}

const roles: UserRole[] = ['Viewer', 'Operator', 'Admin'];

export function UsersPage() {
  const { data, isLoading, error } = useUsers();
  const { user: me } = useAuth();
  const [editing, setEditing] = useState<UserView | 'new' | null>(null);
  const [resetting, setResetting] = useState<UserView | null>(null);

  return (
    <div>
      <div className="page-head">
        <h1>Users</h1>
        <span className="spacer" />
        <button className="btn btn-primary" onClick={() => setEditing('new')}>Add user</button>
      </div>
      <ErrorBox error={error} />
      <div className="card" style={{ padding: 0 }}>
        {isLoading ? <Loading /> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>User</th><th>Role</th><th>State</th><th>Last login</th><th /></tr></thead>
              <tbody>
                {data?.map((u) => (
                  <tr key={u.id}>
                    <td><strong>{u.userName}</strong><div className="sub">{u.displayName}</div></td>
                    <td>{u.role}</td>
                    <td><Badge tone={!u.isActive ? 'neutral' : u.isLockedOut ? 'warning' : 'good'} label={!u.isActive ? 'Inactive' : u.isLockedOut ? 'Locked out' : 'Active'} /></td>
                    <td>{timeAgo(u.lastLoginAt)}</td>
                    <td style={{ whiteSpace: 'nowrap' }}>
                      <button className="btn btn-sm" onClick={() => setEditing(u)}>Edit</button>{' '}
                      <button className="btn btn-sm" onClick={() => setResetting(u)}>Reset password</button>
                      {u.id === me?.id && <span className="sub"> (you)</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      {editing && <UserDialog user={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      {resetting && <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} />}
    </div>
  );
}

function UserDialog({ user, onClose }: { user: UserView | null; onClose: () => void }) {
  const save = useSaveUser();
  const [form, setForm] = useState({
    userName: user?.userName ?? '', displayName: user?.displayName ?? '', role: user?.role ?? ('Viewer' as UserRole),
    isActive: user?.isActive ?? true, password: '',
  });
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    await save.mutateAsync({ id: user?.id, ...form });
    onClose();
  };
  return (
    <Modal open onClose={onClose} title={user ? `Edit ${user.userName}` : 'Add user'}>
      <form onSubmit={submit} className="stack" style={{ gap: 12 }}>
        {!user && <label className="field">User name<input required pattern="[a-zA-Z0-9._\-]{3,64}" value={form.userName} onChange={(e) => setForm({ ...form, userName: e.target.value })} /></label>}
        <label className="field">Display name<input required maxLength={128} value={form.displayName} onChange={(e) => setForm({ ...form, displayName: e.target.value })} /></label>
        <label className="field">Role
          <select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value as UserRole })}>{roles.map((r) => <option key={r}>{r}</option>)}</select>
        </label>
        {user && <label><input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} /> Active</label>}
        {!user && <label className="field">Initial password (min. 12 characters)<input type="password" required minLength={12} autoComplete="new-password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} /></label>}
        <ErrorBox error={save.error} />
        <div className="dialog-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn btn-primary" disabled={save.isPending}>Save</button>
        </div>
      </form>
    </Modal>
  );
}

function ResetPasswordDialog({ user, onClose }: { user: UserView; onClose: () => void }) {
  const reset = useResetPassword();
  const [password, setPassword] = useState('');
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    await reset.mutateAsync({ id: user.id, newPassword: password });
    onClose();
  };
  return (
    <Modal open onClose={onClose} title={`Reset password for ${user.userName}`}>
      <form onSubmit={submit} className="stack" style={{ gap: 12 }}>
        <label className="field">New password (min. 12 characters)<input type="password" required minLength={12} autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} /></label>
        <ErrorBox error={reset.error} />
        <div className="dialog-actions">
          <button type="button" className="btn" onClick={onClose}>Cancel</button>
          <button className="btn btn-primary" disabled={reset.isPending}>Reset</button>
        </div>
      </form>
    </Modal>
  );
}

export function AuditPage() {
  const [page, setPage] = useState(1);
  const [action, setAction] = useState('');
  const [actor, setActor] = useState('');
  const { data, isLoading, error } = useAudit({ page, pageSize: 100, action, actor });
  return (
    <div>
      <div className="page-head"><h1>Audit log</h1><span className="sub">Append-only record of security-relevant actions.</span></div>
      <div className="toolbar">
        <input placeholder="Action prefix (e.g. command.)" value={action} onChange={(e) => { setAction(e.target.value); setPage(1); }} aria-label="Action" />
        <input placeholder="Actor (exact)" value={actor} onChange={(e) => { setActor(e.target.value); setPage(1); }} aria-label="Actor" />
      </div>
      <ErrorBox error={error} />
      <div className="card" style={{ padding: 0 }}>
        {isLoading ? <Loading /> : !data?.items.length ? <Empty>No entries.</Empty> : (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Time</th><th>Actor</th><th>Action</th><th>Target</th><th>Details</th><th>IP</th></tr></thead>
              <tbody>
                {data.items.map((a) => (
                  <tr key={a.id}>
                    <td style={{ whiteSpace: 'nowrap' }}>{dateTime(a.timestamp)}</td>
                    <td>{a.actor}</td>
                    <td className="mono">{a.action}</td>
                    <td>{a.targetType} {a.targetId}</td>
                    <td className="sub">{a.details}</td>
                    <td className="mono sub">{a.ipAddress}</td>
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
