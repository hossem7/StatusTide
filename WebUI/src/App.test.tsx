import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { FakeConnection } from './test/fakeConnection'
import { acme, guest } from './test/sampleCards'
import App from './App'

const connections = vi.hoisted(() => [] as unknown[])

vi.mock('./api/statusConnection', async () => {
  const { FakeConnection } = await import('./test/fakeConnection')
  return {
    createStatusConnection: () => {
      const connection = new FakeConnection()
      connections.push(connection)
      return connection
    },
  }
})

type Route = () => Response

/** Answers each "METHOD /path" from the table. Anything else is a 404. */
function stubApi(routes: Record<string, Route>) {
  const fetchMock = vi.fn(async (path: string, init?: RequestInit) => {
    const route = routes[`${init?.method ?? 'GET'} ${path}`]
    return route ? route() : new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const unauthorized = () => new Response(null, { status: 401 })
const dashboard = () => Response.json({ vendors: [acme] })
const noOutage = () => Response.json({ state: 'none', startedAt: null, resolvesAt: null, nextStartAt: null })

beforeEach(() => {
  connections.length = 0
})

afterEach(() => vi.unstubAllGlobals())

describe('App', () => {
  it('offers a guest session to a visitor with none, without opening a connection', async () => {
    const fetchMock = stubApi({ 'GET /api/me': unauthorized })
    render(<App />)

    expect(await screen.findByRole('button', { name: 'Try it as a guest' })).toBeEnabled()
    expect(connections).toHaveLength(0)
    expect(fetchMock).not.toHaveBeenCalledWith('/api/dashboard', expect.anything())
    expect(fetchMock).not.toHaveBeenCalledWith('/api/guest', expect.anything())
    expect(screen.queryByText('Guest')).toBeNull()
  })

  it('goes to the dashboard after starting a guest session', async () => {
    stubApi({
      'GET /api/me': unauthorized,
      'POST /api/guest': () => Response.json(guest, { status: 201 }),
      'GET /api/dashboard': dashboard,
      'GET /api/demo/outage': noOutage,
    })
    render(<App />)

    await userEvent.click(await screen.findByRole('button', { name: 'Try it as a guest' }))

    expect(await screen.findByRole('article', { name: 'Acme Cloud (demo)' })).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Guest session' })).toHaveTextContent(
      'We keep this dashboard for 30 days after your last visit.',
    )
    expect(screen.getByRole('button', { name: 'Create account to keep it' })).toBeDisabled()
    expect(screen.getByText('Guest')).toBeInTheDocument()
    expect((connections as FakeConnection[])[0].started).toBe(1)
  })

  it('takes a returning guest straight to the dashboard', async () => {
    stubApi({
      'GET /api/me': () => Response.json(guest),
      'GET /api/dashboard': dashboard,
      'GET /api/demo/outage': noOutage,
    })
    render(<App />)

    expect(await screen.findByRole('article', { name: 'Acme Cloud (demo)' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Try it as a guest' })).toBeNull()
  })

  it('shows no guest banner or badge to an account', async () => {
    const account = { ...guest, isGuest: false, email: 'someone@example.com', guestExpiresAt: null }
    stubApi({
      'GET /api/me': () => Response.json(account),
      'GET /api/dashboard': dashboard,
      'GET /api/demo/outage': noOutage,
    })
    render(<App />)

    await screen.findByRole('article', { name: 'Acme Cloud (demo)' })

    expect(screen.queryByRole('region', { name: 'Guest session' })).toBeNull()
    expect(screen.queryByText('Guest')).toBeNull()
  })

  it('goes back to the guest prompt when the dashboard says the session is gone', async () => {
    stubApi({ 'GET /api/me': () => Response.json(guest), 'GET /api/dashboard': unauthorized })
    render(<App />)

    expect(await screen.findByRole('button', { name: 'Try it as a guest' })).toBeInTheDocument()
    await waitFor(() => expect((connections as FakeConnection[])[0].stopped).toBe(1))
  })

  it('offers a retry when the server is down', async () => {
    const fetchMock = stubApi({ 'GET /api/me': () => new Response(null, { status: 503 }) })
    render(<App />)

    await userEvent.click(await screen.findByRole('button', { name: 'Try again' }))

    await waitFor(() => expect(fetchMock.mock.calls.filter(([path]) => path === '/api/me')).toHaveLength(2))
  })
})
