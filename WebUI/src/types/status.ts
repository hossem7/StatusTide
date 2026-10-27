// Mirrors the backend ComponentStatus enum (StatusTide.Data/Vendors/VendorEnums.cs).
// Kept in sync by hand until the API client is generated.
export type ComponentStatus =
  | 'Unknown'
  | 'Operational'
  | 'DegradedPerformance'
  | 'PartialOutage'
  | 'MajorOutage'
  | 'UnderMaintenance'

interface StatusDisplay {
  label: string
  color: string
}

// How each status appears: a human label and the dot/pill color.
export const statusDisplay: Record<ComponentStatus, StatusDisplay> = {
  Operational: { label: 'Operational', color: '#16a34a' },
  DegradedPerformance: { label: 'Degraded', color: '#d97706' },
  PartialOutage: { label: 'Partial outage', color: '#ea580c' },
  MajorOutage: { label: 'Major outage', color: '#dc2626' },
  UnderMaintenance: { label: 'Maintenance', color: '#2563eb' },
  Unknown: { label: 'Unknown', color: '#6b7280' },
}
