import type { CatalogVendor, VendorCategory } from '../../api/types'

export const catalogPath = '/catalog'

/** In the design's order. */
export const categories: { value: VendorCategory; label: string }[] = [
  { value: 'developerTools', label: 'Developer tools' },
  { value: 'cloud', label: 'Cloud' },
  { value: 'payments', label: 'Payments' },
  { value: 'ai', label: 'AI' },
  { value: 'communication', label: 'Communication' },
]

export function categoryLabel(category: VendorCategory | null): string {
  return categories.find((c) => c.value === category)?.label ?? ''
}

export function followSummary({ componentCount, followedComponentCount }: CatalogVendor): string {
  if (followedComponentCount === componentCount) {
    return componentCount === 1 ? 'The 1 component' : `All ${componentCount} components`
  }
  return `${followedComponentCount} of ${componentCount} components`
}

/**
 * The address to link to, or null when it can't be a link. Only absolute http and https addresses
 * qualify: the demo vendor's page is a local path with no screen yet, and custom pages (#16) will
 * make this value user-supplied, so a "javascript:" address must never reach an href.
 */
export function externalUrl(url: string): string | null {
  try {
    const { protocol } = new URL(url)
    return protocol === 'https:' || protocol === 'http:' ? url : null
  } catch {
    return null
  }
}
