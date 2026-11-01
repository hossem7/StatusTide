import { afterEach, describe, expect, it, vi } from 'vitest'
import { account, guest } from '../test/sampleCards'
import { api, ApiError, clearAccess } from './client'

const auth = (token: string, expiresIn = 900_000) =>
  Response.json({ accessToken: token, expiresAt: new Date(Date.now() + expiresIn).toISOString(), user: account })

const status = (code: number) => new Response(null, { status: code })

/** Replies with each response in turn, and records what was sent. */
function stubFetch(...responses: Response[]) {
  const fetchMock = vi.fn(async (_path: string, _init?: RequestInit) => responses.shift() ?? status(500))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const bearerOf = (init?: RequestInit) => new Headers(init?.headers).get('Authorization') ?? undefined

afterEach(() => {
  vi.unstubAllGlobals()
  clearAccess()
})

describe('api sessions', () => {
  it('restores an account through the refresh cookie first', async () => {
    const fetchMock = stubFetch(auth('jwt-1'))

    await expect(api.restoreSession()).resolves.toEqual(account)

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/auth/refresh')
  })

  it('falls back to the guest cookie when there is no refresh cookie', async () => {
    const fetchMock = stubFetch(status(401), Response.json(guest))

    await expect(api.restoreSession()).resolves.toEqual(guest)

    expect(fetchMock.mock.calls[1][0]).toBe('/api/me')
    expect(bearerOf(fetchMock.mock.calls[1][1])).toBeUndefined()
  })

  it('reports a server failure instead of treating it as signed out', async () => {
    stubFetch(status(503))

    await expect(api.restoreSession()).rejects.toMatchObject({ status: 503 })
  })

  it('shares one refresh between requests made together', async () => {
    const fetchMock = stubFetch(auth('jwt-1'))

    await Promise.all([api.restoreSession(), api.restoreSession()])

    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('refreshes once and repeats a request whose token expired', async () => {
    const fetchMock = stubFetch(auth('old'))
    await api.login('a@acme.example', 'correct-horse-battery')
    fetchMock.mockClear()
    stubResponses(fetchMock, status(401), auth('new'), Response.json({ vendors: [] }))

    await expect(api.getDashboard()).resolves.toEqual({ vendors: [] })

    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual(['/api/dashboard', '/api/auth/refresh', '/api/dashboard'])
    expect(bearerOf(fetchMock.mock.calls[0][1])).toBe('Bearer old')
    expect(bearerOf(fetchMock.mock.calls[2][1])).toBe('Bearer new')
  })

  it('gives up with a 401 when the refresh fails, and forgets the token', async () => {
    const fetchMock = stubFetch(auth('old'))
    await api.login('a@acme.example', 'correct-horse-battery')
    stubResponses(fetchMock, status(401), status(401))

    await expect(api.getDashboard()).rejects.toMatchObject({ status: 401 })

    await expect(api.getHubToken()).resolves.toBe('')
  })

  it('does not try to refresh after a wrong password', async () => {
    const fetchMock = stubFetch(status(401))

    const failure = await api.login('a@acme.example', 'wrong').catch((e: unknown) => e)

    expect(failure).toBeInstanceOf(ApiError)
    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('does not refresh a guest whose request gets a 401', async () => {
    const fetchMock = stubFetch(status(401))

    await expect(api.getDashboard()).rejects.toMatchObject({ status: 401 })

    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('forgets the token on sign-out, even when the request fails', async () => {
    const fetchMock = stubFetch(auth('jwt-1'))
    await api.login('a@acme.example', 'correct-horse-battery')
    stubResponses(fetchMock, status(500))

    await expect(api.logout()).rejects.toMatchObject({ status: 500 })

    await expect(api.getHubToken()).resolves.toBe('')
  })

  it('accepts answers with no body', async () => {
    stubFetch(status(204))

    await expect(api.confirmEmail('abc')).resolves.toBeUndefined()
  })
})

describe('api.getHubToken', () => {
  it('is empty for a guest and asks the server nothing', async () => {
    const fetchMock = stubFetch()

    await expect(api.getHubToken()).resolves.toBe('')

    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('returns a token with plenty of time left as it is', async () => {
    const fetchMock = stubFetch(auth('jwt-1'))
    await api.login('a@acme.example', 'correct-horse-battery')
    fetchMock.mockClear()

    await expect(api.getHubToken()).resolves.toBe('jwt-1')

    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('refreshes a token that is about to expire', async () => {
    const fetchMock = stubFetch(auth('old', 5_000))
    await api.login('a@acme.example', 'correct-horse-battery')
    stubResponses(fetchMock, auth('new'))

    await expect(api.getHubToken()).resolves.toBe('new')
  })

  it('never throws when that refresh fails, since SignalR would stop reconnecting', async () => {
    const fetchMock = stubFetch(auth('old', 5_000))
    await api.login('a@acme.example', 'correct-horse-battery')
    stubResponses(fetchMock, status(503))

    await expect(api.getHubToken()).resolves.toBe('old')
  })
})

function stubResponses(fetchMock: ReturnType<typeof stubFetch>, ...responses: Response[]) {
  fetchMock.mockImplementation(async () => responses.shift() ?? status(500))
}
