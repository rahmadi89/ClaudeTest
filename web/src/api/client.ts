// Thin fetch wrapper: attaches the bearer token, parses RFC 7807 problem responses, and signals expired sessions.

const TOKEN_KEY = 'atm-monitor.token';

export class ApiError extends Error {
  readonly status: number;
  readonly details?: Record<string, string[]>;
  constructor(status: number, message: string, details?: Record<string, string[]>) {
    super(message);
    this.status = status;
    this.details = details;
  }
}

// sessionStorage: the token dies with the tab. See docs/SECURITY.md for the planned move to OIDC + cookie BFF.
export const tokenStore = {
  get: (): string | null => {
    try { return sessionStorage.getItem(TOKEN_KEY); } catch { return null; }
  },
  set: (token: string | null) => {
    try {
      if (token) sessionStorage.setItem(TOKEN_KEY, token);
      else sessionStorage.removeItem(TOKEN_KEY);
    } catch { /* storage unavailable (private mode); session will not survive reload */ }
  },
};

let onUnauthorized: (() => void) | null = null;
export const setUnauthorizedHandler = (fn: (() => void) | null) => { onUnauthorized = fn; };

type Query = Record<string, string | number | boolean | null | undefined>;

export function buildUrl(path: string, query?: Query): string {
  if (!query) return path;
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v !== undefined && v !== null && v !== '') params.set(k, String(v));
  }
  const qs = params.toString();
  return qs ? `${path}?${qs}` : path;
}

export async function api<T>(method: string, path: string, options: { body?: unknown; query?: Query } = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  const token = tokenStore.get();
  if (token) headers.Authorization = `Bearer ${token}`;
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';

  const res = await fetch(buildUrl(path, options.query), {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  });

  if (res.status === 401 && token) {
    onUnauthorized?.();
  }

  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`;
    let details: Record<string, string[]> | undefined;
    try {
      const problem = await res.json();
      message = problem.title ?? message;
      details = problem.errors;
      if (details) message = Object.values(details).flat().join(' ') || message;
    } catch { /* non-JSON error body */ }
    throw new ApiError(res.status, message, details);
  }

  if (res.status === 204 || res.headers.get('content-length') === '0') return undefined as T;
  return (await res.json()) as T;
}

export const get = <T,>(path: string, query?: Query) => api<T>('GET', path, { query });
export const post = <T,>(path: string, body?: unknown) => api<T>('POST', path, { body: body ?? {} });
export const put = <T,>(path: string, body: unknown) => api<T>('PUT', path, { body });
export const del = <T,>(path: string) => api<T>('DELETE', path);
