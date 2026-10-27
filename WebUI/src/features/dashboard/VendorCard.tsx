import StatusBadge from '../../components/StatusBadge'
import type { Vendor } from './types'
import { followedComponents, vendorStatus } from './vendorStatus'
import './VendorCard.css'

interface VendorCardProps {
  vendor: Vendor
}

// First two initials of a vendor name, e.g. "Acme Cloud" -> "AC".
function initialsOf(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((word) => word[0]?.toUpperCase() ?? '')
    .join('')
}

function VendorCard({ vendor }: VendorCardProps) {
  const followed = followedComponents(vendor)

  return (
    <article className="vendor-card">
      <div className="vendor-card__header">
        <span className="vendor-card__initials" aria-hidden="true">
          {initialsOf(vendor.name)}
        </span>
        <div className="vendor-card__heading">
          <h2 className="vendor-card__name">{vendor.name}</h2>
          {vendor.statusPageUrl && (
            <a
              className="vendor-card__source"
              href={vendor.statusPageUrl}
              target="_blank"
              rel="noreferrer"
            >
              Status page
            </a>
          )}
        </div>
        <StatusBadge status={vendorStatus(vendor)} />
      </div>

      <ul className="vendor-card__components">
        {followed.map((component) => (
          <li key={component.id} className="vendor-card__component">
            <span>{component.name}</span>
            <StatusBadge status={component.status} />
          </li>
        ))}
      </ul>
    </article>
  )
}

export default VendorCard
