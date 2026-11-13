import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../../api/client'
import type { Incident } from '../../api/types'

export type IncidentState =
  | { kind: 'loading' }
  | { kind: 'ready'; incident: Incident }
  | { kind: 'error'; error: unknown }

export interface UseIncident {
  state: IncidentState
  retry: () => void
}

/** How often an open incident is loaded again. Our checks run once a minute, so this catches each one. */
export const refreshMs = 30_000

interface Options {
  load?: (incidentId: string, signal: AbortSignal) => Promise<Incident>
}

/**
 * Loads one incident, then loads it again every 30 seconds while it is open, so new checks and
 * vendor updates appear without a reload. A failed refresh keeps what is on screen. Resolved
 * incidents don't change, so they are loaded once.
 */
export function useIncident(incidentId: string, { load = api.getIncident }: Options = {}): UseIncident {
  const [state, setState] = useState<IncidentState>({ kind: 'loading' })
  const requestRef = useRef<AbortController | null>(null)

  const fetchIncident = useCallback(
    (isRefresh: boolean) => {
      requestRef.current?.abort()
      const controller = new AbortController()
      requestRef.current = controller
      load(incidentId, controller.signal)
        .then((incident) => {
          if (!controller.signal.aborted) setState({ kind: 'ready', incident })
        })
        .catch((error: unknown) => {
          if (!controller.signal.aborted && !isRefresh) setState({ kind: 'error', error })
        })
    },
    [incidentId, load],
  )

  useEffect(() => {
    fetchIncident(false)
    return () => requestRef.current?.abort()
  }, [fetchIncident])

  const isOpen = state.kind === 'ready' && state.incident.resolvedAt === null
  useEffect(() => {
    if (!isOpen) return
    const timer = setInterval(() => fetchIncident(true), refreshMs)
    return () => clearInterval(timer)
  }, [isOpen, fetchIncident])

  const retry = useCallback(() => {
    setState({ kind: 'loading' })
    fetchIncident(false)
  }, [fetchIncident])

  return { state, retry }
}
