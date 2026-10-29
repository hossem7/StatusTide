import type { DemoOutage, VendorCard } from '../api/types'

// Sample cards for tests only. Acme Cloud is our demo vendor; the others are made up, like the
// ".example" vendors in the backend fixtures. Never use real vendors here.

export const acme = {
  id: '0199e0a0-0000-7000-8000-000000000001',
  slug: 'acme-cloud',
  name: 'Acme Cloud (demo)',
  status: 'operational',
  followedComponents: [
    { id: '0199e0a0-0000-7000-8000-0000000000a1', name: 'API', status: 'operational' },
    { id: '0199e0a0-0000-7000-8000-0000000000a2', name: 'Dashboard', status: 'operational' },
    { id: '0199e0a0-0000-7000-8000-0000000000a3', name: 'Managed Database', status: 'operational' },
    { id: '0199e0a0-0000-7000-8000-0000000000a4', name: 'Webhooks', status: 'operational' },
  ],
  componentCount: 4,
  lastCheckedAt: '2026-11-02T12:00:00Z',
  isDemo: true,
  activeIncidentCount: 0,
  activeIncident: null,
} satisfies VendorCard

export const nimbus = {
  id: '0199e0a0-0000-7000-8000-000000000002',
  slug: 'nimbus',
  name: 'Nimbus',
  status: 'unknown',
  followedComponents: [{ id: '0199e0a0-0000-7000-8000-0000000000b1', name: 'Storage', status: 'unknown' }],
  componentCount: 6,
  lastCheckedAt: null,
  isDemo: false,
  activeIncidentCount: 0,
  activeIncident: null,
} satisfies VendorCard

export const acmeDown = {
  ...acme,
  status: 'majorOutage',
  activeIncidentCount: 1,
  activeIncident: {
    id: '0199e0a0-0000-7000-8000-0000000000c1',
    title: 'Elevated API errors',
    startedAt: '2026-11-02T12:01:00Z',
  },
} satisfies VendorCard

// Demo outage states, as GET /api/demo/outage returns them.

export const noOutage = { state: 'none', startedAt: null, resolvesAt: null, nextStartAt: null } satisfies DemoOutage

export const activeOutage = {
  state: 'active',
  startedAt: '2026-11-02T12:00:00Z',
  resolvesAt: '2026-11-02T12:05:00Z',
  nextStartAt: '2026-11-02T12:07:00Z',
} satisfies DemoOutage

export const coolingDown = { ...activeOutage, state: 'coolingDown' } satisfies DemoOutage
