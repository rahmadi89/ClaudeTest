import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { get, post, setUnauthorizedHandler, tokenStore } from '../api/client';
import type { LoginResponse, UserInfo, UserRole } from '../api/types';

interface AuthState {
  user: UserInfo | null;
  loading: boolean;
  login: (userName: string, password: string) => Promise<void>;
  logout: () => void;
  hasRole: (min: UserRole) => boolean;
}

const rank: Record<UserRole, number> = { Viewer: 0, Operator: 1, Admin: 2 };
const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [user, setUser] = useState<UserInfo | null>(null);
  const [loading, setLoading] = useState(() => tokenStore.get() !== null);

  const logout = useCallback(() => {
    tokenStore.set(null);
    setUser(null);
    qc.clear();
  }, [qc]);

  useEffect(() => {
    setUnauthorizedHandler(logout);
    if (tokenStore.get()) {
      get<UserInfo>('/api/auth/me').then(setUser).catch(() => logout()).finally(() => setLoading(false));
    }
    return () => setUnauthorizedHandler(null);
  }, [logout]);

  const login = useCallback(async (userName: string, password: string) => {
    const res = await post<LoginResponse>('/api/auth/login', { userName, password });
    tokenStore.set(res.accessToken);
    setUser(res.user);
  }, []);

  const value = useMemo<AuthState>(() => ({
    user, loading, login, logout,
    hasRole: (min) => user !== null && rank[user.role] >= rank[min],
  }), [user, loading, login, logout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider');
  return ctx;
}
