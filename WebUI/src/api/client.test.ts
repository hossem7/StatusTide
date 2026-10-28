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
