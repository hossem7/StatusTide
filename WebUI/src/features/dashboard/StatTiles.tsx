import type { VendorCard } from '../../api/types'
import { statsOf } from './format'

export function StatTiles({ vendors }: { vendors: VendorCard[] }) {
  const stats = statsOf(vendors)
  return (
    <dl className="stat-tiles">
      <div className="stat-tile">
        <dt>Services followed</dt>
        <dd>{stats.followed}</dd>
      </div>
      <div className="stat-tile" data-tone="operational">
        <dt>Operational</dt>
        <dd>{stats.operational}</dd>
      </div>
      <div className="stat-tile" data-tone={stats.activeIncidents > 0 ? 'outage' : undefined}>
        <dt>Active incidents</dt>
        <dd>{stats.activeIncidents}</dd>
      </div>
    </dl>
  )
}
