import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError } from './client'

afterEach(() => vi.unstubAllGlobals())

describe('api.getDashboard', () => {
  it('returns the parsed body', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ vendors: [] })))

    await expect(api.getDashboard()).resolves.toEqual({ vendors: [] })
    expect(fetch).toHaveBeenCalledWith('/api/dashboard', expect.objectContaining({ credentials: 'same-origin' }))
  })

  it('throws an ApiError with the status code', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 401 })))

    const error = await api.getDashboard().catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(401)
  })
})

describe('demo outage calls', () => {
  it.each([
    ['startDemoOutage', 'POST'],
    ['resolveDemoOutage', 'DELETE'],
  ] as const)('%s sends %s to /api/demo/outage', async (call, method) => {
    const body = { state: 'active', startedAt: null, resolvesAt: null, nextStartAt: null }
    vi.stubGlobal('fetch', vi.fn(async () => Response.json(body, { status: method === 'POST' ? 202 : 200 })))

    await expect(api[call]()).resolves.toEqual(body)
    expect(fetch).toHaveBeenCalledWith('/api/demo/outage', expect.objectContaining({ method }))
  })

  it('reports a conflict as an ApiError', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 409 })))

    const error = await api.startDemoOutage().catch((e: unknown) => e)

    expect((error as ApiError).status).toBe(409)
    expect((error as ApiError).message).toBe('POST /api/demo/outage returned 409')
  })
})
