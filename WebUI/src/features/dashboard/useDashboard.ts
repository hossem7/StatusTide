import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../../api/client'
import { createStatusConnection, type StatusConnection } from '../../api/statusConnection'
import type { VendorCard } from '../../api/types'
import { statusLabel } from '../../components/statusLabels'

export type LiveState = 'connecting' | 'live' | 'reconnecting' | 'offline'

export type DashboardState =
  | { kind: 'loading' }
  | { kind: 'error'; error: unknown }
  | { kind: 'ready'; vendors: VendorCard[] }

export interface UseDashboard {
  state: DashboardState
  live: LiveState
  /** The latest live change in words, for the screen reader announcement. */
  announcement: string
  /** Loads the snapshot again and reconnects if the connection is gone. */
  retry: () => void
}

interface Options {
  load?: (signal: AbortSignal) => Promise<{ vendors: VendorCard[] }>
  connect?: () => StatusConnection
}

/** Replaces the card in place, so cards don't jump around while someone is reading them. */
export function applyUpdate(vendors: VendorCard[], card: VendorCard): VendorCard[] {
  const index = vendors.findIndex((v) => v.id === card.id)
  if (index === -1) {
    return [...vendors, card]
  }
  return vendors.with(index, card)
}

/**
 * Loads the dashboard and keeps it current. The connection starts before the snapshot loads,
 * so no change can fall between the two; updates that arrive while the snapshot is loading are
 * applied on top of it. After a reconnect the snapshot is loaded again, since updates sent
 * while we were away are lost.
 */
export function useDashboard({ load = api.getDashboard, connect = createStatusConnection }: Options = {}): UseDashboard {
  const [state, setState] = useState<DashboardState>({ kind: 'loading' })
  const [live, setLive] = useState<LiveState>('connecting')
  const [announcement, setAnnouncement] = useState('')
  const reloadRef = useRef<() => void>(() => {})
  const startRef = useRef<() => void>(() => {})

  useEffect(() => {
    let disposed = false
    let pending: VendorCard[] | null = []
    let request: AbortController | null = null
    // The cards on screen, so a live update can tell whether the status changed.
    let shown: VendorCard[] | null = null
    const connection = connect()

    const show = (vendors: VendorCard[]) => {
      shown = vendors
      setState({ kind: 'ready', vendors })
    }

    const reload = () => {
      request?.abort()
      const controller = new AbortController()
      request = controller
      load(controller.signal)
        .then((dashboard) => {
          if (disposed || controller.signal.aborted) return
          const buffered = pending ?? []
          pending = null
          show(buffered.reduce(applyUpdate, dashboard.vendors))
        })
        .catch((error: unknown) => {
          if (disposed || controller.signal.aborted) return
          // A failed reload keeps the cards we already have on screen.
          if (!shown) setState({ kind: 'error', error })
        })
    }

    const start = () => {
      setLive('connecting')
      connection.start().then(
        () => !disposed && setLive('live'),
        () => !disposed && setLive('offline'),
      )
    }

    connection.onVendorUpdated((card) => {
      if (disposed) return
      if (pending || !shown) {
        pending?.push(card)
        return
      }
      const before = shown.find((v) => v.id === card.id)
      if (before?.status !== card.status) {
        setAnnouncement(`${card.name}: ${statusLabel(card.status)}`)
      }
      show(applyUpdate(shown, card))
    })
    connection.onReconnecting(() => !disposed && setLive('reconnecting'))
    connection.onReconnected(() => {
      if (disposed) return
      setLive('live')
      reload()
    })
    connection.onClose(() => !disposed && setLive('offline'))

    reloadRef.current = reload
    startRef.current = start
    start()
    reload()

    return () => {
      disposed = true
      request?.abort()
      void connection.stop().catch(() => {})
    }
  }, [load, connect])

  const retry = useCallback(() => {
    setState((current) => (current.kind === 'error' ? { kind: 'loading' } : current))
    reloadRef.current()
    if (live === 'offline') {
      startRef.current()
    }
  }, [live])

  return { state, live, announcement, retry }
}
