import { useCallback, useEffect, useRef, useState } from 'react'
import { api, ApiError } from '../../api/client'
import type { Me } from '../../api/types'

export type SessionState =
  | { kind: 'loading' }
  | { kind: 'signedOut' }
  | { kind: 'error' }
  | { kind: 'ready'; me: Me }

export interface UseSession {
  state: SessionState
  /** True while the request behind "Try it as a guest" is on its way. */
  starting: boolean
  /** Why the last guest start failed, in words, or empty. */
  startError: string
  startGuest: () => void
  /** Asks the server who we are again, after a failed load. */
  retry: () => void
  /** For screens that get a 401 later on, when the guest was deleted or the cookie is gone. */
  markSignedOut: () => void
}

interface Options {
  load?: (signal: AbortSignal) => Promise<Me>
  start?: () => Promise<Me>
}

/** Who is using the app. Screens that need a user only render once this is ready. */
export function useSession({ load = api.getMe, start = api.startGuest }: Options = {}): UseSession {
  const [state, setState] = useState<SessionState>({ kind: 'loading' })
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState('')
  const requestRef = useRef<AbortController | null>(null)

  const reload = useCallback(() => {
    requestRef.current?.abort()
    const controller = new AbortController()
    requestRef.current = controller
    load(controller.signal)
      .then((me) => !controller.signal.aborted && setState({ kind: 'ready', me }))
      .catch((e: unknown) => {
        if (controller.signal.aborted) return
        setState(e instanceof ApiError && e.status === 401 ? { kind: 'signedOut' } : { kind: 'error' })
      })
  }, [load])

  useEffect(() => {
    reload()
    return () => requestRef.current?.abort()
  }, [reload])

  const startGuest = useCallback(() => {
    setStarting(true)
    setStartError('')
    start()
      .then((me) => setState({ kind: 'ready', me }))
      .catch((e: unknown) => {
        setStartError(
          e instanceof ApiError && e.status === 429
            ? 'Too many guest sessions from this address. Try again later.'
            : "We couldn't reach the server. Try again.",
        )
      })
      .finally(() => setStarting(false))
  }, [start])

  const retry = useCallback(() => {
    setState({ kind: 'loading' })
    reload()
  }, [reload])

  const markSignedOut = useCallback(() => setState({ kind: 'signedOut' }), [])

  return { state, starting, startError, startGuest, retry, markSignedOut }
}
