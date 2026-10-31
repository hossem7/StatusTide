import type { VendorCard } from '../../api/types'

/** "Checked 40s ago". Takes `now` so it can be tested without faking the clock. */
export function formatChecked(lastCheckedAt: string | null, now: number): string {
  const checked = lastCheckedAt === null ? Number.NaN : Date.parse(lastCheckedAt)
  if (Number.isNaN(checked)) {
    return 'Not checked yet'
  }

  // A clock slightly ahead of ours would give a negative age.
  const seconds = Math.max(0, Math.floor((now - checked) / 1000))
  if (seconds < 60) return `Checked ${seconds}s ago`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `Checked ${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `Checked ${hours}h ago`
  return `Checked ${Math.floor(hours / 24)}d ago`
}

/** "All 4 components", "API only" or "3 of 10 components". */
export function componentSummary({ followedComponents, componentCount }: VendorCard): string {
  const followed = followedComponents.length
  if (followed >= componentCount) {
    return componentCount === 1 ? 'All components' : `All ${componentCount} components`
  }
  if (followed === 1) {
    return `${followedComponents[0].name} only`
  }
  return `${followed} of ${componentCount} components`
}

/** Vendor tiles use initials, never logos: "Acme Cloud (demo)" becomes "AC". */
export function initialsOf(name: string): string {
  const words = name
    .replace(/\(.*?\)/g, '')
    .split(/[\s\-_.]+/)
    .filter((word) => /\p{L}|\p{N}/u.test(word))
  const letters = words.length > 1 ? words[0][0] + words[1][0] : (words[0] ?? '?').slice(0, 2)
  return letters.toUpperCase()
}

export interface Stats {
  followed: number
  operational: number
  activeIncidents: number
}

/** Three fixed tiles, so the summary never goes quiet when some vendors are Unknown. */
export function statsOf(vendors: VendorCard[]): Stats {
  return {
    followed: vendors.length,
    operational: vendors.filter((v) => v.status === 'operational').length,
    activeIncidents: vendors.reduce((sum, v) => sum + v.activeIncidentCount, 0),
  }
}

/**
 * "in 2 min" until a time in the future. Rounds to the nearest minute because `now` can be a few
 * seconds old: rounding up would turn a fresh 2 minute wait into "in 3 min".
 */
export function formatWait(until: string, now: number): string {
  const seconds = Math.max(0, (Date.parse(until) - now) / 1000)
  if (seconds < 60) return 'in under a minute'
  return `in ${Math.round(seconds / 60)} min`
}

/** "2:16 PM" in the viewer's own time zone. */
export function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
}
