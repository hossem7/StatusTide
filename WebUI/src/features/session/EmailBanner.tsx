import { useState } from 'react'
import { api, ApiError } from '../../api/client'
import './Session.css'

type ResendState = 'idle' | 'sending' | 'sent' | 'limited' | 'failed'

const resendText: Partial<Record<ResendState, string>> = {
  sent: "We've sent it. It can take a minute, so check your spam folder too.",
  limited: 'Too many tries. Wait a minute and try again.',
  failed: "We couldn't send that. Try again.",
}

/** Reminds an account to confirm its email, since alerts only go to confirmed addresses. */
export function EmailBanner({ email, resend = api.resendConfirmation }: { email: string; resend?: () => Promise<void> }) {
  const [state, setState] = useState<ResendState>('idle')

  const onResend = () => {
    setState('sending')
    resend()
      .then(() => setState('sent'))
      .catch((e: unknown) => setState(e instanceof ApiError && e.status === 429 ? 'limited' : 'failed'))
  }

  return (
    <section className="guest-banner" aria-label="Email confirmation">
      <p>
        Confirm your email to turn on alerts. We sent a link to {email}.
        {resendText[state] && <span role="status"> {resendText[state]}</span>}
      </p>
      <button type="button" className="button" onClick={onResend} disabled={state === 'sending' || state === 'sent'}>
        Send it again
      </button>
    </section>
  )
}
