import { useEffect } from 'react'
import { ApiError } from '../../api/client'
import { EmailBanner } from '../session/EmailBanner'
import { GuestBanner } from '../session/GuestBanner'
import { LiveIndicator } from './LiveIndicator'
import { OutageBanner } from './OutageBanner'
import { StatTiles } from './StatTiles'
import { useDashboard, type UseDashboard } from './useDashboard'
import { useDemoOutage, type UseDemoOutage } from './useDemoOutage'
import { useNow } from './useNow'
import { VendorCardView } from './VendorCardView'
import './Dashboard.css'

interface PageProps {
  isGuest: boolean
  /** Set for an account whose email isn't confirmed yet. */
  unconfirmedEmail?: string | null
  onSignedOut: () => void
  onCreateAccount?: () => void
}

export function DashboardPage({ isGuest, unconfirmedEmail, onSignedOut, onCreateAccount }: PageProps) {
  const dashboard = useDashboard()
  const now = useNow(5_000)
  const demoCard = dashboard.state.kind === 'ready' ? dashboard.state.vendors.find((v) => v.isDemo) : undefined
  const demo = useDemoOutage(demoCard?.status)

  const signedOut = dashboard.state.kind === 'error' && isUnauthorized(dashboard.state.error)
  useEffect(() => {
    if (signedOut) onSignedOut()
  }, [signedOut, onSignedOut])

  return (
    <DashboardView
      {...dashboard}
      now={now}
      demo={demo}
      isGuest={isGuest}
      unconfirmedEmail={unconfirmedEmail}
      onCreateAccount={onCreateAccount}
    />
  )
}

function isUnauthorized(error: unknown): boolean {
  return error instanceof ApiError && error.status === 401
}

/** The screen itself, with no connection or timers, so tests can render any state directly. */
export function DashboardView({
  state,
  live,
  announcement,
  retry,
  now,
  demo,
  isGuest = false,
  unconfirmedEmail,
  onCreateAccount,
}: UseDashboard & {
  now: number
  demo?: UseDemoOutage
  isGuest?: boolean
  unconfirmedEmail?: string | null
  onCreateAccount?: () => void
}) {
  return (
    <main className="dashboard">
      {isGuest && <GuestBanner onCreateAccount={onCreateAccount} />}
      {unconfirmedEmail && <EmailBanner email={unconfirmedEmail} />}
      {state.kind === 'ready' && <OutageBanner vendors={state.vendors} />}

      <div className="dashboard__top">
        <div>
          <h1>Your services</h1>
          {state.kind === 'ready' && <LiveIndicator live={live} onRetry={retry} />}
        </div>
      </div>

      {/* Announces live changes, such as "Acme Cloud (demo): Major outage", to screen readers. */}
      <p className="visually-hidden" aria-live="polite">
        {announcement}
      </p>

      {state.kind === 'loading' && <p className="dashboard__message">Loading your services...</p>}

      {state.kind === 'error' && <LoadError error={state.error} onRetry={retry} />}

      {state.kind === 'ready' && state.vendors.length === 0 && (
        <div className="dashboard__message">
          <p>You aren't following any services yet.</p>
        </div>
      )}

      {state.kind === 'ready' && state.vendors.length > 0 && (
        <>
          <StatTiles vendors={state.vendors} />
          <div className="dashboard__cards">
            {state.vendors.map((card) => (
              <VendorCardView key={card.id} card={card} now={now} demo={demo} />
            ))}
          </div>
        </>
      )}
    </main>
  )
}

function LoadError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  if (isUnauthorized(error)) {
    return (
      <div className="dashboard__message" role="alert">
        <p>You need to be signed in to see your dashboard.</p>
      </div>
    )
  }
  return (
    <div className="dashboard__message" role="alert">
      <p>We couldn't load your dashboard.</p>
      <button type="button" className="button" onClick={onRetry}>
        Try again
      </button>
    </div>
  )
}
