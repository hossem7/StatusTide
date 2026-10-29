import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { DemoOutage } from '../../api/types'
import { acme, acmeDown, activeOutage, coolingDown, nimbus, noOutage } from '../../test/sampleCards'
import { DashboardView } from './DashboardPage'
import type { DashboardState, LiveState } from './useDashboard'
import type { UseDemoOutage } from './useDemoOutage'

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
    renderView({ kind: 'error', error: new ApiError(401, 'GET', '/api/dashboard') })

    expect(screen.getByRole('alert')).toHaveTextContent('You need to be signed in')
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull()
  })

  it('lets the user retry after a server error', async () => {
    const retry = renderView({ kind: 'error', error: new ApiError(503, 'GET', '/api/dashboard') })

    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(retry).toHaveBeenCalled()
  })
})

describe('OutageBanner', () => {
  it('names the vendor that is down and its incident', () => {
    renderView({ kind: 'ready', vendors: [nimbus, acmeDown] })

    const banner = screen.getByRole('region', { name: 'Outages' })
    expect(banner).toHaveTextContent('Acme Cloud (demo): major outage.')
    expect(banner).toHaveTextContent('Elevated API errors, since')
  })

  it('counts the other vendors that are down', () => {
    const down = (name: string, id: string) => ({ ...acmeDown, id, name, isDemo: false })
    renderView({ kind: 'ready', vendors: [acmeDown, down('Nimbus', 'n'), down('Cirrus', 'c')] })

    expect(screen.getByRole('region', { name: 'Outages' })).toHaveTextContent('2 other services are down too.')
  })

  it('stays hidden while everything is up', () => {
    renderView({ kind: 'ready', vendors: [acme, nimbus] })

    expect(screen.queryByRole('region', { name: 'Outages' })).toBeNull()
  })
})

describe('DemoControls', () => {
  function renderDemo(outage: DemoOutage | null, extra: Partial<UseDemoOutage> = {}) {
    const demo = { outage, busy: false, error: '', start: vi.fn(), resolve: vi.fn(), ...extra }
    render(
      <DashboardView
        state={{ kind: 'ready', vendors: [acme, nimbus] }}
        live="live"
        announcement=""
        retry={vi.fn()}
        now={Date.parse('2026-11-02T12:05:30Z')}
        demo={demo}
      />,
    )
    return demo
  }

  it('starts an outage from the demo card only', async () => {
    const demo = renderDemo(noOutage)

    expect(within(screen.getByRole('article', { name: 'Nimbus' })).queryByRole('button')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Simulate an outage' }))

    expect(demo.start).toHaveBeenCalled()
  })

  it('resolves a running outage', async () => {
    const demo = renderDemo(activeOutage)

    await userEvent.click(screen.getByRole('button', { name: 'Resolve the outage' }))

    expect(demo.resolve).toHaveBeenCalled()
  })

  it('waits for the cooldown', () => {
    renderDemo(coolingDown)

    expect(screen.getByRole('button', { name: 'Simulate an outage' })).toBeDisabled()
    expect(screen.getByText('Ready again in 2 min')).toBeInTheDocument()
  })

  it.each([
    ['loading', null, {}],
    ['sending', noOutage, { busy: true }],
  ] as const)('disables the button while %s', (_, outage, extra) => {
    renderDemo(outage, extra)

    expect(screen.getByRole('button', { name: 'Simulate an outage' })).toBeDisabled()
  })

  it('shows why a click failed', () => {
    renderDemo(noOutage, { error: 'Too many tries. Wait a minute and try again.' })

    expect(screen.getByRole('alert')).toHaveTextContent('Too many tries')
  })
})
