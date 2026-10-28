import type { Dashboard } from './types'

/** A response other than 2xx. `status` lets screens tell "not signed in" from "server down". */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, path: string) {
    super(`GET ${path} returned ${status}`)
    this.name = 'ApiError'
    this.status = status
  }
}

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, {
    headers: { Accept: 'application/json' },
    credentials: 'same-origin',
    signal,
  })
  if (!response.ok) {
    throw new ApiError(response.status, path)
  }
  return (await response.json()) as T
}

/** The one place the app talks to the API. */
export const api = {
  getDashboard: (signal?: AbortSignal) => get<Dashboard>('/api/dashboard', signal),
}
