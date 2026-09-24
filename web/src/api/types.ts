// Mirrors the server DTOs in AtmMonitor.Api/Models/Dtos.cs. Enums are serialized as strings.

export type AtmStatus = 'Unknown' | 'Online' | 'Degraded' | 'OutOfService' | 'Offline' | 'Maintenance';
export type OperationalMode = 'Unknown' | 'InService' | 'OutOfService' | 'Supervisor' | 'Maintenance';
export type ComponentState = 'Unknown' | 'Ok' | 'Warning' | 'Error' | 'Offline';
export type CassetteType = 'Dispense' | 'Recycle' | 'Reject' | 'Retract' | 'Deposit';
export type CassetteStatus = 'Unknown' | 'Ok' | 'Low' | 'Empty' | 'High' | 'Full' | 'Missing' | 'Inoperative';
export type AlertSeverity = 'Info' | 'Warning' | 'Major' | 'Critical';
export type AlertStatus = 'Open' | 'Acknowledged' | 'Resolved';
export type LogSeverity = 'Trace' | 'Debug' | 'Information' | 'Warning' | 'Error' | 'Critical';
export type UserRole = 'Viewer' | 'Operator' | 'Admin';
export type CommandStatus =
  | 'Pending' | 'Sent' | 'Acknowledged' | 'Running' | 'Succeeded' | 'Failed' | 'TimedOut' | 'Cancelled' | 'Rejected';
export type CommandType =
  | 'Ping' | 'RefreshStatus' | 'CollectLogs' | 'RunDiagnostics' | 'SetOutOfService' | 'SetInService'
  | 'RestartApplication' | 'RestartAgent' | 'RebootMachine' | 'RunScript' | 'ResetDevice';

export interface PagedResult<T> { items: T[]; total: number; page: number; pageSize: number }

export interface UserInfo { id: string; userName: string; displayName: string; role: UserRole }
export interface LoginResponse { accessToken: string; expiresAt: string; user: UserInfo }

export interface AtmListItem {
  id: string; terminalId: string; name: string; branch: string | null; city: string | null;
  status: AtmStatus; mode: OperationalMode; isEnabled: boolean; isEnrolled: boolean; isConnected: boolean;
  lastSeenAt: string | null; agentVersion: string | null; openAlerts: number; availableCash: number; lowCassettes: number;
}

export interface ComponentView { type: string; state: ComponentState; errorCode: string | null; description: string | null; updatedAt: string; stateChangedAt: string }
export interface CassetteView { cassetteId: string; type: CassetteType; currency: string; denomination: number; count: number; capacity: number; status: CassetteStatus; fillPercent: number; value: number }
export interface NetworkSnapshot { hostReachable: boolean; hostLatencyMs: number | null; packetLossPercent: number; localIpAddress: string | null; interfaceName: string | null; interfaceUp: boolean; linkSpeedMbps: number | null }
export interface SystemSnapshot { cpuPercent: number; memoryUsedPercent: number; diskUsedPercent: number; diskFreeBytes: number; uptimeSeconds: number; osDescription: string | null }

export interface AtmDetail {
  id: string; terminalId: string; name: string; branch: string | null; address: string | null; city: string | null;
  latitude: number | null; longitude: number | null; vendor: string | null; model: string | null; serialNumber: string | null;
  isEnabled: boolean; isEnrolled: boolean; isConnected: boolean; createdAt: string; enrolledAt: string | null;
  status: AtmStatus; mode: OperationalMode; lastSeenAt: string | null; lastReportAt: string | null; statusChangedAt: string | null;
  agentVersion: string | null; machineName: string | null; osDescription: string | null;
  components: ComponentView[]; cassettes: CassetteView[]; network: NetworkSnapshot | null; system: SystemSnapshot | null;
  availableCash: Record<string, number>;
}

export interface UpsertAtmRequest {
  terminalId: string; name: string; branch?: string | null; address?: string | null; city?: string | null;
  latitude?: number | null; longitude?: number | null; vendor?: string | null; model?: string | null; serialNumber?: string | null;
}

export interface TelemetryPoint {
  timestamp: string; cpuPercent: number | null; memoryUsedPercent: number | null; diskUsedPercent: number | null;
  hostLatencyMs: number | null; packetLossPercent: number | null; hostReachable: boolean | null; availableCash: number | null;
}

export interface LogView { id: number; timestamp: string; severity: LogSeverity; source: string; message: string }

export interface CommandView {
  id: string; atmId: string; terminalId: string | null; type: CommandType; parameters: Record<string, string>; status: CommandStatus;
  requestedBy: string; reason: string | null; createdAt: string; sentAt: string | null; completedAt: string | null;
  expiresAt: string; output: string | null; error: string | null;
}

export interface AlertView {
  id: string; atmId: string; terminalId: string | null; atmName: string | null; type: string; severity: AlertSeverity; status: AlertStatus;
  message: string; raisedAt: string; lastOccurredAt: string; occurrenceCount: number;
  acknowledgedAt: string | null; acknowledgedBy: string | null; resolvedAt: string | null; resolvedBy: string | null; note: string | null;
}

export interface DashboardSummary {
  totalAtms: number; byStatus: Partial<Record<AtmStatus, number>>; openAlertsBySeverity: Partial<Record<AlertSeverity, number>>;
  cashByCurrency: Record<string, number>; atmsWithLowCash: number; pendingCommands: number; recentAlerts: AlertView[];
}

export interface EnrollmentTokenView { id: string; description: string; createdAt: string; createdBy: string; expiresAt: string; maxUses: number; useCount: number; isRevoked: boolean }
export interface CreatedEnrollmentToken { token: EnrollmentTokenView; secret: string }
export interface UserView { id: string; userName: string; displayName: string; role: UserRole; isActive: boolean; createdAt: string; lastLoginAt: string | null; isLockedOut: boolean }
export interface AuditView { id: number; timestamp: string; actor: string; action: string; targetType: string | null; targetId: string | null; details: string | null; ipAddress: string | null }
