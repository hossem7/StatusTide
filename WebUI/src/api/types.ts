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

// Mirrors MeDto in StatusTide.Api/Features/Users/MeQuery.cs.

export interface Me {
  id: string
  isGuest: boolean
  email: string | null
  /** When a guest is deleted if they don't come back. Null for accounts. */
  guestExpiresAt: string | null
}

// Mirrors DemoOutageDto in StatusTide.Api/Features/Demo/DemoController.cs.

/**
 * The demo vendor's simulated outage, shared by every visitor. "coolingDown" means the last
 * outage ended and a new one can start at `nextStartAt`.
 */
export interface DemoOutage {
  state: 'none' | 'active' | 'coolingDown'
  startedAt: string | null
  resolvesAt: string | null
  nextStartAt: string | null
}
