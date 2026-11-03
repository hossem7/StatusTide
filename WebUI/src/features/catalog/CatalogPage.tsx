import { useEffect, useRef, type ReactNode } from 'react'
import type { CatalogVendor } from '../../api/types'
import { initialsOf } from '../dashboard/format'
import { categories, categoryLabel, externalUrl, followSummary } from './catalogLabels'
import { ComponentPicker } from './ComponentPicker'
import { useCatalog, type FollowFailure, type UseCatalog } from './useCatalog'
import './Catalog.css'

interface PageProps {
  isGuest: boolean
  onSignedOut: () => void
  onCreateAccount?: () => void
}

export function CatalogPage({ isGuest, onSignedOut, onCreateAccount }: PageProps) {
  const catalog = useCatalog({ onSignedOut })
  return <CatalogView {...catalog} isGuest={isGuest} onCreateAccount={onCreateAccount} />
}

function failureText(reason: FollowFailure, vendorName: string, isGuest: boolean): string {
  switch (reason) {
    case 'limit':
      return isGuest
        ? "You've reached the limit for guests. Create an account to follow more."
        : "You've reached the limit of services you can follow. Unfollow one to add another."
    case 'gone':
      return `${vendorName} isn't in the catalog any more.`
    case 'changed':
      return `The components of ${vendorName} have changed. Check your choice and save again.`
    case 'tooMany':
      return 'Too many tries. Wait a minute and try again.'
    case 'unreachable':
      return "We couldn't reach the server. Try again."
  }
}

const tileButtonId = (vendorId: string) => `catalog-follow-${vendorId}`

/** The screen itself, with no requests or timers, so tests can render any state directly. */
export function CatalogView({
  vendors,
  loadFailed,
  retry,
  search,
  setSearch,
  category,
  setCategory,
  picker,
  busy,
  failure,
  announcement,
  follow,
  openPicker,
  closePicker,
  saveComponents,
  unfollow,
  isGuest = false,
  onCreateAccount,
}: UseCatalog & { isGuest?: boolean; onCreateAccount?: () => void }) {
  // Closing the picker removes the focused element, so focus goes back to the tile it came from.
  const pickerVendorId = picker?.vendorId
  const lastPickerVendorId = useRef(pickerVendorId)
  useEffect(() => {
    if (!pickerVendorId && lastPickerVendorId.current) {
      document.getElementById(tileButtonId(lastPickerVendorId.current))?.focus()
    }
    lastPickerVendorId.current = pickerVendorId
  }, [pickerVendorId])

  const failureNote: ReactNode = failure && (
    <div className="catalog__error" role="alert">
      <p>{failureText(failure.reason, failure.vendorName, isGuest)}</p>
      {failure.reason === 'limit' && isGuest && (
        <button type="button" className="button button--primary" onClick={onCreateAccount}>
          Create account
        </button>
      )}
    </div>
  )
  // The note sits next to the control that failed, or at the top when that control has gone.
  const failureInPicker = failure !== null && failure.vendorId === pickerVendorId
  const failureOnTile = failure !== null && !failureInPicker && vendors?.some((v) => v.id === failure.vendorId) === true

  const statusPages = (vendors ?? []).flatMap((vendor) => {
    const url = externalUrl(vendor.statusPageUrl)
    return url ? [{ id: vendor.id, name: vendor.name, url }] : []
  })

  return (
    <main className="catalog">
      <div className="catalog__top">
        <div>
          <h1>Add services</h1>
          <p>Pick from the catalog, then choose only the components you use.</p>
        </div>
        <label className="catalog__search">
          Search the catalog
          <input
            type="search"
            placeholder="Service name"
            maxLength={100}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
      </div>

      <div className="catalog__categories" role="group" aria-label="Categories">
        <CategoryButton label="All" pressed={category === null} onClick={() => setCategory(null)} />
        {categories.map(({ value, label }) => (
          <CategoryButton key={value} label={label} pressed={category === value} onClick={() => setCategory(value)} />
        ))}
      </div>

      {/* Announces changes the screen shows elsewhere, such as "Following Acme Cloud (demo)". */}
      <p className="visually-hidden" aria-live="polite">
        {announcement}
      </p>

      {failure && !failureInPicker && !failureOnTile && failureNote}

      {loadFailed && (
        <div className="catalog__message" role="alert">
          <p>We couldn't load the catalog.</p>
          <button type="button" className="button" onClick={retry}>
            Try again
          </button>
        </div>
      )}

      {vendors === null && !loadFailed && <p className="catalog__message">Loading the catalog...</p>}

      {vendors?.length === 0 && !loadFailed && (
        <p className="catalog__message">No services match. Try another name or category.</p>
      )}

      {/* An open picker stays through a new search, even one that hides every tile. */}
      {((vendors !== null && vendors.length > 0) || picker) && (
        <div className="catalog__body">
          {vendors !== null && vendors.length > 0 && (
            <ul className="catalog__tiles">
              {vendors.map((vendor) => (
                <VendorTile
                  key={vendor.id}
                  vendor={vendor}
                  busy={busy}
                  onFollow={() => follow(vendor)}
                  onOpen={() => openPicker(vendor)}
                  error={failureOnTile && failure.vendorId === vendor.id ? failureNote : null}
                />
              ))}
            </ul>
          )}
          {picker && (
            <ComponentPicker
              picker={picker}
              busy={busy}
              error={failureInPicker ? failureNote : null}
              onSave={saveComponents}
              onUnfollow={unfollow}
              onClose={closePicker}
            />
          )}
        </div>
      )}

      <p className="catalog__footnote">
        Statuses come from each vendor's public status page
        {statusPages.length === 0 && '.'}
        {statusPages.length > 0 && ': '}
        {statusPages.map(({ id, name, url }, index) => (
          <span key={id}>
            <a href={url} target="_blank" rel="noopener noreferrer">
              {name}
            </a>
            {index < statusPages.length - 1 ? ', ' : '.'}
          </span>
        ))}
      </p>
    </main>
  )
}

function CategoryButton({ label, pressed, onClick }: { label: string; pressed: boolean; onClick: () => void }) {
  return (
    <button type="button" className="catalog__category" aria-pressed={pressed} onClick={onClick}>
      {label}
    </button>
  )
}

function VendorTile({
  vendor,
  busy,
  onFollow,
  onOpen,
  error,
}: {
  vendor: CatalogVendor
  busy: boolean
  onFollow: () => void
  onOpen: () => void
  error: ReactNode
}) {
  const following = vendor.followedComponentCount > 0
  // A vendor's components arrive with its first poll, and until then there is nothing to follow.
  const empty = vendor.componentCount === 0
  return (
    <li className="catalog-tile" data-following={following}>
      <div className="catalog-tile__row">
        <span className="catalog-tile__initials" aria-hidden="true">
          {initialsOf(vendor.name)}
        </span>
        <div className="catalog-tile__title">
          <h2>{vendor.name}</h2>
          <p>{categoryLabel(vendor.category)}</p>
          {following && <p>{followSummary(vendor)}</p>}
          {empty && <p>Not checked yet</p>}
        </div>
        {following ? (
          <button
            id={tileButtonId(vendor.id)}
            type="button"
            className="button catalog-tile__following"
            aria-label={`Following ${vendor.name}, choose components`}
            disabled={busy}
            onClick={onOpen}
          >
            Following
          </button>
        ) : (
          <button
            id={tileButtonId(vendor.id)}
            type="button"
            className="button"
            aria-label={`Follow ${vendor.name}`}
            disabled={busy || empty}
            onClick={onFollow}
          >
            Follow
          </button>
        )}
      </div>
      {error}
    </li>
  )
}
