import type { ComponentStatus } from '../../types/status'

// A sub-part of a vendor, e.g. "Actions" on GitHub.
// Mirrors the backend Component entity. `followed` marks whether the current
// user follows this component: the dashboard shows only followed ones.
export interface VendorComponent {
  id: string
  name: string
  status: ComponentStatus
  followed: boolean
}

// A service on the dashboard, e.g. GitHub or the Acme Cloud demo vendor.
// Mirrors the backend Vendor entity (dashboard-facing fields only).
//
// A vendor carries all its components, but the dashboard only shows and counts
// the followed ones (the "follow only what you use" design). Overall status is
// derived from the followed components, not stored, so it can never disagree
// with what the user sees on the card.
export interface Vendor {
  id: string
  name: string
  statusPageUrl: string | null
  components: VendorComponent[]
}

// One entry in the recent activity feed: a component changing status at a time.
export interface ActivityEvent {
  id: string
  vendorName: string
  componentName: string
  status: ComponentStatus
  at: string // ISO 8601 timestamp
}

// The whole dashboard payload. Shaped like the response a future API will return.
export interface DashboardData {
  vendors: Vendor[]
  activity: ActivityEvent[]
}
