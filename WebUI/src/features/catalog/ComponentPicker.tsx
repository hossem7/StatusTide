import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import type { CatalogVendorDetail } from '../../api/types'
import type { Picker } from './useCatalog'

interface Props {
  picker: Picker
  busy: boolean
  /** Why the last change to this vendor failed, ready to show. */
  error?: ReactNode
  onSave: (componentIds: string[]) => void
  onUnfollow: () => void
  onClose: () => void
}

/** Chooses which of a followed vendor's components count. Keeps its choices until it closes. */
export function ComponentPicker({ picker, busy, error, onSave, onUnfollow, onClose }: Props) {
  const headingRef = useRef<HTMLHeadingElement>(null)

  // Below the tiles on a phone, so moving focus here also brings it into view.
  useEffect(() => {
    headingRef.current?.focus()
  }, [picker.vendorId])

  return (
    <aside className="component-picker" aria-label={`Choose ${picker.vendorName} components`}>
      <div>
        <h2 ref={headingRef} tabIndex={-1}>
          {picker.vendorName} components
        </h2>
        <p>You'll only get alerts for the ones you pick.</p>
      </div>
      {picker.detail ? (
        <Choices
          // A new load means a new list, so old choices don't carry over.
          key={picker.vendorId}
          detail={picker.detail}
          busy={busy}
          error={error}
          onSave={onSave}
          onUnfollow={onUnfollow}
          onClose={onClose}
        />
      ) : (
        <>
          <p>Loading components...</p>
          {error}
          <button type="button" className="button" onClick={onClose}>
            Close
          </button>
        </>
      )}
    </aside>
  )
}

function Choices({
  detail,
  busy,
  error,
  onSave,
  onUnfollow,
  onClose,
}: Omit<Props, 'picker'> & { detail: CatalogVendorDetail }) {
  const [chosen, setChosen] = useState(() => new Set(detail.components.filter((c) => c.followed).map((c) => c.id)))

  const toggle = (id: string) => {
    const next = new Set(chosen)
    if (!next.delete(id)) next.add(id)
    setChosen(next)
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    onSave([...chosen])
  }

  return (
    <form onSubmit={onSubmit}>
      <div className="component-picker__list">
        {detail.components.map((component) => (
          <label key={component.id}>
            <input type="checkbox" checked={chosen.has(component.id)} onChange={() => toggle(component.id)} />
            <span>{component.name}</span>
          </label>
        ))}
      </div>
      {chosen.size === 0 && <p>Pick at least one, or unfollow this service.</p>}
      {error}
      <button type="submit" className="button button--primary" disabled={busy || chosen.size === 0}>
        Save {chosen.size} {chosen.size === 1 ? 'component' : 'components'}
      </button>
      <div className="component-picker__actions">
        <button type="button" className="button" disabled={busy} onClick={onUnfollow}>
          Unfollow
        </button>
        <button type="button" className="button" onClick={onClose}>
          Close
        </button>
      </div>
    </form>
  )
}
