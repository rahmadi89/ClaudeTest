import { useEffect, useState } from 'react';
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { tokenStore } from '../api/client';
import { keys } from '../api/hooks';

export type RealtimeState = 'connecting' | 'live' | 'reconnecting' | 'offline';

interface AtmUpdated { atmId: string }
interface AlertChanged { atmId: string }
interface CommandChanged { atmId: string }

/**
 * Subscribes to the dashboard hub and turns server pushes into targeted query invalidations.
 * Events are coalesced per animation frame so a burst of reports from hundreds of terminals
 * triggers one refetch per query, not hundreds.
 */
export function useRealtime(enabled: boolean): RealtimeState {
  const qc = useQueryClient();
  const [state, setState] = useState<RealtimeState>('connecting');

  useEffect(() => {
    if (!enabled) return;
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/dashboard', { accessTokenFactory: () => tokenStore.get() ?? '' })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => Math.min(30_000, 1000 * 2 ** ctx.previousRetryCount) })
      .configureLogging(LogLevel.Warning)
      .build();

    const pending = new Set<string>();
    let frame = 0;
    const invalidate = (key: readonly unknown[]) => {
      pending.add(JSON.stringify(key));
      if (!frame) {
        frame = requestAnimationFrame(() => {
          frame = 0;
          for (const k of pending) qc.invalidateQueries({ queryKey: JSON.parse(k) });
          pending.clear();
        });
      }
    };

    connection.on('AtmUpdated', (e: AtmUpdated) => {
      invalidate(keys.atm(e.atmId));
      invalidate([...keys.atms, 'list']);
      invalidate(keys.dashboard);
    });
    connection.on('AlertChanged', (_e: AlertChanged) => {
      invalidate(keys.alerts);
      invalidate(keys.dashboard);
      invalidate([...keys.atms, 'list']);
    });
    connection.on('CommandChanged', (_e: CommandChanged) => {
      invalidate(keys.commands);
      invalidate(keys.dashboard);
    });

    connection.onreconnecting(() => setState('reconnecting'));
    connection.onreconnected(() => { setState('live'); qc.invalidateQueries(); });
    connection.onclose(() => setState('offline'));

    let disposed = false;
    connection.start()
      .then(() => { if (!disposed) setState('live'); })
      .catch(() => { if (!disposed) setState('offline'); });

    return () => {
      disposed = true;
      if (frame) cancelAnimationFrame(frame);
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop();
    };
  }, [enabled, qc]);

  return state;
}
