import StatusBadge from '../../components/StatusBadge'
import type { ComponentStatus } from '../../types/status'
import type { Vendor } from './types'
import { vendorStatus } from './vendorStatus'
import './StatusSummary.css'

interface StatusSummaryProps {
  vendors: Vendor[]
}

// Statuses worth calling out, in order of severity. Operational and Unknown
// are left out of the summary tiles: the healthy case needs no alarm, and
// Unknown is shown on the card itself.
const summaryStatuses: ComponentStatus[] = [
  'MajorOutage',
  'PartialOutage',
  'DegradedPerformance',
  'UnderMaintenance',
]

function countByStatus(vendors: Vendor[]): Record<ComponentStatus, number> {
  const counts = {
    Operational: 0,
    DegradedPerformance: 0,
    PartialOutage: 0,
    MajorOutage: 0,
    UnderMaintenance: 0,
    Unknown: 0,
  } satisfies Record<ComponentStatus, number>

  for (const vendor of vendors) {
    counts[vendorStatus(vendor)] += 1
  }

  return counts
}

function StatusSummary({ vendors }: StatusSummaryProps) {
  const counts = countByStatus(vendors)
  const allOperational = vendors.length > 0 && counts.Operational === vendors.length

  return (
    <div className="status-summary">
      <div className="status-summary__tile">
        <span className="status-summary__count">{vendors.length}</span>
        <span className="status-summary__label">Services tracked</span>
      </div>

      {allOperational ? (
        <div className="status-summary__tile">
          <StatusBadge status="Operational" />
          <span className="status-summary__label">All systems operational</span>
        </div>
      ) : (
        summaryStatuses
          .filter((status) => counts[status] > 0)
          .map((status) => (
            <div key={status} className="status-summary__tile">
              <span className="status-summary__count">{counts[status]}</span>
              <StatusBadge status={status} />
            </div>
          ))
      )}
    </div>
  )
}

export default StatusSummary
