import './Header.css'

function Header() {
  return (
    <header className="site-header">
      <div className="site-header__inner">
        <span className="site-header__logo" aria-hidden="true">
          ST
        </span>
        <div>
          <h1 className="site-header__title">StatusTide</h1>
          <p className="site-header__tagline">
            One dashboard for the status of every service your stack depends on.
          </p>
        </div>
      </div>
    </header>
  )
}

export default Header
