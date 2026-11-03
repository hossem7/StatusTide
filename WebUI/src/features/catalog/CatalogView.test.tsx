import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { acmeDetail, acmeTile, nimbusTile } from '../../test/sampleCatalog'
import { externalUrl, followSummary } from './catalogLabels'
import { CatalogView } from './CatalogPage'
import type { Picker, UseCatalog } from './useCatalog'

const openPicker: Picker = { vendorId: acmeTile.id, vendorName: acmeTile.name, detail: acmeDetail }

function renderView(overrides: Partial<UseCatalog> = {}, extra: { isGuest?: boolean; onCreateAccount?: () => void } = {}) {
  const props: UseCatalog = {
    vendors: [acmeTile, nimbusTile],
    loadFailed: false,
    retry: vi.fn(),
    search: '',
    setSearch: vi.fn(),
    category: null,
    setCategory: vi.fn(),
    picker: null,
    busy: false,
    failure: null,
    announcement: '',
    follow: vi.fn(),
    openPicker: vi.fn(),
    closePicker: vi.fn(),
    saveComponents: vi.fn(),
    unfollow: vi.fn(),
    ...overrides,
  }
  const view = render(<CatalogView {...props} {...extra} />)
  return { ...props, ...view, props }
}

describe('CatalogView', () => {
  it('shows each vendor with its category and whether it is followed', () => {
    renderView({ vendors: [{ ...acmeTile, followedComponentCount: 2 }, nimbusTile] })

    const [acme, nimbus] = screen.getAllByRole('listitem')
    expect(within(acme).getByText('Cloud')).toBeInTheDocument()
    expect(within(acme).getByText('2 of 4 components')).toBeInTheDocument()
    expect(within(acme).getByRole('button', { name: /^Following Acme/ })).toBeEnabled()
    expect(within(nimbus).getByText('Developer tools')).toBeInTheDocument()
    expect(within(nimbus).getByRole('button', { name: 'Follow Nimbus' })).toBeEnabled()
  })

  it('follows from the tile, and opens the picker from a followed tile', async () => {
    const { follow, openPicker: open } = renderView()

    await userEvent.click(screen.getByRole('button', { name: 'Follow Nimbus' }))
    await userEvent.click(screen.getByRole('button', { name: /^Following Acme/ }))

    expect(follow).toHaveBeenCalledWith(nimbusTile)
    expect(open).toHaveBeenCalledWith(acmeTile)
  })

  it("can't follow a vendor that has no components yet", () => {
    renderView({ vendors: [{ ...nimbusTile, componentCount: 0 }] })

    expect(screen.getByRole('button', { name: 'Follow Nimbus' })).toBeDisabled()
    expect(screen.getByText('Not checked yet')).toBeInTheDocument()
  })

  it('sends the typed search and marks the chosen category', async () => {
    const { setSearch, setCategory } = renderView({ category: 'cloud' })

    await userEvent.type(screen.getByLabelText('Search the catalog'), 'a')
    await userEvent.click(screen.getByRole('button', { name: 'AI' }))

    expect(setSearch).toHaveBeenCalledWith('a')
    expect(setCategory).toHaveBeenCalledWith('ai')
    expect(screen.getByRole('button', { name: 'Cloud' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: 'All' })).toHaveAttribute('aria-pressed', 'false')
  })

  it('tells the user when nothing matches', () => {
    renderView({ vendors: [] })

    expect(screen.getByText(/No services match/)).toBeInTheDocument()
  })

  it('offers a retry when the catalog fails to load', async () => {
    const { retry } = renderView({ vendors: null, loadFailed: true })

    await userEvent.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Try again' }))

    expect(retry).toHaveBeenCalled()
    expect(screen.queryByText('Loading the catalog...')).toBeNull()
  })

  it('renders a vendor name as text, never as markup', () => {
    const name = '<img src=x onerror=alert(1)>'
    renderView({ vendors: [{ ...nimbusTile, name }] })

    expect(screen.getByRole('heading', { name })).toBeInTheDocument()
    expect(document.querySelector('img')).toBeNull()
  })

  it('links only to real status pages in the footer note', () => {
    renderView({
      vendors: [
        acmeTile,
        nimbusTile,
        { ...nimbusTile, id: 'bad', name: 'Hostile', statusPageUrl: 'javascript:alert(1)' },
      ],
    })

    const note = screen.getByText(/Statuses come from each vendor's public status page/)
    const links = within(note).getAllByRole('link')
    expect(links.map((a) => a.getAttribute('href'))).toEqual(['https://status.nimbus.example'])
    expect(links[0]).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('still shows the footer note with nothing to link to', () => {
    renderView({ vendors: [acmeTile] })

    expect(screen.getByText("Statuses come from each vendor's public status page.")).toBeInTheDocument()
    expect(screen.queryByRole('link')).toBeNull()
  })

  it('offers a guest at the limit an account', async () => {
    const onCreateAccount = vi.fn()
    renderView(
      { failure: { vendorId: nimbusTile.id, vendorName: nimbusTile.name, reason: 'limit' } },
      { isGuest: true, onCreateAccount },
    )

    const tile = screen.getAllByRole('listitem')[1]
    expect(within(tile).getByRole('alert')).toHaveTextContent('limit for guests')
    await userEvent.click(within(tile).getByRole('button', { name: 'Create account' }))

    expect(onCreateAccount).toHaveBeenCalled()
  })

  it('tells an account at the limit to unfollow one', () => {
    renderView({ failure: { vendorId: nimbusTile.id, vendorName: nimbusTile.name, reason: 'limit' } })

    expect(screen.getByRole('alert')).toHaveTextContent('Unfollow one to add another.')
    expect(screen.queryByRole('button', { name: 'Create account' })).toBeNull()
  })

  it('shows at the top a failure whose tile has gone', () => {
    renderView({ vendors: [acmeTile], failure: { vendorId: nimbusTile.id, vendorName: nimbusTile.name, reason: 'gone' } })

    expect(screen.getByRole('alert')).toHaveTextContent("Nimbus isn't in the catalog any more.")
  })
})

describe('ComponentPicker', () => {
  const picker = () => screen.getByRole('complementary', { name: 'Choose Acme Cloud (demo) components' })

  it('starts with what the user follows, and moves focus to its heading', () => {
    renderView({ picker: openPicker })

    expect(within(picker()).getByLabelText('API')).toBeChecked()
    expect(within(picker()).getByLabelText('Managed Database')).not.toBeChecked()
    expect(within(picker()).getByRole('button', { name: 'Save 2 components' })).toBeEnabled()
    expect(within(picker()).getByRole('heading', { name: 'Acme Cloud (demo) components' })).toHaveFocus()
  })

  it('saves the changed choice', async () => {
    const { saveComponents } = renderView({ picker: openPicker })

    await userEvent.click(screen.getByLabelText('API'))
    await userEvent.click(screen.getByLabelText('Managed Database'))
    await userEvent.click(screen.getByLabelText('Managed Database'))
    await userEvent.click(screen.getByLabelText('Managed Database'))
    await userEvent.click(screen.getByRole('button', { name: 'Save 2 components' }))

    expect(saveComponents).toHaveBeenCalledTimes(1)
    expect(vi.mocked(saveComponents).mock.calls[0][0].toSorted()).toEqual(['a2', 'a3'])
  })

  it("won't save an empty choice", async () => {
    const { saveComponents } = renderView({ picker: openPicker })

    await userEvent.click(screen.getByLabelText('API'))
    await userEvent.click(screen.getByLabelText('Dashboard'))

    expect(screen.getByRole('button', { name: 'Save 0 components' })).toBeDisabled()
    expect(screen.getByText(/Pick at least one/)).toBeInTheDocument()
    expect(saveComponents).not.toHaveBeenCalled()
  })

  it('says "component" for one', async () => {
    renderView({ picker: openPicker })

    await userEvent.click(screen.getByLabelText('API'))

    expect(screen.getByRole('button', { name: 'Save 1 component' })).toBeEnabled()
  })

  it('unfollows and closes', async () => {
    const { unfollow, closePicker } = renderView({ picker: openPicker })

    await userEvent.click(screen.getByRole('button', { name: 'Unfollow' }))
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(unfollow).toHaveBeenCalled()
    expect(closePicker).toHaveBeenCalled()
  })

  it('shows a save failure inside the picker, not on the tile', () => {
    renderView({
      picker: openPicker,
      failure: { vendorId: acmeTile.id, vendorName: acmeTile.name, reason: 'unreachable' },
    })

    expect(within(picker()).getByRole('alert')).toHaveTextContent("couldn't reach the server")
    expect(screen.getAllByRole('alert')).toHaveLength(1)
  })

  it('shows a wait while the components load', () => {
    renderView({ picker: { ...openPicker, detail: null } })

    expect(within(picker()).getByText('Loading components...')).toBeInTheDocument()
    expect(within(picker()).queryByRole('checkbox')).toBeNull()
  })

  it('stays open through a search that hides every tile', () => {
    renderView({ vendors: [], picker: openPicker })

    expect(within(picker()).getByLabelText('API')).toBeChecked()
    expect(screen.getByText(/No services match/)).toBeInTheDocument()
  })

  it('returns focus to the tile when it closes', () => {
    const { props, rerender } = renderView({ picker: openPicker })

    rerender(<CatalogView {...props} picker={null} />)

    expect(screen.getByRole('button', { name: /^Following Acme/ })).toHaveFocus()
  })
})

describe('catalog labels', () => {
  it.each([
    ['https://status.nimbus.example/x', 'https://status.nimbus.example/x'],
    ['http://status.nimbus.example', 'http://status.nimbus.example'],
    ['/vendors/acme-cloud', null],
    ['javascript:alert(1)', null],
    ['data:text/html,hi', null],
    ['', null],
  ])('externalUrl(%s) is %s', (url, expected) => {
    expect(externalUrl(url)).toBe(expected)
  })

  it.each([
    [4, 4, 'All 4 components'],
    [1, 1, 'The 1 component'],
    [4, 1, '1 of 4 components'],
  ])('summarises %i components with %i followed', (componentCount, followedComponentCount, text) => {
    expect(followSummary({ ...acmeTile, componentCount, followedComponentCount })).toBe(text)
  })
})
