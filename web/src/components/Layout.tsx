import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { useDashboard } from '../api/hooks';
import type { RealtimeState } from '../realtime/useRealtime';
import { Glyph, type Tone } from './StatusBadge';

const liveTone: Record<RealtimeState, [Tone, string]> = {
  connecting: ['neutral', 'Connecting…'],
  live: ['good', 'Live'],
  reconnecting: ['warning', 'Reconnecting…'],
  offline: ['critical', 'Live updates offline'],
};

export function Layout({ realtime }: { realtime: RealtimeState }) {
  const { user, logout, hasRole } = useAuth();
  const { data } = useDashboard();
  const critical = (data?.openAlertsBySeverity.Critical ?? 0) + (data?.openAlertsBySeverity.Major ?? 0);
  const [tone, label] = liveTone[realtime];
  const link = ({ isActive }: { isActive: boolean }) => `nav-link${isActive ? ' active' : ''}`;

  return (
    <div className="shell">
      <nav className="sidebar" aria-label="Main">
        <div className="brand"><span className="brand-mark" aria-hidden="true">▣</span>ATM Fleet Monitor</div>
        <NavLink to="/" end className={link}>Dashboard</NavLink>
        <NavLink to="/atms" className={link}>Terminals</NavLink>
        <NavLink to="/alerts" className={link}>
          Alerts {critical > 0 && <span className="nav-count" aria-label={`${critical} major or critical`}>{critical}</span>}
        </NavLink>
        <NavLink to="/commands" className={link}>Commands</NavLink>
        {hasRole('Admin') && (
          <>
            <div className="nav-section">Administration</div>
            <NavLink to="/admin/enrollment" className={link}>Agent enrollment</NavLink>
            <NavLink to="/admin/users" className={link}>Users</NavLink>
            <NavLink to="/admin/audit" className={link}>Audit log</NavLink>
          </>
        )}
        <div className="sidebar-footer">
          <span className="live" role="status"><Glyph tone={tone} />{label}</span>
          <span>{user?.displayName} · {user?.role}</span>
          <button className="btn btn-sm" onClick={logout}>Sign out</button>
        </div>
      </nav>
      <main className="main">
        <Outlet />
      </main>
    </div>
  );
}
