import { formatWait } from './format'
import type { UseDemoOutage } from './useDemoOutage'

/** "Simulate an outage" and "Resolve the outage" on the demo card. The outage is shared. */
export function DemoControls({ demo, now }: { demo: UseDemoOutage; now: number }) {
  const { outage, busy, error, start, resolve } = demo
  const active = outage?.state === 'active'
  const coolingDown = outage?.state === 'coolingDown' && outage.nextStartAt !== null

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
      {active && <p className="demo-controls__note">Shared with everyone trying the demo</p>}
      {error && (
        <p className="demo-controls__error" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
