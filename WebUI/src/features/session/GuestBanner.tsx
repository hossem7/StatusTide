import './Session.css'

/** Tells a guest how long the dashboard lasts, and how to keep it. */
export function GuestBanner({ onCreateAccount }: { onCreateAccount?: () => void }) {
  return (
    <section className="guest-banner" aria-label="Guest session">
      <p>You're browsing as a guest. We keep this dashboard for 30 days after your last visit.</p>
      <button type="button" className="button button--primary" onClick={onCreateAccount}>
        Create account to keep it
      </button>
    </section>
  )
}
