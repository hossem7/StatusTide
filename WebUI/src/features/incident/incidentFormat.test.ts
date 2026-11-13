import { describe, expect, it } from 'vitest'
import { openIncident, resolvedIncident } from '../../test/sampleIncident'
import {
  checksHeadline,
  durationText,
  failureText,
  formatDuration,
  leadHeadline,
  shortVendorName,
  startOf,
  worstStatus,
} from './incidentFormat'
import { incidentIdFrom, incidentPath, isIncidentPath } from './incidentPaths'

const outage = openIncident.checks.outage

describe('incident formatting', () => {
  it.each([
    [0, '0 seconds'],
    [1, '1 second'],
    [45, '45 seconds'],
    [60, '1 minute'],
    [144, '2 minutes'],
    [150, '3 minutes'],
    [3600, '1 hour'],
    [3900, '1 hour 5 minutes'],
    [-5, '0 seconds'],
  ])('formats %s seconds as "%s"', (seconds, text) => {
    expect(formatDuration(seconds)).toBe(text)
  })

  it('names the vendor without "(demo)"', () => {
    expect(shortVendorName('Acme Cloud (demo)')).toBe('Acme Cloud')
    expect(shortVendorName('Nimbus')).toBe('Nimbus')
  })

  it('says how early we were, or who was first when it was not us', () => {
    expect(leadHeadline(outage, 'Acme Cloud (demo)')).toBe('Caught 2 minutes early')
    expect(leadHeadline({ ...outage, leadSeconds: 40 }, 'Acme Cloud (demo)')).toBe('Caught 40 seconds early')
    expect(leadHeadline({ ...outage, leadSeconds: 0 }, 'Acme Cloud (demo)')).toBe('Our checks found it as Acme Cloud posted')
    expect(leadHeadline({ ...outage, leadSeconds: -180 }, 'Acme Cloud (demo)')).toBe(
      'Acme Cloud posted 3 minutes before our checks found it',
    )
  })

  it('says so when our checks never found the vendor down', () => {
    expect(checksHeadline({ ...openIncident.checks, outage: null }, 'Nimbus')).toBe("Our checks didn't find Nimbus down")
  })

  it('starts the incident at our first failure when it came before the post', () => {
    expect(startOf(openIncident)).toBe('2026-11-02T12:01:00Z')
    expect(startOf(resolvedIncident)).toBe(resolvedIncident.startedAt)
  })

  it('says how long it has run, or how long it lasted', () => {
    expect(durationText(openIncident, Date.parse('2026-11-02T12:19:00Z'))).toBe('ongoing for 18 minutes')
    expect(durationText(resolvedIncident, Date.parse('2026-11-02T13:00:00Z'))).toBe('lasted 25 minutes')
  })

  it('describes a failed check by its answer', () => {
    expect(failureText({ checkedAt: '', succeeded: false, statusCode: 503, responseTimeMs: 40 })).toBe('the check returned 503')
    expect(failureText({ checkedAt: '', succeeded: false, statusCode: null, responseTimeMs: null })).toBe(
      'the check got no response',
    )
  })

  it('picks the worst status in the same order as the server', () => {
    expect(worstStatus(['operational', 'degradedPerformance', 'unknown'])).toBe('degradedPerformance')
    expect(worstStatus(['partialOutage', 'majorOutage'])).toBe('majorOutage')
    expect(worstStatus([])).toBeNull()
  })
})

describe('incident paths', () => {
  it('reads the id back from the path', () => {
    const path = incidentPath(openIncident.id)

    expect(path).toBe(`/incidents/${openIncident.id}`)
    expect(incidentIdFrom(path)).toBe(openIncident.id)
  })

  it('rejects a path whose id is not a guid, so it never reaches the server', () => {
    expect(isIncidentPath('/incidents/../api/me')).toBe(true)
    expect(incidentIdFrom('/incidents/../api/me')).toBeNull()
    expect(incidentIdFrom('/catalog')).toBeNull()
  })
})
