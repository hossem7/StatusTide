import type { VendorCard } from '../../api/types'
import { statusLabel } from '../../components/statusLabels'
import { formatTime } from './format'

const isDown = (card: VendorCard) => card.status === 'majorOutage' || card.status === 'partialOutage'

/**
 * A banner above the dashboard while a followed vendor is down. The live region below the
 * heading already announces the change, so this is not a second alert.
 */
export function OutageBanner({ vendors }: { vendors: VendorCard[] }) {
  const down = vendors.filter(isDown)
  if (down.length === 0) return null

  const [first, ...others] = down
  const incident = first.activeIncident
  return (
    <section className="outage-banner" aria-label="Outages">
      <p>
        <b>
          {first.name}: {statusLabel(first.status).toLowerCase()}.
        </b>{' '}
        {/* The vendor's own title: React renders it as text, never as HTML. */}
        {incident && `${incident.title}, since ${formatTime(incident.startedAt)}.`}
        {others.length === 1 && ` ${others[0].name} is down too.`}
        {others.length > 1 && ` ${others.length} other services are down too.`}
      </p>
    </section>
  )
}
