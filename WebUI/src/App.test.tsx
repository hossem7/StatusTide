import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearAccess } from './api/client'
import type { Me } from './api/types'
import type { FakeConnection } from './test/fakeConnection'
import { account, acme, acmeDown, guest } from './test/sampleCards'
import { openIncident } from './test/sampleIncident'
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
  // No refresh cookie unless a test says so, the same as any guest.
  const table: Record<string, Route> = { 'POST /api/auth/refresh': () => new Response(null, { status: 401 }), ...routes }
  const fetchMock = vi.fn(async (path: string, init?: RequestInit) => {
    const route = table[`${init?.method ?? 'GET'} ${path}`]
    return route ? route() : new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const unauthorized = () => new Response(null, { status: 401 })
const dashboard = () => Response.json({ vendors: [acme] })
const noOutage = () => Response.json({ state: 'none', startedAt: null, postsAt: null, resolvesAt: null, nextStartAt: null })

beforeEach(() => {
  connections.length = 0
})

afterEach(() => {
  vi.unstubAllGlobals()
  clearAccess()
  window.history.replaceState(null, '', '/')
})

const authResponse = (user: Me, status = 200) => () =>
  Response.json({ accessToken: 'jwt-1', expiresAt: new Date(Date.now() + 900_000).toISOString(), user }, { status })

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
    expect(screen.getByRole('button', { name: 'Create account to keep it' })).toBeEnabled()
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

  it('restores an account from the refresh cookie and sends its token', async () => {
    const fetchMock = stubApi({
      'POST /api/auth/refresh': authResponse(account),
      'GET /api/dashboard': dashboard,
      'GET /api/demo/outage': noOutage,
    })
    render(<App />)

    await screen.findByRole('article', { name: 'Acme Cloud (demo)' })

    expect(screen.getByText('someone@acme.example')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sign in' })).toBeNull()
    const [, init] = fetchMock.mock.calls.find(([path]) => path === '/api/dashboard')!
    expect(init?.headers).toMatchObject({ Authorization: 'Bearer jwt-1' })
    expect(fetchMock).not.toHaveBeenCalledWith('/api/me', expect.anything())
  })

  it('upgrades a guest in place, keeping the same dashboard and connection', async () => {
    const fetchMock = stubApi({
      'GET /api/me': () => Response.json(guest),
      'GET /api/dashboard': dashboard,
      'GET /api/demo/outage': noOutage,
      'POST /api/auth/register': authResponse({ ...guest, isGuest: false, email: 'new@acme.example', guestExpiresAt: null }, 201),
    })
    render(<App />)

    await userEvent.click(await screen.findByRole('button', { name: 'Create account to keep it' }))
    expect(screen.getByText('Your dashboard and followed services come with you.')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('Email'), 'new@acme.example')
    await userEvent.type(screen.getByLabelText('Password'), 'correct-horse-battery')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(await screen.findByRole('article', { name: 'Acme Cloud (demo)' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Guest session' })).toBeNull()
    expect(screen.getByRole('region', { name: 'Email confirmation' })).toHaveTextContent('new@acme.example')
    const [, init] = fetchMock.mock.calls.find(([path]) => path === '/api/auth/register')!
    expect(JSON.parse(init?.body as string)).toEqual({ email: 'new@acme.example', password: 'correct-horse-battery' })
  })

  it('signs out and falls back to the guest prompt', async () => {
    let signedIn = true
    const fetchMock = stubApi({
      'POST /api/auth/refresh': () => (signedIn ? authResponse(account)() : new Response(null, { status: 401 })),
      'POST /api/auth/logout': () => {
        signedIn = false
        return new Response(null, { status: 204 })
      },
      'GET /api/me': unauthorized,
      'GET /api/dashboard': dashboard,
      'GET /api/demo/outage': noOutage,
    })
    render(<App />)

    await userEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByRole('button', { name: 'Try it as a guest' })).toBeInTheDocument()
    const [, init] = fetchMock.mock.calls.findLast(([path]) => path === '/api/me')!
    expect(init?.headers).not.toHaveProperty('Authorization')
  })

  describe('Add services', () => {
    const catalog = () =>
      Response.json({
        vendors: [
          {
            id: acme.id,
            slug: acme.slug,
            name: acme.name,
            category: 'cloud',
            statusPageUrl: '/vendors/acme-cloud',
            componentCount: 4,
            followedComponentCount: 4,
          },
        ],
      })
    const routes = {
      'GET /api/me': () => Response.json(guest),
      'GET /api/dashboard': dashboard,
      'GET /api/demo/outage': noOutage,
      'GET /api/catalog': catalog,
    }

    it('moves between the dashboard and the catalog without a page load, and back again', async () => {
      const fetchMock = stubApi(routes)
      render(<App />)
      await screen.findByRole('article', { name: 'Acme Cloud (demo)' })

      const nav = screen.getByRole('navigation', { name: 'Main' })
      await userEvent.click(within(nav).getByRole('link', { name: 'Add services' }))

      expect(await screen.findByRole('heading', { level: 1, name: 'Add services' })).toBeInTheDocument()
      expect(window.location.pathname).toBe('/catalog')
      expect(within(nav).getByRole('link', { name: 'Add services' })).toHaveAttribute('aria-current', 'page')
      expect(within(nav).getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute('aria-current')
      await waitFor(() => expect((connections as FakeConnection[])[0].stopped).toBe(1))

      window.history.back()

      // The dashboard loads a new snapshot, so a follow made on the catalog screen shows up.
      expect(await screen.findByRole('article', { name: 'Acme Cloud (demo)' })).toBeInTheDocument()
      expect(fetchMock.mock.calls.filter(([path]) => path === '/api/dashboard')).toHaveLength(2)
      expect(fetchMock.mock.calls.filter(([path]) => path === '/api/me')).toHaveLength(1)
    })

    it('opens the catalog directly from its address', async () => {
      window.history.replaceState(null, '', '/catalog')
      stubApi(routes)
      render(<App />)

      expect(await screen.findByRole('button', { name: /^Following Acme/ })).toBeInTheDocument()
      expect(connections).toHaveLength(0)
    })

    it('reaches the catalog from the button on the dashboard', async () => {
      stubApi(routes)
      render(<App />)

      await userEvent.click(await screen.findByRole('link', { name: '+ Add services' }))

      expect(await screen.findByRole('heading', { level: 1, name: 'Add services' })).toBeInTheDocument()
    })

    it('asks a visitor with no session to start one, then shows the catalog', async () => {
      window.history.replaceState(null, '', '/catalog')
      stubApi({ ...routes, 'GET /api/me': unauthorized, 'POST /api/guest': () => Response.json(guest, { status: 201 }) })
      render(<App />)

      await userEvent.click(await screen.findByRole('button', { name: 'Try it as a guest' }))

      expect(await screen.findByRole('heading', { level: 1, name: 'Add services' })).toBeInTheDocument()
    })

    it('hides the link until there is a session', async () => {
      stubApi({ 'GET /api/me': unauthorized })
      render(<App />)

      await screen.findByRole('button', { name: 'Try it as a guest' })

      expect(screen.queryByRole('link', { name: 'Add services' })).toBeNull()
    })

    it('goes back to the guest prompt when the catalog says the session is gone', async () => {
      window.history.replaceState(null, '', '/catalog')
      stubApi({ ...routes, 'GET /api/catalog': unauthorized })
      render(<App />)

      expect(await screen.findByRole('button', { name: 'Try it as a guest' })).toBeInTheDocument()
    })

    it('closes the account form when a header link is clicked', async () => {
      stubApi(routes)
      render(<App />)
      await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

      await userEvent.click(screen.getByRole('link', { name: 'Add services' }))

      expect(await screen.findByRole('heading', { level: 1, name: 'Add services' })).toBeInTheDocument()
      expect(screen.queryByLabelText('Password')).toBeNull()
    })
  })

  it('shows the confirm page for an emailed link without looking for a session', async () => {
    window.history.replaceState(null, '', '/confirm-email?token=abc')
    const fetchMock = stubApi({ 'POST /api/auth/confirm-email': () => new Response(null, { status: 204 }) })
    render(<App />)

    expect(fetchMock).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Confirm my email' }))

    expect(await screen.findByRole('heading', { name: 'Your email is confirmed' })).toBeInTheDocument()
    const [, init] = fetchMock.mock.calls[0]
    expect(JSON.parse(init?.body as string)).toEqual({ token: 'abc' })
  })

  it('opens an incident from the dashboard card, and goes back', async () => {
    stubApi({
      'GET /api/me': () => Response.json(guest),
      'GET /api/dashboard': () => Response.json({ vendors: [acmeDown] }),
      'GET /api/demo/outage': noOutage,
      [`GET /api/incidents/${acmeDown.activeIncident.id}`]: () =>
        Response.json({ ...openIncident, id: acmeDown.activeIncident.id }),
    })
    render(<App />)

    await userEvent.click(await screen.findByRole('link', { name: 'Elevated API errors' }))

    expect(window.location.pathname).toBe(`/incidents/${acmeDown.activeIncident.id}`)
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Elevated error rates on the API')

    await userEvent.click(screen.getByRole('link', { name: '← Back to dashboard' }))

    expect(await screen.findByRole('heading', { name: 'Your services' })).toBeInTheDocument()
  })

  it('shows "not found" for an incident address without a valid id, without asking the server', async () => {
    window.history.replaceState(null, '', '/incidents/not-an-id')
    const fetchMock = stubApi({ 'GET /api/me': () => Response.json(guest) })
    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent("We couldn't find that incident.")
    expect(fetchMock).not.toHaveBeenCalledWith(expect.stringContaining('/api/incidents'), expect.anything())
  })
})
