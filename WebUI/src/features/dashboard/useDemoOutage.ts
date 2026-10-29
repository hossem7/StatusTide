import { useCallback, useEffect, useRef, useState } from 'react'
import { api, ApiError } from '../../api/client'
import type { ComponentStatus, DemoOutage } from '../../api/types'

export interface UseDemoOutage {
  /** Null until the first load finishes. */
  outage: DemoOutage | null
  /** True while a start or resolve request is on its way. */
  busy: boolean
  /** Why the last click failed, in words, or empty. */
  error: string
  start: () => void
  resolve: () => void
}

interface Options {
  load?: (signal: AbortSignal) => Promise<DemoOutage>
  startOutage?: () => Promise<DemoOutage>
  resolveOutage?: () => Promise<DemoOutage>
}

/** The next moment the outage changes state by itself: it resolves, or the cooldown ends. */
function nextChangeAt(outage: DemoOutage): string | null {
  if (outage.state === 'active') return outage.resolvesAt
  if (outage.state === 'coolingDown') return outage.nextStartAt
  return null
}

/**
 * Drives the "Simulate an outage" button on the demo card. The outage is shared by every
 * visitor, so it is loaded again whenever the demo card's status changes (someone else may
 * have started or resolved it) and when it is due to resolve or finish cooling down.
 * Pass the demo card's status, or undefined when the user doesn't follow the demo vendor.
 */
export function useDemoOutage(
  demoStatus: ComponentStatus | undefined,
  {
    load = api.getDemoOutage,
    startOutage = api.startDemoOutage,
    resolveOutage = api.resolveDemoOutage,
  }: Options = {},
): UseDemoOutage {
  const [outage, setOutage] = useState<DemoOutage | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const requestRef = useRef<AbortController | null>(null)
  const following = demoStatus !== undefined

  const reload = useCallback(() => {
    requestRef.current?.abort()
    const controller = new AbortController()
    requestRef.current = controller
    load(controller.signal)
      .then((loaded) => !controller.signal.aborted && setOutage(loaded))
      // A failed load keeps the last known state; the next status change loads it again.
      .catch(() => {})
  }, [load])

  useEffect(() => {
    if (following) reload()
    return () => requestRef.current?.abort()
  }, [following, demoStatus, reload])

  const changeAt = outage ? nextChangeAt(outage) : null
  useEffect(() => {
    if (!following || changeAt === null) return
    // A second late, so the server agrees the time has passed.
    const delay = Math.max(0, Date.parse(changeAt) - Date.now()) + 1_000
    const timer = setTimeout(reload, delay)
    return () => clearTimeout(timer)
  }, [following, changeAt, reload])

  const send = useCallback(
    (action: () => Promise<DemoOutage>) => {
      setBusy(true)
      setError('')
      action()
        .then(setOutage)
        .catch((e: unknown) => {
          if (e instanceof ApiError && (e.status === 404 || e.status === 409)) {
            // Someone else started or resolved it first. Show what is true now.
            reload()
          } else if (e instanceof ApiError && e.status === 429) {
            setError('Too many tries. Wait a minute and try again.')
          } else {
            setError("We couldn't reach the server. Try again.")
          }
        })
        .finally(() => setBusy(false))
    },
    [reload],
  )

  const start = useCallback(() => send(startOutage), [send, startOutage])
  const resolve = useCallback(() => send(resolveOutage), [send, resolveOutage])

  return { outage, busy, error, start, resolve }
}
