import type { LiveState } from './useDashboard'

const labels: Record<LiveState, string> = {
  connecting: 'Connecting to live updates',
  live: 'Live, updates appear without refreshing',
  reconnecting: 'Reconnecting, updates are paused',
  offline: 'Not live, updates are paused',
}

export function LiveIndicator({ live, onRetry }: { live: LiveState; onRetry: () => void }) {
  return (
    <span className="live-indicator" data-live={live}>
      <span className="live-indicator__dot" aria-hidden="true" />
      {labels[live]}
      {live === 'offline' && (
        <button type="button" className="live-indicator__retry" onClick={onRetry}>
          Reconnect
        </button>
      )}
    </span>
  )
}
