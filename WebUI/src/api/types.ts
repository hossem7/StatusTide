// Mirrors the API DTOs in StatusTide.Api/Features/Dashboard/DashboardDtos.cs.
// Enums arrive as camelCase strings.

export type ComponentStatus =
  | 'operational'
  | 'degradedPerformance'
  | 'partialOutage'
  | 'majorOutage'
  | 'underMaintenance'
  | 'unknown'

export interface FollowedComponent {
  id: string
  name: string
  status: ComponentStatus
}

/** Untrusted: the title is the vendor's own text, so render it as text only. */
export interface ActiveIncident {
  id: string
  title: string
  startedAt: string
}

export interface VendorCard {
  id: string
  slug: string
  name: string
  /** The worst status among the components this user follows, computed on the server. */
  status: ComponentStatus
  followedComponents: FollowedComponent[]
  componentCount: number
  lastCheckedAt: string | null
  isDemo: boolean
  activeIncidentCount: number
  activeIncident: ActiveIncident | null
}

export interface Dashboard {
  vendors: VendorCard[]
}
