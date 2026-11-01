import type { UseSession } from './useSession'
import './Session.css'

/** What a visitor with no session sees. The full landing page (design screen 1) replaces it later. */
export function GuestPrompt({ session, onCreateAccount }: { session: UseSession; onCreateAccount?: () => void }) {
  const { starting, startError, startGuest } = session
  return (
    <main className="guest-prompt">
      <h1>Is it down, or is it just you?</h1>
      <p className="guest-prompt__lead">
        Follow the services your stack depends on. StatusTide reads their official status pages, runs its own
        checks, and tells you the moment something breaks.
      </p>
      <div className="guest-prompt__actions">
        <button type="button" className="button button--primary" onClick={startGuest} disabled={starting}>
          Try it as a guest
        </button>
        <button type="button" className="button" onClick={onCreateAccount}>
          Create an account
        </button>
      </div>
      {startError && (
        <p className="guest-prompt__error" role="alert">
          {startError}
        </p>
      )}
      <p className="guest-prompt__note">
        No sign-up needed. We keep your guest dashboard for 30 days after your last visit.
      </p>
    </main>
  )
}
