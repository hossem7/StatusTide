import type { Dashboard, DemoOutage } from './types'

/** A response other than 2xx. `status` lets screens tell "not signed in" from "server down". */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, method: string, path: string) {
    super(`${method} ${path} returned ${status}`)
    this.name = 'ApiError'
    this.status = status
  }
}

async function request<T>(method: string, path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    method,
    headers: { Accept: 'application/json' },
    credentials: 'same-origin',
    signal,
  })
  if (!response.ok) {
    throw new ApiError(response.status, method, path)
  }
  return (await response.json()) as T
}

/** The one place the app talks to the API. */
export const api = {
  getDashboard: (signal?: AbortSignal) => request<Dashboard>('GET', '/api/dashboard', signal),
  getDemoOutage: (signal?: AbortSignal) => request<DemoOutage>('GET', '/api/demo/outage', signal),
  /** 409 when an outage is already running or the demo is cooling down. */
  startDemoOutage: () => request<DemoOutage>('POST', '/api/demo/outage'),
  /** 404 when no outage is running. */
  resolveDemoOutage: () => request<DemoOutage>('DELETE', '/api/demo/outage'),
}
