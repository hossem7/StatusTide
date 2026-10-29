import { describe, expect, it } from 'vitest'
import { acme, acmeDown, nimbus } from '../../test/sampleCards'
import { componentSummary, formatChecked, formatWait, initialsOf, statsOf } from './format'

const now = Date.parse('2026-11-02T12:00:00Z')

describe('formatChecked', () => {
  it.each([
    ['2026-11-02T11:59:20Z', 'Checked 40s ago'],
    ['2026-11-02T11:58:30Z', 'Checked 1m ago'],
    ['2026-11-02T09:00:00Z', 'Checked 3h ago'],
    ['2026-10-30T12:00:00Z', 'Checked 3d ago'],
    ['2026-11-02T12:00:05Z', 'Checked 0s ago'],
  ])('shows %s as "%s"', (checkedAt, expected) => {
    expect(formatChecked(checkedAt, now)).toBe(expected)
  })

  it('says when there is no check on record or the time is broken', () => {
    expect(formatChecked(null, now)).toBe('Not checked yet')
    expect(formatChecked('not a date', now)).toBe('Not checked yet')
  })
})

describe('componentSummary', () => {
  it('names the only followed component', () => {
    expect(componentSummary(nimbus)).toBe('Storage only')
  })

  it('says all when every component is followed', () => {
    expect(componentSummary(acme)).toBe('All 4 components')
  })

  it('counts a partial selection', () => {
    const card = { ...acme, followedComponents: acme.followedComponents.slice(0, 3), componentCount: 10 }
    expect(componentSummary(card)).toBe('3 of 10 components')
  })
})

describe('initialsOf', () => {
  it.each([
    ['Acme Cloud (demo)', 'AC'],
    ['Nimbus', 'NI'],
    ['cirrus-status', 'CS'],
    ['', '?'],
  ])('turns "%s" into %s', (name, expected) => {
    expect(initialsOf(name)).toBe(expected)
  })
})

describe('statsOf', () => {
  it('still reports health when a vendor is Unknown', () => {
    expect(statsOf([acme, nimbus])).toEqual({ followed: 2, operational: 1, activeIncidents: 0 })
  })

  it('adds up active incidents', () => {
    expect(statsOf([acmeDown, { ...nimbus, activeIncidentCount: 2 }])).toEqual({
      followed: 2,
      operational: 0,
      activeIncidents: 3,
    })
  })
})

describe('formatWait', () => {
  it.each([
    ['2026-11-02T12:01:30Z', 'in 2 min'],
    ['2026-11-02T12:01:00Z', 'in 1 min'],
    ['2026-11-02T12:00:59Z', 'in under a minute'],
    ['2026-11-02T11:59:00Z', 'in under a minute'],
  ])('%s is %s away', (until, text) => {
    expect(formatWait(until, now)).toBe(text)
  })
})
