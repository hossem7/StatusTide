import './Session.css'

/** Tells a guest how long the dashboard lasts. The button turns on with accounts (#10). */
export function GuestBanner() {
  return (
    <section className="guest-banner" aria-label="Guest session">
      <p>You're browsing as a guest. We keep this dashboard for 30 days after your last visit.</p>
      <button type="button" className="button button--primary" disabled>
        Create account to keep it
      </button>
    </section>
  )
}
