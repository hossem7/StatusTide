import type { AuthResponse, Dashboard, DemoOutage, Me } from './types'

/** A response other than 2xx. `status` lets screens tell "not signed in" from "server down". */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, method: string, path: string) {
    super(`${method} ${path} returned ${status}`)
    this.name = 'ApiError'
    this.status = status
  }
}

interface RequestOptions {
  signal?: AbortSignal
  body?: unknown
}

/** Get a new token this long before the old one expires, so a hub connection never starts on a dying one. */
const refreshMargin = 30_000

// In memory only, so a script injected into the page can't read it from storage. A reload gets a
// new one from the refresh cookie, which scripts can't read at all. Guests have none.
let access: { token: string; expiresAt: number } | null = null
let refreshing: Promise<Me> | null = null

function send(method: string, path: string, { signal, body }: RequestOptions) {
  return fetch(path, {
    method,
    headers: {
      Accept: 'application/json',
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(access ? { Authorization: `Bearer ${access.token}` } : {}),
    },
    credentials: 'same-origin',
    signal,
    body: body === undefined ? undefined : JSON.stringify(body),
  })
}

async function request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(method, path, options)
  if (response.status === 401 && access && !path.startsWith('/api/auth/')) {
    // The access token expired. A failed refresh throws, which is how a screen learns the session ended.
    await refreshSession()
    response = await send(method, path, options)
  }
  if (!response.ok) {
    throw new ApiError(response.status, method, path)
  }
  // Some answers, such as 204 from sign-out, have no body.
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

function keepAccess(auth: AuthResponse): Me {
  access = { token: auth.accessToken, expiresAt: Date.parse(auth.expiresAt) }
  return auth.user
}

/**
 * Swaps the refresh cookie for a new access token. Requests that fail together share one refresh,
 * since each refresh replaces the cookie the others would be sending.
 */
function refreshSession(): Promise<Me> {
  refreshing ??= request<AuthResponse>('POST', '/api/auth/refresh')
    .then(keepAccess)
    .catch((e: unknown) => {
      if (e instanceof ApiError && e.status === 401) access = null
      throw e
    })
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

/** Forgets the access token. For sign-out, and for tests. */
export function clearAccess() {
  access = null
}

/** The one place the app talks to the API. */
export const api = {
  /**
   * Finds out who is using the app after a page load: an account through the refresh cookie, or
   * else a guest through the guest cookie. 401 when there is neither.
   */
  restoreSession: async (signal?: AbortSignal): Promise<Me> => {
    try {
      return await refreshSession()
    } catch (e) {
      if (!(e instanceof ApiError && e.status === 401)) throw e
    }
    return request<Me>('GET', '/api/me', { signal })
  },
  /** A guest who calls this becomes the account and keeps their follows. 409 when the email is taken. */
  register: (email: string, password: string) =>
    request<AuthResponse>('POST', '/api/auth/register', { body: { email, password } }).then(keepAccess),
  /** 401 for a wrong email or password. */
  login: (email: string, password: string) =>
    request<AuthResponse>('POST', '/api/auth/login', { body: { email, password } }).then(keepAccess),
  /** Forgets the token even when the request fails, so the person is signed out on this device. */
  logout: () => request<void>('POST', '/api/auth/logout').finally(clearAccess),
  /** 400 when the link has expired or was already used. */
  confirmEmail: (token: string) => request<void>('POST', '/api/auth/confirm-email', { body: { token } }),
  resendConfirmation: () => request<void>('POST', '/api/auth/resend-confirmation'),
  /** For the SignalR connection, which asks each time it connects. Empty for guests. */
  getHubToken: async (): Promise<string> => {
    if (access && access.expiresAt - Date.now() < refreshMargin) {
      // If this fails the hub drops the connection, and the dashboard shows it is offline.
      await refreshSession().catch(() => {})
    }
    return access?.token ?? ''
  },
  /** 401 when there is no session yet. */
  getMe: (signal?: AbortSignal) => request<Me>('GET', '/api/me', { signal }),
  /** Sets the guest cookie. Returns the current user if there already is a session. */
  startGuest: () => request<Me>('POST', '/api/guest'),
  getDashboard: (signal?: AbortSignal) => request<Dashboard>('GET', '/api/dashboard', { signal }),
  getDemoOutage: (signal?: AbortSignal) => request<DemoOutage>('GET', '/api/demo/outage', { signal }),
  /** 409 when an outage is already running or the demo is cooling down. */
  startDemoOutage: () => request<DemoOutage>('POST', '/api/demo/outage'),
  /** 404 when no outage is running. */
  resolveDemoOutage: () => request<DemoOutage>('DELETE', '/api/demo/outage'),
}
