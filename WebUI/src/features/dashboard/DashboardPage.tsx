import { ApiError } from '../../api/client'
import { LiveIndicator } from './LiveIndicator'
import { StatTiles } from './StatTiles'
import { useDashboard, type UseDashboard } from './useDashboard'
import { useNow } from './useNow'
import { VendorCardView } from './VendorCardView'
import './Dashboard.css'

export function DashboardPage() {
  const dashboard = useDashboard()
  const now = useNow(5_000)
  return <DashboardView {...dashboard} now={now} />
}

/** The screen itself, with no connection or timers, so tests can render any state directly. */
export function DashboardView({ state, live, announcement, retry, now }: UseDashboard & { now: number }) {
  return (
    <main className="dashboard">
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
              <VendorCardView key={card.id} card={card} now={now} />
            ))}
          </div>
        </>
      )}
    </main>
  )
}

function LoadError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  if (error instanceof ApiError && error.status === 401) {
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
