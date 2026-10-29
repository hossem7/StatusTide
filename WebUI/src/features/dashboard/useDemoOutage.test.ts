import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { ComponentStatus, DemoOutage } from '../../api/types'
import { activeOutage, coolingDown, noOutage } from '../../test/sampleCards'
import { useDemoOutage } from './useDemoOutage'

afterEach(() => vi.useRealTimers())

function setup(
  {
    load = async () => noOutage,
    startOutage = async () => activeOutage,
    resolveOutage = async () => coolingDown,
  }: {
    load?: () => Promise<DemoOutage>
    startOutage?: () => Promise<DemoOutage>
    resolveOutage?: () => Promise<DemoOutage>
  } = {},
  // Null stands for "not following the demo vendor", since undefined would take the default.
  status: ComponentStatus | null = 'operational',
) {
  const spies = { load: vi.fn(load), startOutage: vi.fn(startOutage), resolveOutage: vi.fn(resolveOutage) }
  const hook = renderHook(({ s }) => useDemoOutage(s, spies), {
    initialProps: { s: status ?? undefined },
  })
  return { ...spies, ...hook }
}

describe('useDemoOutage', () => {
  it('loads nothing when the user does not follow the demo vendor', () => {
    const { load, result } = setup({}, null)

    expect(load).not.toHaveBeenCalled()
    expect(result.current.outage).toBeNull()
  })

  it('loads the outage, then starts one', async () => {
    const { result, startOutage } = setup()
    await waitFor(() => expect(result.current.outage).toEqual(noOutage))

    act(() => result.current.start())

    expect(result.current.busy).toBe(true)
    await waitFor(() => expect(result.current.outage).toEqual(activeOutage))
    expect(result.current.busy).toBe(false)
    expect(startOutage).toHaveBeenCalledOnce()
  })

  it('loads again when the demo card status changes, since another visitor may have clicked', async () => {
    const { load, rerender } = setup()
    await waitFor(() => expect(load).toHaveBeenCalledTimes(1))

    rerender({ s: 'majorOutage' as ComponentStatus | undefined })

    await waitFor(() => expect(load).toHaveBeenCalledTimes(2))
  })

  it.each([409, 404])('shows the current state instead of an error on %i', async (status) => {
    let loaded: DemoOutage = noOutage
    const { result } = setup({
      load: async () => loaded,
      startOutage: () => Promise.reject(new ApiError(status, 'POST', '/api/demo/outage')),
    })
    await waitFor(() => expect(result.current.outage).toEqual(noOutage))

    loaded = activeOutage
    act(() => result.current.start())

    await waitFor(() => expect(result.current.outage).toEqual(activeOutage))
    expect(result.current.error).toBe('')
  })

  it('explains a rate limit', async () => {
    const { result } = setup({
      startOutage: () => Promise.reject(new ApiError(429, 'POST', '/api/demo/outage')),
    })
    await waitFor(() => expect(result.current.outage).not.toBeNull())

    act(() => result.current.start())

    await waitFor(() => expect(result.current.error).toBe('Too many tries. Wait a minute and try again.'))
  })

  it('loads again once the cooldown ends', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] })
    vi.setSystemTime(Date.parse('2026-11-02T12:06:00Z'))
    const { load } = setup({ load: async () => coolingDown })
    await act(async () => {})
    expect(load).toHaveBeenCalledTimes(1)

    // Cooldown ends at 12:07, a minute away, and the hook waits one extra second.
    await act(async () => vi.advanceTimersByTime(60_000))
    expect(load).toHaveBeenCalledTimes(1)
    await act(async () => vi.advanceTimersByTime(1_000))
    expect(load).toHaveBeenCalledTimes(2)
  })
})
