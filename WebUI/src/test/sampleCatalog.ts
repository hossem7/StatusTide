import type { CatalogVendor, CatalogVendorDetail } from '../api/types'

// Sample catalog for tests only. Acme Cloud is our demo vendor; Nimbus is made up. Never use real
// vendors here.

export const acmeTile = {
  id: '0199e0a0-0000-7000-8000-000000000001',
  slug: 'acme-cloud',
  name: 'Acme Cloud (demo)',
  category: 'cloud',
  statusPageUrl: '/vendors/acme-cloud',
  componentCount: 4,
  followedComponentCount: 4,
} satisfies CatalogVendor

export const nimbusTile = {
  id: '0199e0a0-0000-7000-8000-000000000002',
  slug: 'nimbus',
  name: 'Nimbus',
  category: 'developerTools',
  statusPageUrl: 'https://status.nimbus.example',
  componentCount: 6,
  followedComponentCount: 0,
} satisfies CatalogVendor

export const acmeDetail = {
  id: acmeTile.id,
  name: acmeTile.name,
  statusPageUrl: acmeTile.statusPageUrl,
  components: [
    { id: 'a1', name: 'API', status: 'operational', followed: true },
    { id: 'a2', name: 'Dashboard', status: 'operational', followed: true },
    { id: 'a3', name: 'Managed Database', status: 'operational', followed: false },
  ],
} satisfies CatalogVendorDetail
