import { useState, type FormEvent } from 'react'
import { api, ApiError } from '../../api/client'
import type { Me } from '../../api/types'
import './Session.css'

export type AccountFormMode = 'signUp' | 'signIn'

interface Props {
  mode: AccountFormMode
  /** A guest's follows carry over when they sign up, so the form says so. */
  isGuest: boolean
  onDone: (me: Me) => void
  onCancel: () => void
  onSwitch: (mode: AccountFormMode) => void
  submit?: { signUp: typeof api.register; signIn: typeof api.login }
}

function errorMessage(mode: AccountFormMode, e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 429) return 'Too many tries from this address. Wait a minute and try again.'
    if (mode === 'signIn' && e.status === 401) return 'Wrong email or password.'
    if (mode === 'signUp' && e.status === 409) return 'That email already has an account. Sign in instead.'
    if (mode === 'signUp' && e.status === 400) return 'Enter a valid email and a password of at least 10 characters.'
  }
  return "We couldn't reach the server. Try again."
}

/** Sign up or sign in. Both are one email and one password, so they share a form. */
export function AccountForm({
  mode,
  isGuest,
  onDone,
  onCancel,
  onSwitch,
  submit = { signUp: api.register, signIn: api.login },
}: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')
  const signingUp = mode === 'signUp'

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    setSending(true)
    setError('')
    submit[mode](email, password)
      .then(onDone)
      .catch((e: unknown) => {
        setError(errorMessage(mode, e))
        setSending(false)
      })
  }

  const switchTo = (next: AccountFormMode) => {
    setError('')
    onSwitch(next)
  }

  return (
    <main className="account-form">
      <h1>{signingUp ? 'Create your account' : 'Sign in'}</h1>
      {signingUp && isGuest && <p className="account-form__note">Your dashboard and followed services come with you.</p>}
      <form onSubmit={onSubmit}>
        <label>
          Email
          <input
            type="email"
            name="email"
            autoComplete="email"
            required
            maxLength={320}
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
        </label>
        <label>
          Password
          <input
            type="password"
            name="password"
            autoComplete={signingUp ? 'new-password' : 'current-password'}
            required
            minLength={signingUp ? 10 : undefined}
            maxLength={128}
            aria-describedby={signingUp ? 'password-hint' : undefined}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </label>
        {signingUp && (
          <p id="password-hint" className="account-form__note">
            At least 10 characters.
          </p>
        )}
        {error && (
          <p className="account-form__error" role="alert">
            {error}
          </p>
        )}
        <div className="account-form__actions">
          <button type="submit" className="button button--primary" disabled={sending}>
            {signingUp ? 'Create account' : 'Sign in'}
          </button>
          <button type="button" className="button" onClick={onCancel}>
            Back
          </button>
        </div>
      </form>
      <p className="account-form__note">
        {signingUp ? 'Already have an account?' : 'New to StatusTide?'}{' '}
        <button type="button" className="account-form__switch" onClick={() => switchTo(signingUp ? 'signIn' : 'signUp')}>
          {signingUp ? 'Sign in' : 'Create an account'}
        </button>
      </p>
    </main>
  )
}
