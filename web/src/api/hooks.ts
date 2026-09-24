import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { del, get, post, put } from './client';
import type {
  AlertSeverity, AlertStatus, AlertView, AtmDetail, AtmListItem, AtmStatus, AuditView, CommandStatus, CommandType,
  CommandView, CreatedEnrollmentToken, DashboardSummary, EnrollmentTokenView, LogSeverity, LogView, PagedResult,
  TelemetryPoint, UpsertAtmRequest, UserRole, UserView,
} from './types';

export const keys = {
  dashboard: ['dashboard'] as const,
  atms: ['atms'] as const,
  atm: (id: string) => ['atms', id] as const,
  alerts: ['alerts'] as const,
  commands: ['commands'] as const,
  admin: ['admin'] as const,
};

export const useDashboard = () =>
  useQuery({ queryKey: keys.dashboard, queryFn: () => get<DashboardSummary>('/api/dashboard/summary'), refetchInterval: 60_000 });

export const useAtms = (q: { page: number; pageSize: number; status?: AtmStatus | ''; search?: string }) =>
  useQuery({
    queryKey: [...keys.atms, 'list', q],
    queryFn: () => get<PagedResult<AtmListItem>>('/api/atms', q),
    placeholderData: keepPreviousData,
  });

export const useAtm = (id: string) =>
  useQuery({ queryKey: keys.atm(id), queryFn: () => get<AtmDetail>(`/api/atms/${id}`) });

export const useTelemetry = (id: string, hours: number) =>
  useQuery({
    queryKey: [...keys.atm(id), 'telemetry', hours],
    queryFn: () => get<TelemetryPoint[]>(`/api/atms/${id}/telemetry`, { from: new Date(Date.now() - hours * 3600_000).toISOString() }),
    refetchInterval: 60_000,
  });

export const useAtmLogs = (id: string, q: { page: number; pageSize: number; minSeverity?: LogSeverity | ''; search?: string }) =>
  useQuery({
    queryKey: [...keys.atm(id), 'logs', q],
    queryFn: () => get<PagedResult<LogView>>(`/api/atms/${id}/logs`, q),
    placeholderData: keepPreviousData,
    refetchInterval: 15_000,
  });

export const useCommands = (q: { page: number; pageSize: number; status?: CommandStatus | ''; atmId?: string }) =>
  useQuery({
    queryKey: [...keys.commands, q],
    queryFn: () => get<PagedResult<CommandView>>('/api/commands', q),
    placeholderData: keepPreviousData,
  });

export const useAlerts = (q: { page: number; pageSize: number; status?: AlertStatus | ''; active?: boolean; severity?: AlertSeverity | ''; atmId?: string }) =>
  useQuery({
    queryKey: [...keys.alerts, q],
    queryFn: () => get<PagedResult<AlertView>>('/api/alerts', q),
    placeholderData: keepPreviousData,
  });

export function useIssueCommand(atmId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: { type: CommandType; parameters?: Record<string, string>; reason?: string }) =>
      post<CommandView>(`/api/atms/${atmId}/commands`, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.commands }),
  });
}

export function useCancelCommand() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => post<CommandView>(`/api/commands/${id}/cancel`),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.commands }),
  });
}

export function useAlertAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action, note }: { id: string; action: 'acknowledge' | 'resolve'; note?: string }) =>
      post<AlertView>(`/api/alerts/${id}/${action}`, { note: note ?? null }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.alerts });
      qc.invalidateQueries({ queryKey: keys.dashboard });
    },
  });
}

export function useSaveAtm() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id?: string; body: UpsertAtmRequest }) =>
      id ? put<AtmDetail>(`/api/atms/${id}`, body) : post<AtmDetail>('/api/atms', body),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.atms }),
  });
}

export function useAtmAdminAction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'enable' | 'disable' | 'revoke-agent' | 'delete' }) =>
      action === 'delete' ? del<void>(`/api/atms/${id}`) : post<void>(`/api/atms/${id}/${action}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.atms }),
  });
}

// ---- Admin ----
export const useEnrollmentTokens = () =>
  useQuery({ queryKey: [...keys.admin, 'tokens'], queryFn: () => get<EnrollmentTokenView[]>('/api/admin/enrollment-tokens') });

export function useCreateEnrollmentToken() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: { description: string; maxUses: number; validHours: number }) =>
      post<CreatedEnrollmentToken>('/api/admin/enrollment-tokens', body),
    onSuccess: () => qc.invalidateQueries({ queryKey: [...keys.admin, 'tokens'] }),
  });
}

export function useRevokeEnrollmentToken() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => del<void>(`/api/admin/enrollment-tokens/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: [...keys.admin, 'tokens'] }),
  });
}

export const useUsers = () => useQuery({ queryKey: [...keys.admin, 'users'], queryFn: () => get<UserView[]>('/api/admin/users') });

export function useSaveUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (v: { id?: string; userName?: string; displayName: string; role: UserRole; isActive?: boolean; password?: string }) =>
      v.id
        ? put<UserView>(`/api/admin/users/${v.id}`, { displayName: v.displayName, role: v.role, isActive: v.isActive ?? true })
        : post<UserView>('/api/admin/users', { userName: v.userName, displayName: v.displayName, role: v.role, password: v.password }),
    onSuccess: () => qc.invalidateQueries({ queryKey: [...keys.admin, 'users'] }),
  });
}

export const useResetPassword = () =>
  useMutation({ mutationFn: ({ id, newPassword }: { id: string; newPassword: string }) => post<void>(`/api/admin/users/${id}/reset-password`, { newPassword }) });

export const useAudit = (q: { page: number; pageSize: number; action?: string; actor?: string }) =>
  useQuery({ queryKey: [...keys.admin, 'audit', q], queryFn: () => get<PagedResult<AuditView>>('/api/admin/audit', q), placeholderData: keepPreviousData });
