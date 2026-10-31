import './AppHeader.css'

/** The app bar. Links to pages that don't exist yet (Add services) arrive with those pages. */
export function AppHeader({ isGuest }: { isGuest: boolean }) {
  return (
    <header className="app-header">
      <div className="app-header__inner">
        <a className="app-header__brand" href="/">
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
        </a>
        <nav aria-label="Main" className="app-header__nav">
          <a href="/" aria-current="page" className="app-header__link">
            Dashboard
          </a>
          {isGuest && <span className="app-header__badge">Guest</span>}
        </nav>
      </div>
    </header>
  )
}
