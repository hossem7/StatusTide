import { formatWait } from './format'
import type { UseDemoOutage } from './useDemoOutage'

/** "Simulate an outage" and "Resolve the outage" on the demo card. The outage is shared. */
export function DemoControls({ demo, now }: { demo: UseDemoOutage; now: number }) {
  const { outage, busy, error, start, resolve } = demo
  const active = outage?.state === 'active'
  const coolingDown = outage?.state === 'coolingDown' && outage.nextStartAt !== null
  // Acme posts a few minutes in, like many real vendors, so the cards don't change at first.
  const waitingForPost = active && outage.postsAt !== null && Date.parse(outage.postsAt) > now

  return (
    <div className="demo-controls">
      <button
        type="button"
        className="button"
        onClick={active ? resolve : start}
        disabled={outage === null || busy || coolingDown}
      >
        {active ? 'Resolve the outage' : 'Simulate an outage'}
      </button>
      {coolingDown && (
        <p className="demo-controls__note">Ready again {formatWait(outage.nextStartAt!, now)}</p>
      )}
      {waitingForPost && (
        <p className="demo-controls__note">
          Acme posts about it {formatWait(outage.postsAt!, now)}. Our own checks are already watching.
        </p>
      )}
      {active && !waitingForPost && <p className="demo-controls__note">Shared with everyone trying the demo</p>}
      {error && (
        <p className="demo-controls__error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
