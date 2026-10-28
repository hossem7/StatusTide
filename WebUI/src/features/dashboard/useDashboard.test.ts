import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { VendorCard } from '../../api/types'
import { FakeConnection } from '../../test/fakeConnection'
import { acme, acmeDown, nimbus } from '../../test/sampleCards'
import { applyUpdate, useDashboard } from './useDashboard'

function setup(load: () => Promise<{ vendors: VendorCard[] }>) {
  const connection = new FakeConnection()
  const loadSpy = vi.fn(load)
  const connect = () => connection
  const hook = renderHook(() => useDashboard({ load: loadSpy, connect }))
  return { connection, load: loadSpy, ...hook }
}

const vendorsOf = (state: ReturnType<typeof useDashboard>['state']) =>
  state.kind === 'ready' ? state.vendors : undefined

describe('useDashboard', () => {
  it('loads the snapshot and goes live', async () => {
    const { result } = setup(async () => ({ vendors: [acme, nimbus] }))

    await waitFor(() => expect(result.current.live).toBe('live'))
    expect(vendorsOf(result.current.state)).toEqual([acme, nimbus])
  })

  it('replaces a card in place when the hub sends an update and announces the new status', async () => {
    const { result, connection } = setup(async () => ({ vendors: [nimbus, acme] }))
    await waitFor(() => expect(result.current.state.kind).toBe('ready'))

    act(() => connection.emitUpdate(acmeDown))

    expect(vendorsOf(result.current.state)).toEqual([nimbus, acmeDown])
    expect(result.current.announcement).toBe('Acme Cloud (demo): Major outage')
  })

  it('does not announce an update that keeps the same status', async () => {
    const { result, connection } = setup(async () => ({ vendors: [acme] }))
    await waitFor(() => expect(result.current.state.kind).toBe('ready'))

    act(() => connection.emitUpdate({ ...acme, lastCheckedAt: '2026-11-02T12:05:00Z' }))

    expect(result.current.announcement).toBe('')
  })

  it('applies updates that arrive while the snapshot is loading', async () => {
    let finish!: (value: { vendors: VendorCard[] }) => void
    const { result, connection } = setup(() => new Promise((resolve) => (finish = resolve)))

    act(() => connection.emitUpdate(acmeDown))
    await act(async () => finish({ vendors: [acme, nimbus] }))

    expect(vendorsOf(result.current.state)).toEqual([acmeDown, nimbus])
  })

  it('shows reconnecting, then reloads the snapshot once the connection is back', async () => {
    const { result, connection, load } = setup(async () => ({ vendors: [acme] }))
    await waitFor(() => expect(result.current.live).toBe('live'))

    act(() => connection.drop())
    expect(result.current.live).toBe('reconnecting')

    load.mockResolvedValueOnce({ vendors: [acmeDown] })
    act(() => connection.restore())

    expect(result.current.live).toBe('live')
    await waitFor(() => expect(vendorsOf(result.current.state)).toEqual([acmeDown]))
    expect(load).toHaveBeenCalledTimes(2)
  })

  it('goes offline when reconnecting gives up, and retry starts again', async () => {
    const { result, connection } = setup(async () => ({ vendors: [acme] }))
    await waitFor(() => expect(result.current.live).toBe('live'))

    act(() => connection.close())
    expect(result.current.live).toBe('offline')

    act(() => result.current.retry())
    await waitFor(() => expect(result.current.live).toBe('live'))
    expect(connection.started).toBe(2)
  })

  it('still shows the snapshot when the connection cannot start', async () => {
    const connection = new FakeConnection()
    connection.failStart = true
    const load = async () => ({ vendors: [acme] })
    const connect = () => connection
    const { result } = renderHook(() => useDashboard({ load, connect }))

    await waitFor(() => expect(result.current.live).toBe('offline'))
    expect(vendorsOf(result.current.state)).toEqual([acme])
  })

  it('reports a failed load, and keeps the cards when a later reload fails', async () => {
    const { result, load, connection } = setup(() => Promise.reject(new ApiError(500, '/api/dashboard')))
    await waitFor(() => expect(result.current.state.kind).toBe('error'))

    load.mockResolvedValueOnce({ vendors: [acme] })
    act(() => result.current.retry())
    await waitFor(() => expect(vendorsOf(result.current.state)).toEqual([acme]))

    load.mockRejectedValueOnce(new Error('network'))
    act(() => connection.restore())
    await waitFor(() => expect(load).toHaveBeenCalledTimes(3))
    expect(vendorsOf(result.current.state)).toEqual([acme])
  })

  it('stops the connection on unmount', async () => {
    const { result, connection, unmount } = setup(async () => ({ vendors: [] }))
    await waitFor(() => expect(result.current.live).toBe('live'))

    unmount()

    expect(connection.stopped).toBe(1)
  })
})

describe('applyUpdate', () => {
  it('adds a card it has not seen', () => {
    expect(applyUpdate([acme], nimbus)).toEqual([acme, nimbus])
  })
})
