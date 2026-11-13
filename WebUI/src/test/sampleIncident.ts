import type { CheckPoint, Incident } from '../api/types'

// The demo outage from the design: our checks fail from 12:01, call it down at 12:03, and Acme
// posts at 12:05. Times are UTC.

export const incidentId = '0199e0a0-0000-7000-8000-0000000000c1'

/** One check a minute from 11:51 to 12:10: passing until 12:00, failing from 12:01. */
export const samplePoints: CheckPoint[] = Array.from({ length: 20 }, (_, i) => {
  const succeeded = i < 10
  return {
    checkedAt: new Date(Date.parse('2026-11-02T11:51:00Z') + i * 60_000).toISOString(),
    succeeded,
    statusCode: succeeded ? 200 : 503,
    responseTimeMs: succeeded ? 100 + i * 10 : 40,
  }
})

export const openIncident = {
  id: incidentId,
  title: 'Elevated error rates on the API',
  status: 'identified',
  startedAt: '2026-11-02T12:05:00Z',
  resolvedAt: null,
  sourceUrl: '/vendors/acme-cloud',
  vendor: { id: '0199f0a0-0000-7000-8000-00000000ac01', slug: 'acme-cloud', name: 'Acme Cloud (demo)', isDemo: true },
  components: [
    { id: 'c-api', name: 'API', status: 'majorOutage', isAffected: true, isFollowed: true },
    { id: 'c-webhooks', name: 'Webhooks', status: 'degradedPerformance', isAffected: true, isFollowed: false },
    { id: 'c-dashboard', name: 'Dashboard', status: 'operational', isAffected: false, isFollowed: true },
  ],
  updates: [
    { status: 'identified', occurredAt: '2026-11-02T12:06:00Z' },
    { status: 'investigating', occurredAt: '2026-11-02T12:05:00Z' },
  ],
  checks: {
    from: '2026-11-02T11:51:00Z',
    to: '2026-11-02T12:10:00Z',
    points: samplePoints,
    outage: {
      firstFailedAt: '2026-11-02T12:01:00Z',
      detectedAt: '2026-11-02T12:03:00Z',
      recoveredAt: null,
      vendorPostedAt: '2026-11-02T12:05:00Z',
      leadSeconds: 120,
    },
  },
} satisfies Incident

/** A real vendor's incident that has ended, with a status page we may link to and no checks. */
export const resolvedIncident = {
  ...openIncident,
  status: 'resolved',
  resolvedAt: '2026-11-02T12:30:00Z',
  sourceUrl: 'https://status.nimbus.example/incidents/abc',
  vendor: { id: 'v-nimbus', slug: 'nimbus', name: 'Nimbus', isDemo: false },
  checks: null,
} satisfies Incident
