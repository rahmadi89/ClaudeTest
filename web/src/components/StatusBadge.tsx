import type { AlertSeverity, AtmStatus, CommandStatus, ComponentState, CassetteStatus } from '../api/types';
import { humanize } from '../lib/format';

/**
 * Status is never color-alone: each tone has a distinct glyph shape (circle / triangle / diamond / square / cross)
 * plus a text label in normal ink.
 */
export type Tone = 'good' | 'warning' | 'serious' | 'critical' | 'neutral' | 'info';

const glyphs: Record<Tone, string> = {
  good: 'M5 1a4 4 0 1 1 0 8a4 4 0 1 1 0-8z',
  warning: 'M5 0.8 9.4 9H0.6z',
  serious: 'M5 0.5 9.5 5 5 9.5 0.5 5z',
  critical: 'M1.5 0.5 5 4 8.5 0.5 9.5 1.5 6 5 9.5 8.5 8.5 9.5 5 6 1.5 9.5 0.5 8.5 4 5 0.5 1.5z',
  neutral: 'M1.5 1.5h7v7h-7z',
  info: 'M5 1a4 4 0 1 1 0 8a4 4 0 1 1 0-8z',
};

export function Glyph({ tone }: { tone: Tone }) {
  return (
    <svg className="glyph" viewBox="0 0 10 10" aria-hidden="true">
      <path d={glyphs[tone]} fill={`var(--${tone})`} />
    </svg>
  );
}

export function Badge({ tone, label, title }: { tone: Tone; label: string; title?: string }) {
  return (
    <span className="badge" title={title}>
      <Glyph tone={tone} />
      {label}
    </span>
  );
}

export const atmTone = (s: AtmStatus): Tone =>
  ({ Online: 'good', Degraded: 'warning', OutOfService: 'serious', Offline: 'critical', Maintenance: 'info', Unknown: 'neutral' } as const)[s];

export const severityTone = (s: AlertSeverity): Tone =>
  ({ Info: 'neutral', Warning: 'warning', Major: 'serious', Critical: 'critical' } as const)[s];

export const componentTone = (s: ComponentState): Tone =>
  ({ Ok: 'good', Warning: 'warning', Error: 'critical', Offline: 'critical', Unknown: 'neutral' } as const)[s];

export const cassetteTone = (s: CassetteStatus): Tone =>
  ({ Ok: 'good', High: 'warning', Low: 'warning', Empty: 'critical', Full: 'serious', Missing: 'critical', Inoperative: 'critical', Unknown: 'neutral' } as const)[s];

export const commandTone = (s: CommandStatus): Tone =>
  ({
    Pending: 'neutral', Sent: 'info', Acknowledged: 'info', Running: 'info', Succeeded: 'good',
    Failed: 'critical', TimedOut: 'serious', Cancelled: 'neutral', Rejected: 'serious',
  } as const)[s];

export const AtmStatusBadge = ({ status }: { status: AtmStatus }) => <Badge tone={atmTone(status)} label={humanize(status)} />;
export const SeverityBadge = ({ severity }: { severity: AlertSeverity }) => <Badge tone={severityTone(severity)} label={severity} />;
export const CommandStatusBadge = ({ status }: { status: CommandStatus }) => <Badge tone={commandTone(status)} label={humanize(status)} />;
