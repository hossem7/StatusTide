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
  /** Whether the account's email is confirmed. Always false for guests. */
  emailConfirmed: boolean
}

// Mirrors AuthResponse in StatusTide.Api/Features/Accounts/AuthController.cs.

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: Me
}

// Mirrors the DTOs in StatusTide.Api/Features/Catalog/CatalogQuery.cs.

export type VendorCategory = 'developerTools' | 'cloud' | 'payments' | 'ai' | 'communication'

export interface CatalogQuery {
  search: string
  category: VendorCategory | null
}

export interface CatalogVendor {
  id: string
  slug: string
  name: string
  category: VendorCategory | null
  /** Not always a web address: check it with externalUrl before linking to it. */
  statusPageUrl: string
  componentCount: number
  /** Zero means the user doesn't follow this vendor. */
  followedComponentCount: number
}

export interface Catalog {
  vendors: CatalogVendor[]
}

export interface CatalogComponent {
  id: string
  name: string
  status: ComponentStatus
  followed: boolean
}

export interface CatalogVendorDetail {
  id: string
  name: string
  statusPageUrl: string
  components: CatalogComponent[]
}

// Mirrors DemoOutageDto in StatusTide.Api/Features/Demo/DemoController.cs.

/**
 * The demo vendor's simulated outage, shared by every visitor. "coolingDown" means the last
 * outage ended and a new one can start at `nextStartAt`.
 */
export interface DemoOutage {
  state: 'none' | 'active' | 'coolingDown'
  startedAt: string | null
  /**
   * When Acme posts the incident on its status page. Until then only our own checks see the
   * outage, so the dashboard shows no change. Null when it was resolved before Acme posted.
   */
  postsAt: string | null
  resolvesAt: string | null
  nextStartAt: string | null
}

// Mirrors the DTOs in StatusTide.Api/Features/Incidents/IncidentDtos.cs.

export type IncidentStatus = 'investigating' | 'identified' | 'monitoring' | 'resolved'

export interface IncidentComponent {
  id: string
  name: string
  /** The component's status now, not at the time of the incident. */
  status: ComponentStatus
  /** The vendor named this component in the incident. */
  isAffected: boolean
  isFollowed: boolean
}

/** A status change the vendor posted. Their written text is never stored. */
export interface VendorUpdate {
  status: IncidentStatus
  occurredAt: string
}

export interface CheckPoint {
  checkedAt: string
  succeeded: boolean
  /** Null when no response came back, such as a timeout. */
  statusCode: number | null
  responseTimeMs: number | null
}

export interface EarlyDetection {
  firstFailedAt: string
  /** The 3rd failure in a row, when our checks called the vendor down. */
  detectedAt: string
  recoveredAt: string | null
  vendorPostedAt: string
  /** How long before the vendor's post we called it down. Negative when the vendor posted first. */
  leadSeconds: number
}

export interface IncidentChecks {
  from: string
  to: string
  /** Oldest first. Raw checks are kept for 7 days, so older incidents have none. */
  points: CheckPoint[]
  /** Null when our checks never found the vendor down around this incident. */
  outage: EarlyDetection | null
}

export interface Incident {
  id: string
  /** Untrusted: the vendor's own text, so render it as text only. */
  title: string
  status: IncidentStatus
  /** When the vendor first posted it. */
  startedAt: string
  resolvedAt: string | null
  /** Not always a web address: check it with externalUrl before linking to it. */
  sourceUrl: string
  vendor: { id: string; slug: string; name: string; isDemo: boolean }
  /** Components the incident names first, then the other ones the user follows. */
  components: IncidentComponent[]
  /** Newest first. */
  updates: VendorUpdate[]
  /** Null when we don't run our own checks on this vendor. */
  checks: IncidentChecks | null
}
