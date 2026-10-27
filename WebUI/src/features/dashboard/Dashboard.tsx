import ActivityFeed from './ActivityFeed'
import StatusSummary from './StatusSummary'
import VendorCard from './VendorCard'
import type { ActivityEvent, Vendor } from './types'
import './Dashboard.css'

interface DashboardProps {
  vendors: Vendor[]
  activity: ActivityEvent[]
}

function Dashboard({ vendors, activity }: DashboardProps) {
  return (
    <section className="dashboard">
      <StatusSummary vendors={vendors} />
      <h2 className="dashboard__heading">Services</h2>
      <div className="dashboard__grid">
        {vendors.map((vendor) => (
          <VendorCard key={vendor.id} vendor={vendor} />
        ))}
      </div>
      <ActivityFeed activity={activity} />

      <p className="dashboard__footnote">
        Statuses come from each vendor's public status page or feed, with a link
        back to the source. StatusTide is independent and not affiliated with any
        vendor it lists.
      </p>
    </section>
  )
}

export default Dashboard
