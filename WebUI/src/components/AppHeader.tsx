import { catalogPath } from '../features/catalog/catalogLabels'
import { AppLink } from './AppLink'
import { usePath } from './navigation'
import './AppHeader.css'

/** The app bar. Links to pages that don't exist yet arrive with those pages. */
export function AppHeader({
  isGuest,
  email,
  hasSession = false,
  onNavigate,
  onSignIn,
  onSignOut,
}: {
  isGuest: boolean
  /** The account's address. Shown with a sign-out button when set. */
  email?: string | null
  /** Add services needs a session, since it shows what the user follows. */
  hasSession?: boolean
  /** Called when one of the header's links is clicked. */
  onNavigate?: () => void
  /** Shows a sign-in button when set. */
  onSignIn?: () => void
  onSignOut?: () => void
}) {
  const path = usePath()
  return (
    <header className="app-header">
      <div className="app-header__inner">
        <AppLink className="app-header__brand" href="/" onClick={onNavigate}>
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <polyline
              points="3 12 7 12 10 5 14 19 17 12 21 12"
              stroke="currentColor"
              strokeWidth="2.2"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
          <span>StatusTide</span>
        </AppLink>
        <nav aria-label="Main" className="app-header__nav">
          <AppLink
            href="/"
            aria-current={path === '/' ? 'page' : undefined}
            className="app-header__link"
            onClick={onNavigate}
          >
            Dashboard
          </AppLink>
          {hasSession && (
            <AppLink
              href={catalogPath}
              aria-current={path === catalogPath ? 'page' : undefined}
              className="app-header__link"
              onClick={onNavigate}
            >
              Add services
            </AppLink>
          )}
          {isGuest && <span className="app-header__badge">Guest</span>}
          {onSignIn && (
            <button type="button" className="app-header__link app-header__button" onClick={onSignIn}>
              Sign in
            </button>
          )}
          {email && <span className="app-header__email">{email}</span>}
          {email && onSignOut && (
            <button type="button" className="app-header__link app-header__button" onClick={onSignOut}>
              Sign out
            </button>
          )}
        </nav>
      </div>
    </header>
  )
}
