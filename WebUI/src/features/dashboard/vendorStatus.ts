import type { ComponentStatus } from '../../types/status'
import type { Vendor, VendorComponent } from './types'

// The components the user actually follows on a vendor.
export function followedComponents(vendor: Vendor): VendorComponent[] {
  return vendor.components.filter((component) => component.followed)
}

// Worst wins: a vendor's overall status is the most severe status among the
// components the user follows. Higher number is worse.
const severity: Record<ComponentStatus, number> = {
  Operational: 0,
  UnderMaintenance: 1,
  Unknown: 2,
  DegradedPerformance: 3,
  PartialOutage: 4,
  MajorOutage: 5,
}

export function vendorStatus(vendor: Vendor): ComponentStatus {
  const followed = followedComponents(vendor)
  if (followed.length === 0) return 'Unknown'

  return followed.reduce<ComponentStatus>(
    (worst, component) =>
      severity[component.status] > severity[worst] ? component.status : worst,
    'Operational',
  )
}
