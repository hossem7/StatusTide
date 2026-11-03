import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { Catalog, CatalogQuery, CatalogVendorDetail } from '../../api/types'
import { acmeDetail, acmeTile, nimbusTile } from '../../test/sampleCatalog'
import { useCatalog } from './useCatalog'

const followedAll: CatalogVendorDetail = {
  ...acmeDetail,
  components: acmeDetail.components.map((c) => ({ ...c, followed: true })),
}
const conflict = new ApiError(409, 'PUT', '/api/follows/x')

function setup(overrides: Partial<Parameters<typeof useCatalog>[0]> = {}) {
  const load = vi.fn(
    async (_query: CatalogQuery, _signal: AbortSignal): Promise<Catalog> => ({ vendors: [acmeTile, nimbusTile] }),
  )
  const loadVendor = vi.fn(async () => followedAll)
  const setFollows = vi.fn(async () => followedAll)
  const removeFollows = vi.fn(async () => {})
  const onSignedOut = vi.fn()
  const hook = renderHook(() => useCatalog({ load, loadVendor, setFollows, removeFollows, onSignedOut, ...overrides }))
  return { ...hook, load, loadVendor, setFollows, removeFollows, onSignedOut }
}

async function loaded(result: { current: ReturnType<typeof useCatalog> }) {
  await waitFor(() => expect(result.current.vendors).not.toBeNull())
}

afterEach(() => {
  vi.useRealTimers()
})

describe('useCatalog', () => {
  it('waits for typing to stop before searching, and trims the search', async () => {
    vi.useFakeTimers()
    const { result, load } = setup()
    await act(() => vi.advanceTimersByTimeAsync(0))
    load.mockClear()

    act(() => result.current.setSearch(' ac'))
    await act(() => vi.advanceTimersByTimeAsync(200))
    act(() => result.current.setSearch(' acm '))
    await act(() => vi.advanceTimersByTimeAsync(200))
    expect(load).not.toHaveBeenCalled()

    await act(() => vi.advanceTimersByTimeAsync(100))
    expect(load).toHaveBeenCalledTimes(1)
    expect(load.mock.calls[0][0]).toEqual({ search: 'acm', category: null })
  })

  it('loads at once when the category changes', async () => {
    const { result, load } = setup()
    await loaded(result)

    act(() => result.current.setCategory('cloud'))

    await waitFor(() => expect(load).toHaveBeenLastCalledWith({ search: '', category: 'cloud' }, expect.anything()))
  })

  it('ignores an older search that answers after a newer one', async () => {
    const answers: ((catalog: Catalog) => void)[] = []
    const load = vi.fn(() => new Promise<Catalog>((resolve) => answers.push(resolve)))
    const { result } = setup({ load })

    act(() => result.current.setCategory('cloud'))
    await waitFor(() => expect(answers).toHaveLength(2))
    await act(async () => {
      answers[1]({ vendors: [acmeTile] })
      answers[0]({ vendors: [nimbusTile] })
    })

    expect(result.current.vendors).toEqual([acmeTile])
  })

  it('reports a failed load and recovers on retry', async () => {
    const load = vi
      .fn<(query: CatalogQuery, signal: AbortSignal) => Promise<Catalog>>()
      .mockRejectedValueOnce(new ApiError(503, 'GET', '/api/catalog'))
      .mockResolvedValue({ vendors: [acmeTile] })
    const { result } = setup({ load })
    await waitFor(() => expect(result.current.loadFailed).toBe(true))

    act(() => result.current.retry())

    await waitFor(() => expect(result.current.loadFailed).toBe(false))
    expect(result.current.vendors).toEqual([acmeTile])
  })

  it('follows everything with one click, then opens the picker', async () => {
    const { result, setFollows } = setup()
    await loaded(result)

    act(() => result.current.follow(nimbusTile))

    await waitFor(() => expect(result.current.picker?.detail).toEqual(followedAll))
    expect(setFollows).toHaveBeenCalledWith(nimbusTile.id, null)
    expect(result.current.busy).toBe(false)
    expect(result.current.announcement).toContain('Following Nimbus')
  })

  it('updates the tile at once and loads the list again after a follow', async () => {
    const answers: ((catalog: Catalog) => void)[] = []
    const load = vi.fn(() => new Promise<Catalog>((resolve) => answers.push(resolve)))
    const setFollows = vi.fn(async () => followedAll)
    const { result } = setup({ load, setFollows })
    await act(async () => answers[0]({ vendors: [{ ...acmeTile, followedComponentCount: 0 }] }))

    act(() => result.current.follow({ ...acmeTile, followedComponentCount: 0 }))

    // The second load is still unanswered, so this count comes from the follow's own answer.
    await waitFor(() => expect(result.current.vendors?.[0].followedComponentCount).toBe(3))
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2))
  })

  it('saves the chosen components and closes the picker', async () => {
    const { result, setFollows } = setup()
    await loaded(result)
    act(() => result.current.openPicker(acmeTile))
    await waitFor(() => expect(result.current.picker?.detail).not.toBeNull())

    act(() => result.current.saveComponents(['a1']))

    await waitFor(() => expect(result.current.picker).toBeNull())
    expect(setFollows).toHaveBeenCalledWith(acmeTile.id, ['a1'])
  })

  it('unfollows, and the tile goes back to not following', async () => {
    // The first load answers; the one after the unfollow never does, so the count is the hook's own.
    const load = vi
      .fn<(query: CatalogQuery, signal: AbortSignal) => Promise<Catalog>>()
      .mockResolvedValueOnce({ vendors: [acmeTile, nimbusTile] })
      .mockReturnValue(new Promise<Catalog>(() => {}))
    const { result, removeFollows } = setup({ load })
    await loaded(result)
    act(() => result.current.openPicker(acmeTile))
    await waitFor(() => expect(result.current.picker?.detail).not.toBeNull())

    act(() => result.current.unfollow())

    await waitFor(() => expect(result.current.picker).toBeNull())
    expect(removeFollows).toHaveBeenCalledWith(acmeTile.id)
    expect(result.current.vendors?.[0].followedComponentCount).toBe(0)
    expect(result.current.announcement).toBe('Stopped following Acme Cloud (demo).')
  })

  it('reports the follow limit', async () => {
    const { result } = setup({ setFollows: vi.fn().mockRejectedValue(conflict) })
    await loaded(result)

    act(() => result.current.follow(nimbusTile))

    await waitFor(() => expect(result.current.failure?.reason).toBe('limit'))
    expect(result.current.failure?.vendorId).toBe(nimbusTile.id)
    expect(result.current.picker).toBeNull()
    expect(result.current.busy).toBe(false)
  })

  it('closes the picker and reloads when the vendor has left the catalog', async () => {
    const { result, load } = setup({ setFollows: vi.fn().mockRejectedValue(new ApiError(404, 'PUT', '/x')) })
    await loaded(result)
    act(() => result.current.openPicker(acmeTile))
    await waitFor(() => expect(result.current.picker?.detail).not.toBeNull())
    load.mockClear()

    act(() => result.current.saveComponents(['a1']))

    await waitFor(() => expect(result.current.failure?.reason).toBe('gone'))
    expect(result.current.picker).toBeNull()
    await waitFor(() => expect(load).toHaveBeenCalled())
  })

  it('loads the components again when they changed while the picker was open', async () => {
    const { result, loadVendor } = setup({ setFollows: vi.fn().mockRejectedValue(new ApiError(400, 'PUT', '/x')) })
    await loaded(result)
    act(() => result.current.openPicker(acmeTile))
    await waitFor(() => expect(result.current.picker?.detail).not.toBeNull())
    loadVendor.mockClear()

    act(() => result.current.saveComponents(['a1']))

    await waitFor(() => expect(result.current.failure?.reason).toBe('changed'))
    expect(loadVendor).toHaveBeenCalledWith(acmeTile.id, expect.anything())
    await waitFor(() => expect(result.current.picker?.detail).not.toBeNull())
    expect(result.current.failure?.reason).toBe('changed')
  })

  it('ignores a picker load that answers after the picker closed', async () => {
    let answer: (detail: CatalogVendorDetail) => void = () => {}
    const loadVendor = vi.fn(() => new Promise<CatalogVendorDetail>((resolve) => (answer = resolve)))
    const { result } = setup({ loadVendor })
    await loaded(result)
    act(() => result.current.openPicker(acmeTile))
    expect(result.current.busy).toBe(true)

    act(() => result.current.closePicker())
    await act(async () => answer(followedAll))

    expect(result.current.picker).toBeNull()
    expect(result.current.busy).toBe(false)
  })

  it.each([
    ['the list', () => {}, { load: vi.fn().mockRejectedValue(new ApiError(401, 'GET', '/x')) }],
    [
      'a follow',
      (current: ReturnType<typeof useCatalog>) => current.follow(nimbusTile),
      { setFollows: vi.fn().mockRejectedValue(new ApiError(401, 'PUT', '/x')) },
    ],
  ])('signs out when %s answers 401', async (_, action, overrides) => {
    const { result, onSignedOut } = setup(overrides)
    act(() => action(result.current))

    await waitFor(() => expect(onSignedOut).toHaveBeenCalled())
    expect(result.current.failure).toBeNull()
    expect(result.current.loadFailed).toBe(false)
  })
})
