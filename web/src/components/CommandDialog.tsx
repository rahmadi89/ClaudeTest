import { useState } from 'react';
import { useIssueCommand } from '../api/hooks';
import type { CommandType, UserRole } from '../api/types';
import { useAuth } from '../auth/AuthContext';
import { ErrorBox, Modal } from './Common';

interface CommandSpec { type: CommandType; label: string; description: string; role: UserRole; needsReason: boolean; danger?: boolean }

// Keep in sync with AtmMonitor.Domain.Services.CommandPolicy (the server enforces it regardless).
export const commandCatalog: CommandSpec[] = [
  { type: 'Ping', label: 'Ping', description: 'Round-trip check that the agent is responsive.', role: 'Operator', needsReason: false },
  { type: 'RefreshStatus', label: 'Refresh status', description: 'Collect and push a full status report now.', role: 'Operator', needsReason: false },
  { type: 'RunDiagnostics', label: 'Run diagnostics', description: 'Network, disk, and device self-test.', role: 'Operator', needsReason: false },
  { type: 'CollectLogs', label: 'Collect logs', description: 'Fetch the last lines of a log source.', role: 'Operator', needsReason: false },
  { type: 'ResetDevice', label: 'Reset device', description: 'Reset a single device (e.g. card reader).', role: 'Operator', needsReason: false },
  { type: 'SetOutOfService', label: 'Put out of service', description: 'Stops customer transactions.', role: 'Operator', needsReason: true, danger: true },
  { type: 'SetInService', label: 'Put in service', description: 'Resumes customer transactions.', role: 'Operator', needsReason: true },
  { type: 'RestartAgent', label: 'Restart agent', description: 'Restarts the monitoring agent process.', role: 'Operator', needsReason: false },
  { type: 'RestartApplication', label: 'Restart ATM application', description: 'Runs the terminal’s configured restart procedure.', role: 'Admin', needsReason: true, danger: true },
  { type: 'RunScript', label: 'Run allow-listed script', description: 'Runs a script from the terminal’s local allow-list.', role: 'Admin', needsReason: true, danger: true },
  { type: 'RebootMachine', label: 'Reboot terminal', description: 'Reboots the whole machine (must be enabled on the terminal).', role: 'Admin', needsReason: true, danger: true },
];

const devices = ['CardReader', 'CashDispenser', 'Depository', 'ReceiptPrinter', 'JournalPrinter', 'PinPad', 'Display', 'Camera'];

export function CommandDialog({ atmId, terminalId, open, onClose }: { atmId: string; terminalId: string; open: boolean; onClose: () => void }) {
  const { hasRole } = useAuth();
  const issue = useIssueCommand(atmId);
  const available = commandCatalog.filter((c) => hasRole(c.role));
  const [type, setType] = useState<CommandType>('Ping');
  const [reason, setReason] = useState('');
  const [param, setParam] = useState('');
  const spec = commandCatalog.find((c) => c.type === type)!;

  const parameters: Record<string, string> | undefined =
    type === 'RunScript' ? { script: param }
    : type === 'ResetDevice' ? { device: param || devices[0]! }
    : type === 'CollectLogs' ? { source: param || 'agent', lines: '200' }
    : undefined;

  const close = () => { issue.reset(); setReason(''); setParam(''); onClose(); };

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (spec.danger && !window.confirm(`${spec.label} on ${terminalId}?`)) return;
    await issue.mutateAsync({ type, parameters, reason: reason.trim() || undefined });
    close();
  };

  return (
    <Modal open={open} onClose={close} title={`Send command to ${terminalId}`}>
      <form onSubmit={submit} className="stack" style={{ gap: 12 }}>
        <label className="field">Command
          <select value={type} onChange={(e) => { setType(e.target.value as CommandType); setParam(''); }}>
            {available.map((c) => <option key={c.type} value={c.type}>{c.label}</option>)}
          </select>
        </label>
        <div className="sub">{spec.description}</div>
        {type === 'ResetDevice' && (
          <label className="field">Device
            <select value={param || devices[0]} onChange={(e) => setParam(e.target.value)}>
              {devices.map((d) => <option key={d}>{d}</option>)}
            </select>
          </label>
        )}
        {type === 'RunScript' && (
          <label className="field">Script name (must exist in the terminal’s allow-list)
            <input required value={param} onChange={(e) => setParam(e.target.value)} placeholder="e.g. clear-temp" />
          </label>
        )}
        {type === 'CollectLogs' && (
          <label className="field">Log source
            <input value={param} onChange={(e) => setParam(e.target.value)} placeholder="agent" />
          </label>
        )}
        <label className="field">Reason {spec.needsReason ? '(required, recorded in audit log)' : '(optional)'}
          <textarea value={reason} required={spec.needsReason} maxLength={512} onChange={(e) => setReason(e.target.value)} />
        </label>
        <ErrorBox error={issue.error} />
        <div className="dialog-actions">
          <button type="button" className="btn" onClick={close}>Cancel</button>
          <button type="submit" className={`btn ${spec.danger ? 'btn-danger' : 'btn-primary'}`} disabled={issue.isPending}>
            {issue.isPending ? 'Sending…' : 'Send command'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
