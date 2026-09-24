import { describe, expect, it } from 'vitest';
import { bytes, duration, humanize, timeAgo } from './format';
import { buildUrl } from '../api/client';

describe('format', () => {
  it('humanizes PascalCase enum values', () => {
    expect(humanize('OutOfService')).toBe('Out of service');
    expect(humanize('Online')).toBe('Online');
  });

  it('formats relative time', () => {
    const now = Date.parse('2026-01-01T12:00:00Z');
    expect(timeAgo(null, now)).toBe('never');
    expect(timeAgo('2026-01-01T11:59:50Z', now)).toBe('just now');
    expect(timeAgo('2026-01-01T11:50:00Z', now)).toMatch(/10 minutes ago/);
  });

  it('formats bytes and durations', () => {
    expect(bytes(512)).toBe('512 B');
    expect(bytes(1536)).toBe('1.5 KB');
    expect(duration(90061)).toBe('1d 1h');
    expect(duration(3720)).toBe('1h 2m');
  });

  it('builds query strings without empty values', () => {
    expect(buildUrl('/api/atms', { page: 1, status: '', search: undefined, q: 'a b' })).toBe('/api/atms?page=1&q=a+b');
    expect(buildUrl('/api/atms')).toBe('/api/atms');
  });
});
