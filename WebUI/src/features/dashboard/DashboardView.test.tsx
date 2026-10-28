import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import { acmeDown, nimbus } from '../../test/sampleCards'
import { DashboardView } from './DashboardPage'
import type { DashboardState, LiveState } from './useDashboard'

const now = Date.parse('2026-11-02T12:00:40Z')

function renderView(state: DashboardState, live: LiveState = 'live', retry = vi.fn()) {
  render(<DashboardView state={state} live={live} announcement="" retry={retry} now={now} />)
  return retry
}

describe('DashboardView', () => {
  it('shows the stats and one card per vendor under the page heading', () => {
    renderView({ kind: 'ready', vendors: [acmeDown, nimbus] })

    expect(screen.getByRole('heading', { level: 1, name: 'Your services' })).toBeInTheDocument()
    expect(screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)).toEqual([
      'Acme Cloud (demo)',
      'Nimbus',
    ])
    expect(screen.getByText('Services followed').nextSibling).toHaveTextContent('2')
    expect(screen.getByText('Active incidents').nextSibling).toHaveTextContent('1')

    const card = screen.getByRole('article', { name: 'Acme Cloud (demo)' })
    expect(within(card).getByText('Major outage')).toBeInTheDocument()
    expect(within(card).getByText('All 4 components')).toBeInTheDocument()
    expect(within(card).getByText('Checked 40s ago')).toBeInTheDocument()
    expect(within(card).getByText('Elevated API errors')).toBeInTheDocument()
  })

  it('renders an incident title as text, never as markup', () => {
    const title = '<img src=x onerror=alert(1)>'
    const hostile = { ...acmeDown, activeIncident: { ...acmeDown.activeIncident, title } }
    renderView({ kind: 'ready', vendors: [hostile] })

    expect(screen.getByText(title)).toBeInTheDocument()
    expect(document.querySelector('img')).toBeNull()
  })

  it.each<[LiveState, string]>([
    ['connecting', 'Connecting to live updates'],
    ['live', 'Live, updates appear without refreshing'],
    ['reconnecting', 'Reconnecting, updates are paused'],
    ['offline', 'Not live, updates are paused'],
  ])('shows the %s state', (live, text) => {
    renderView({ kind: 'ready', vendors: [nimbus] }, live)

    expect(screen.getByText(text)).toBeInTheDocument()
  })

  it('offers to reconnect when offline', async () => {
    const retry = renderView({ kind: 'ready', vendors: [nimbus] }, 'offline')

    await userEvent.click(screen.getByRole('button', { name: 'Reconnect' }))

    expect(retry).toHaveBeenCalled()
  })

  it('tells a user who follows nothing', () => {
    renderView({ kind: 'ready', vendors: [] })

    expect(screen.getByText(/following any services yet/)).toBeInTheDocument()
    expect(screen.queryByText('Services followed')).toBeNull()
  })

  it('asks a signed out user to sign in', () => {
    renderView({ kind: 'error', error: new ApiError(401, '/api/dashboard') })

    expect(screen.getByRole('alert')).toHaveTextContent('You need to be signed in')
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull()
  })

  it('lets the user retry after a server error', async () => {
    const retry = renderView({ kind: 'error', error: new ApiError(503, '/api/dashboard') })

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(retry).toHaveBeenCalled()
  })
})
