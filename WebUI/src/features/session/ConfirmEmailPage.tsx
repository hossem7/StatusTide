import { useState } from 'react'
import { api, ApiError } from '../../api/client'
import './Session.css'

export const confirmEmailPath = '/confirm-email'

type ConfirmState = 'ready' | 'sending' | 'confirmed' | 'expired' | 'failed'

/**
 * Where the emailed link lands. Confirming takes a click rather than happening on page load,
 * because mail scanners open links and some run scripts. They would use up the one-time token,
 * and the person would then be told their link no longer works.
 */
export function ConfirmEmailPage({ token, confirm = api.confirmEmail }: { token: string | null; confirm?: (token: string) => Promise<void> }) {
  const [state, setState] = useState<ConfirmState>(token ? 'ready' : 'expired')

  const onConfirm = () => {
    setState('sending')
    confirm(token!)
      .then(() => setState('confirmed'))
      .catch((e: unknown) => setState(e instanceof ApiError && e.status === 400 ? 'expired' : 'failed'))
  }

  return (
    <main className="account-form">
      {state === 'confirmed' ? (
        <>
          <h1>Your email is confirmed</h1>
          <p role="status">Alerts can now reach you at this address.</p>
          <a className="button button--primary account-form__link" href="/">
            Go to your dashboard
          </a>
        </>
      ) : state === 'expired' ? (
        <>
          <h1>This link no longer works</h1>
          <p role="alert">It has expired or was already used. You can ask for a new one from your dashboard.</p>
          <a className="button account-form__link" href="/">
            Go to your dashboard
          </a>
        </>
      ) : (
        <>
          <h1>Confirm your email</h1>
          {state === 'failed' && (
            <p className="account-form__error" role="alert">
              We couldn't reach the server. Try again.
            </p>
          )}
          <div className="account-form__actions">
            <button type="button" className="button button--primary" onClick={onConfirm} disabled={state === 'sending'}>
              Confirm my email
            </button>
          </div>
        </>
      )}
    </main>
  )
}
