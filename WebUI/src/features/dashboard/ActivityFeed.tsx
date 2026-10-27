import StatusBadge from '../../components/StatusBadge'
import type { ActivityEvent } from './types'
import './ActivityFeed.css'

interface ActivityFeedProps {
  activity: ActivityEvent[]
}

// A short relative time like "2 min ago" or "3 hr ago", from an ISO timestamp.
function relativeTime(iso: string): string {
  const then = new Date(iso).getTime()
  const seconds = Math.round((Date.now() - then) / 1000)

  if (seconds < 60) return 'just now'
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours} hr ago`
  const days = Math.round(hours / 24)
  return `${days} day${days === 1 ? '' : 's'} ago`
}

function ActivityFeed({ activity }: ActivityFeedProps) {
  return (
    <section className="activity-feed">
      <h2 className="activity-feed__heading">Recent activity</h2>
      {activity.length === 0 ? (
        <p className="activity-feed__empty">No recent changes.</p>
      ) : (
        <ul className="activity-feed__list">
          {activity.map((event) => (
            <li key={event.id} className="activity-feed__item">
              <div className="activity-feed__text">
                <strong>{event.vendorName}</strong> {event.componentName}
              </div>
              <StatusBadge status={event.status} />
              <time className="activity-feed__time" dateTime={event.at}>
                {relativeTime(event.at)}
              </time>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

export default ActivityFeed
