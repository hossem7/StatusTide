import type { VendorCard } from '../../api/types'
import { StatusPill } from '../../components/StatusPill'
import { componentSummary, formatChecked, initialsOf } from './format'

/** One followed vendor. The bottom of the demo card is where "Simulate an outage" goes (#8). */
export function VendorCardView({ card, now }: { card: VendorCard; now: number }) {
  return (
    <article className="vendor-card" data-status={card.status} aria-labelledby={`vendor-${card.id}`}>
      <div className="vendor-card__head">
        <span className="vendor-card__initials" aria-hidden="true">
          {initialsOf(card.name)}
        </span>
        <div className="vendor-card__title">
          <h2 id={`vendor-${card.id}`}>{card.name}</h2>
          <p>{componentSummary(card)}</p>
        </div>
      </div>
      <div className="vendor-card__status">
        <StatusPill status={card.status} />
        <span className="vendor-card__checked">{formatChecked(card.lastCheckedAt, now)}</span>
      </div>
      {card.activeIncident && (
        <p className="vendor-card__incident">
          {/* The vendor's own title: React renders it as text, never as HTML. */}
          {card.activeIncident.title}
          {card.activeIncidentCount > 1 && ` and ${card.activeIncidentCount - 1} more`}
        </p>
      )}
    </article>
  )
}
