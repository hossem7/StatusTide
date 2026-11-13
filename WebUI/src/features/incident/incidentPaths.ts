const prefix = '/incidents/'

/** The incident page's address. */
export function incidentPath(incidentId: string): string {
  return prefix + encodeURIComponent(incidentId)
}

const idPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** The incident id in a path like "/incidents/{id}", or null when the path is not an incident page. */
export function incidentIdFrom(path: string): string | null {
  if (!path.startsWith(prefix)) return null
  const id = path.slice(prefix.length)
  return idPattern.test(id) ? id : null
}

/** True for any "/incidents/..." path, including one with a broken id, so it can show "not found". */
export function isIncidentPath(path: string): boolean {
  return path.startsWith(prefix)
}
