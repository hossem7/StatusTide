import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { Incident } from '../../api/types'
import { incidentId, openIncident, resolvedIncident } from '../../test/sampleIncident'
import { refreshMs, useIncident } from './useIncident'

afterEach(() => vi.useRealTimers())

function setup(load: () => Promise<Incident>) {
  const spy = vi.fn(load)
  const hook = renderHook(() => useIncident(incidentId, { load: spy }))
  return { load: spy, ...hook }
}

describe('useIncident', () => {
  it('loads the incident by its id', async () => {
    const { load, result } = setup(async () => openIncident)

    await waitFor(() => expect(result.current.state).toEqual({ kind: 'ready', incident: openIncident }))
    expect(load).toHaveBeenCalledWith(incidentId, expect.any(AbortSignal))
  })

  it('loads an open incident again every 30 seconds', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const { load } = setup(async () => openIncident)
    await act(async () => {})

    await act(async () => vi.advanceTimersByTime(refreshMs))

    expect(load).toHaveBeenCalledTimes(2)
  })

  it('loads a resolved incident once, since it no longer changes', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const { load } = setup(async () => resolvedIncident)
    await act(async () => {})

    await act(async () => vi.advanceTimersByTime(refreshMs * 3))

    expect(load).toHaveBeenCalledTimes(1)
  })

  it('keeps the page when a refresh fails', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    let fail = false
    const { result } = setup(async () => {
      if (fail) throw new TypeError('Failed to fetch')
      return openIncident
    })
    await act(async () => {})

    fail = true
    await act(async () => vi.advanceTimersByTime(refreshMs))

    expect(result.current.state).toEqual({ kind: 'ready', incident: openIncident })
  })

  it('reports a failed first load, then loads again on retry', async () => {
    let error: unknown = new ApiError(500, 'GET', '/api/incidents')
    const { result } = setup(async () => {
      if (error) throw error
      return openIncident
    })
    await waitFor(() => expect(result.current.state.kind).toBe('error'))

    error = null
    act(() => result.current.retry())

    await waitFor(() => expect(result.current.state.kind).toBe('ready'))
  })
})
