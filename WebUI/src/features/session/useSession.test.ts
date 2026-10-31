import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { Me } from '../../api/types'
import { guest } from '../../test/sampleCards'
import { useSession } from './useSession'

const unauthorized = () => Promise.reject(new ApiError(401, 'GET', '/api/me'))

function setup(load: () => Promise<Me>, start: () => Promise<Me> = async () => guest) {
  const spies = { load: vi.fn(load), start: vi.fn(start) }
  return { ...spies, ...renderHook(() => useSession(spies)) }
}

describe('useSession', () => {
  it('is ready once the server knows the user', async () => {
    const { result } = setup(async () => guest)

    expect(result.current.state.kind).toBe('loading')
    await waitFor(() => expect(result.current.state).toEqual({ kind: 'ready', me: guest }))
  })

  it('treats a 401 as signed out and starts no session by itself', async () => {
    const { result, start } = setup(unauthorized)

    await waitFor(() => expect(result.current.state.kind).toBe('signedOut'))
    expect(start).not.toHaveBeenCalled()
  })

  it('starts a guest session on request', async () => {
    const { result, start } = setup(unauthorized)
    await waitFor(() => expect(result.current.state.kind).toBe('signedOut'))

    act(() => result.current.startGuest())

    expect(result.current.starting).toBe(true)
    await waitFor(() => expect(result.current.state).toEqual({ kind: 'ready', me: guest }))
    expect(result.current.starting).toBe(false)
    expect(start).toHaveBeenCalledOnce()
  })

  it.each([
    ['the rate limit', new ApiError(429, 'POST', '/api/guest'), 'Too many guest sessions'],
    ['a server error', new ApiError(500, 'POST', '/api/guest'), "We couldn't reach the server"],
    ['a network failure', new TypeError('Failed to fetch'), "We couldn't reach the server"],
  ])('stays signed out and explains %s', async (_, failure, message) => {
    const { result } = setup(unauthorized, () => Promise.reject(failure))
    await waitFor(() => expect(result.current.state.kind).toBe('signedOut'))

    act(() => result.current.startGuest())

    await waitFor(() => expect(result.current.startError).toContain(message))
    expect(result.current.state.kind).toBe('signedOut')
    expect(result.current.starting).toBe(false)
  })

  it('clears the last error when trying again', async () => {
    const start = vi.fn<() => Promise<Me>>().mockRejectedValueOnce(new TypeError('Failed to fetch'))
    start.mockResolvedValueOnce(guest)
    const { result } = setup(unauthorized, start)
    await waitFor(() => expect(result.current.state.kind).toBe('signedOut'))
    act(() => result.current.startGuest())
    await waitFor(() => expect(result.current.startError).not.toBe(''))

    act(() => result.current.startGuest())

    expect(result.current.startError).toBe('')
    await waitFor(() => expect(result.current.state.kind).toBe('ready'))
  })

  it('reports a server error, and loads again on retry', async () => {
    const load = vi.fn<() => Promise<Me>>().mockRejectedValueOnce(new ApiError(503, 'GET', '/api/me'))
    load.mockResolvedValueOnce(guest)
    const { result } = setup(load)
    await waitFor(() => expect(result.current.state.kind).toBe('error'))

    act(() => result.current.retry())

    await waitFor(() => expect(result.current.state).toEqual({ kind: 'ready', me: guest }))
  })

  it('can be told the session ended', async () => {
    const { result } = setup(async () => guest)
    await waitFor(() => expect(result.current.state.kind).toBe('ready'))

    act(() => result.current.markSignedOut())

    expect(result.current.state.kind).toBe('signedOut')
  })
})
