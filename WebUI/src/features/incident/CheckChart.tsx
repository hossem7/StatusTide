import type { IncidentChecks } from '../../api/types'
import { formatTime } from '../dashboard/format'
import { chartSummary, sameMoment } from './incidentFormat'

/** One minute, the time between our checks, so each bar is one slot wide. */
const slotMs = 60_000

/**
 * Our checks, one bar a minute. Bars are placed by time, not by count, so a gap in the checks
 * shows as a gap and the vendor's post lines up with the bars around it. Passing checks are short
 * bars whose height follows the response time; failed checks are full height.
 */
export function CheckChart({ checks }: { checks: IncidentChecks }) {
  const from = Date.parse(checks.from)
  // One extra slot, so a check right at the end still has room for its bar.
  const span = Math.max(0, Date.parse(checks.to) - from) + slotMs
  const at = (iso: string) => ((Date.parse(iso) - from) / span) * 100
  const width = (slotMs / span) * 100

  const slowest = Math.max(1, ...checks.points.filter((p) => p.succeeded).map((p) => p.responseTimeMs ?? 0))
  const outage = checks.outage
  const postAt = outage ? at(outage.vendorPostedAt) : null
  const middle = new Date((from + Date.parse(checks.to)) / 2).toISOString()

  return (
    <div className="check-chart">
      <div className="check-chart__plot" role="img" aria-label={chartSummary(checks, formatTime)}>
        {checks.points.map((point) => {
          const kind = !point.succeeded ? (outage && sameMoment(point.checkedAt, outage.detectedAt) ? 'detected' : 'failed') : 'passed'
          const height = point.succeeded ? 12 + 18 * Math.min(1, (point.responseTimeMs ?? 0) / slowest) : 100
          return (
            <span
              key={point.checkedAt}
              className="check-chart__bar"
              data-kind={kind}
              style={{ left: `${at(point.checkedAt)}%`, width: `${width}%`, height: `${height}%` }}
            />
          )
        })}
        {postAt !== null && postAt >= 0 && postAt <= 100 && (
          <span className="check-chart__post" style={{ left: `${postAt}%` }} />
        )}
      </div>
      <div className="check-chart__axis" aria-hidden="true">
        <span>{formatTime(checks.from)}</span>
        <span>{formatTime(middle)}</span>
        <span>{formatTime(checks.to)}</span>
      </div>
    </div>
  )
}
