import type { ComponentStatus } from '../api/types'

const labels: Record<ComponentStatus, string> = {
  operational: 'Operational',
  degradedPerformance: 'Degraded performance',
  partialOutage: 'Partial outage',
  majorOutage: 'Major outage',
  underMaintenance: 'Under maintenance',
  unknown: 'Unknown',
}

/** Every status shows as text as well as color, so it never relies on color alone. */
export function statusLabel(status: ComponentStatus): string {
  return labels[status] ?? labels.unknown
}
