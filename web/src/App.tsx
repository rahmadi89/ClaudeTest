import type { ReactNode } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import type { UserRole } from './api/types';
import { useAuth } from './auth/AuthContext';
import { Loading } from './components/Common';
import { Layout } from './components/Layout';
import { AuditPage, EnrollmentPage, UsersPage } from './pages/AdminPages';
import { AlertsPage } from './pages/AlertsPage';
import { AtmDetailPage } from './pages/AtmDetailPage';
import { AtmListPage } from './pages/AtmListPage';
import { CommandsPage } from './pages/CommandsPage';
import { DashboardPage } from './pages/DashboardPage';
import { LoginPage } from './pages/LoginPage';
import { useRealtime } from './realtime/useRealtime';

function RequireRole({ role, children }: { role: UserRole; children: ReactNode }) {
  const { hasRole } = useAuth();
  return hasRole(role) ? children : <div className="empty">You do not have access to this page.</div>;
}

export function App() {
  const { user, loading } = useAuth();
  const realtime = useRealtime(user !== null);

  if (loading) return <Loading />;
  if (!user) {
    return (
      <Routes>
        <Route path="*" element={<LoginPage />} />
      </Routes>
    );
  }

  return (
    <Routes>
      <Route element={<Layout realtime={realtime} />}>
        <Route index element={<DashboardPage />} />
        <Route path="atms" element={<AtmListPage />} />
        <Route path="atms/:id" element={<AtmDetailPage />} />
        <Route path="alerts" element={<AlertsPage />} />
        <Route path="commands" element={<CommandsPage />} />
        <Route path="admin/enrollment" element={<RequireRole role="Admin"><EnrollmentPage /></RequireRole>} />
        <Route path="admin/users" element={<RequireRole role="Admin"><UsersPage /></RequireRole>} />
        <Route path="admin/audit" element={<RequireRole role="Admin"><AuditPage /></RequireRole>} />
        <Route path="login" element={<Navigate to="/" replace />} />
        <Route path="*" element={<div className="empty">Page not found.</div>} />
      </Route>
    </Routes>
  );
}
