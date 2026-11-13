import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { Incident } from '../../api/types'
import { openIncident, resolvedIncident } from '../../test/sampleIncident'
import { IncidentView } from './IncidentPage'
import type { IncidentState } from './useIncident'

const now = Date.parse('2026-11-02T12:19:00Z')

function renderView(state: IncidentState, retry = vi.fn()) {
  render(<IncidentView state={state} retry={retry} now={now} />)
  return retry
}

const ready = (incident: Incident) => ({ kind: 'ready', incident }) as const

describe('IncidentView', () => {
  it('shows the title, the worst affected status and how long it has run', () => {
    renderView(ready(openIncident))

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(
      'Acme Cloud (demo): Elevated error rates on the API',
    )
    expect(screen.getByText('Major outage', { selector: '.incident__top .status-pill' })).toBeInTheDocument()
    expect(screen.getByText(/ongoing for 18 minutes/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Back to dashboard' })).toHaveAttribute('href', '/')
  })

  it('compares our checks with the official post', () => {
    renderView(ready(openIncident))

    const checks = screen.getByRole('region', { name: 'Caught 2 minutes early' })
    expect(within(checks).getByRole('img')).toHaveAccessibleName(/10 checks passed and 10 checks failed/)
    const moments = within(checks)
      .getAllByRole('listitem')
      .map((li) => li.textContent)
    expect(moments).toHaveLength(3)
    expect(moments[0]).toMatch(/First failed check, the check returned 503$/)
    expect(moments[1]).toMatch(/Confirmed down after 3 failures in a row$/)
    expect(moments[2]).toMatch(/Acme Cloud posted on its official status page$/)
  })

  it('marks the failure that confirmed the outage on the chart', () => {
    renderView(ready(openIncident))

    const bars = document.querySelectorAll('.check-chart__bar')
    expect(bars).toHaveLength(20)
    expect(document.querySelectorAll('.check-chart__bar[data-kind="detected"]')).toHaveLength(1)
    expect(document.querySelectorAll('.check-chart__bar[data-kind="failed"]')).toHaveLength(9)
    expect(document.querySelector('.check-chart__post')).not.toBeNull()
  })

  it('lists components with what they mean to this user', () => {
    renderView(ready(openIncident))

    const items = within(screen.getByRole('complementary', { name: 'Components' })).getAllByRole('listitem')
    expect(items.map((li) => li.textContent)).toEqual([
      'APINamed in this incidentYou follow itMajor outage',
      'WebhooksNamed in this incidentDegraded performance',
      'DashboardYou follow itOperational',
    ])
  })

  it('lists the official updates newest first, without linking to a page that does not exist', () => {
    renderView(ready(openIncident))

    const updates = screen.getByRole('region', { name: 'Official status from Acme Cloud' })
    expect(within(updates).getAllByRole('listitem').map((li) => li.querySelector('b')?.textContent)).toEqual([
      'Identified',
      'Investigating',
    ])
    expect(within(updates).queryByRole('link')).toBeNull()
    expect(within(updates).getByText(/our demo vendor/)).toBeInTheDocument()
  })

  it('links to a real status page in a new tab', () => {
    renderView(ready(resolvedIncident))

    const link = screen.getByRole('link', { name: "Read the full updates on Nimbus's status page ↗" })
    expect(link).toHaveAttribute('href', 'https://status.nimbus.example/incidents/abc')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    expect(screen.getByText('Resolved', { selector: '.status-pill' })).toBeInTheDocument()
    expect(screen.getByText(/lasted 25 minutes/)).toBeInTheDocument()
  })

  it('never links to an address that is not http or https', () => {
    renderView(ready({ ...resolvedIncident, sourceUrl: 'javascript:alert(1)' }))

    expect(screen.queryByRole('link', { name: /Read the full updates/ })).toBeNull()
  })

  it('says when we do not check the vendor', () => {
    renderView(ready(resolvedIncident))

    expect(screen.getByText(/We don't run our own checks on Nimbus yet/)).toBeInTheDocument()
  })

  it('says when the checks are older than we keep', () => {
    renderView(ready({ ...openIncident, checks: { ...openIncident.checks, points: [] } }))

    expect(screen.getByRole('region', { name: 'Caught 2 minutes early' })).toHaveTextContent(
      'We keep each check for 7 days, so the chart for this incident is gone.',
    )
    expect(screen.queryByRole('img')).toBeNull()
  })

  it('renders the title as text, never as markup', () => {
    const title = '<img src=x onerror=alert(1)>'
    renderView(ready({ ...openIncident, title }))

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(title)
    expect(document.querySelector('img')).toBeNull()
  })

  it('says when the incident is not there', () => {
    renderView({ kind: 'error', error: new ApiError(404, 'GET', '/api/incidents/x') })

    expect(screen.getByRole('alert')).toHaveTextContent("We couldn't find that incident.")
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('offers to try again when loading failed', async () => {
    const retry = renderView({ kind: 'error', error: new TypeError('Failed to fetch') })

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(retry).toHaveBeenCalled()
  })
})
