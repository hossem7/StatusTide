import type { CheckPoint, ComponentStatus, EarlyDetection, Incident, IncidentChecks, IncidentStatus } from '../../api/types'

const statusLabels: Record<IncidentStatus, string> = {
  investigating: 'Investigating',
  identified: 'Identified',
  monitoring: 'Monitoring',
  resolved: 'Resolved',
}

export function incidentStatusLabel(status: IncidentStatus): string {
  return statusLabels[status] ?? status
}

/** "Acme Cloud (demo)" becomes "Acme Cloud", for sentences like "Acme Cloud posted first". */
export function shortVendorName(name: string): string {
  return name.replace(/\s*\(.*?\)\s*/g, ' ').trim() || name
}

const plural = (count: number, unit: string) => `${count} ${unit}${count === 1 ? '' : 's'}`

/** "45 seconds", "18 minutes", "2 hours 5 minutes". Rounds to whole minutes from a minute up. */
export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.round(seconds))
  if (total < 60) return plural(total, 'second')
  const minutes = Math.round(total / 60)
  if (minutes < 60) return plural(minutes, 'minute')
  const hours = Math.floor(minutes / 60)
  const rest = minutes % 60
  return rest === 0 ? plural(hours, 'hour') : `${plural(hours, 'hour')} ${plural(rest, 'minute')}`
}

/**
 * When the problem began as far as anyone can tell: our first failed check, if it came before
 * the vendor's post, or else the post.
 */
export function startOf(incident: Incident): string {
  const firstFailed = incident.checks?.outage?.firstFailedAt
  return firstFailed && Date.parse(firstFailed) < Date.parse(incident.startedAt) ? firstFailed : incident.startedAt
}

/** "ongoing for 18 minutes" or "lasted 7 minutes". */
export function durationText(incident: Incident, now: number): string {
  const start = Date.parse(startOf(incident))
  if (incident.resolvedAt === null) {
    return `ongoing for ${formatDuration((now - start) / 1000)}`
  }
  return `lasted ${formatDuration((Date.parse(incident.resolvedAt) - start) / 1000)}`
}

/** The comparison headline: "Caught 4 minutes early", or who was first when it wasn't us. */
export function leadHeadline(outage: EarlyDetection, vendorName: string): string {
  const vendor = shortVendorName(vendorName)
  if (outage.leadSeconds > 0) return `Caught ${formatDuration(outage.leadSeconds)} early`
  if (outage.leadSeconds === 0) return `Our checks found it as ${vendor} posted`
  return `${vendor} posted ${formatDuration(-outage.leadSeconds)} before our checks found it`
}

/** The heading over the chart, for every case including no outage and no checks at all. */
export function checksHeadline(checks: IncidentChecks, vendorName: string): string {
  if (checks.outage) return leadHeadline(checks.outage, vendorName)
  return `Our checks didn't find ${shortVendorName(vendorName)} down`
}

/** "returned 503" or "got no response". */
export function failureText(point: CheckPoint | undefined): string {
  if (!point || point.statusCode === null) return 'the check got no response'
  return `the check returned ${point.statusCode}`
}

/** A sentence describing the chart for screen readers, since the bars are a picture. */
export function chartSummary(checks: IncidentChecks, formatTime: (iso: string) => string): string {
  const failed = checks.points.filter((p) => !p.succeeded).length
  const passed = checks.points.length - failed
  return `Checks from ${formatTime(checks.from)} to ${formatTime(checks.to)}: ${plural(passed, 'check')} passed and ${plural(failed, 'check')} failed.`
}

/** Within a second, since times come back with sub-second precision from different sources. */
export function sameMoment(a: string, b: string): boolean {
  return Math.abs(Date.parse(a) - Date.parse(b)) < 1_000
}

/** Best to worst, the same order as VendorSeverity on the server. */
const severity: ComponentStatus[] = [
  'operational',
  'underMaintenance',
  'unknown',
  'degradedPerformance',
  'partialOutage',
  'majorOutage',
]

/** The worst of the statuses, or null when there are none. */
export function worstStatus(statuses: ComponentStatus[]): ComponentStatus | null {
  return statuses.reduce<ComponentStatus | null>(
    (worst, s) => (worst === null || severity.indexOf(s) > severity.indexOf(worst) ? s : worst),
    null,
  )
}
