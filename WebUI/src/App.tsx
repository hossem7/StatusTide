import { useState } from 'react'
import type { Me } from './api/types'
import { AppHeader } from './components/AppHeader'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { AccountForm, type AccountFormMode } from './features/session/AccountForm'
import { ConfirmEmailPage, confirmEmailPath } from './features/session/ConfirmEmailPage'
import { GuestPrompt } from './features/session/GuestPrompt'
import { useSession, type UseSession } from './features/session/useSession'

function App() {
  // The emailed link works for anyone holding it, so it doesn't wait for a session.
  if (window.location.pathname === confirmEmailPath) {
    return (
      <>
        <AppHeader isGuest={false} />
        <ConfirmEmailPage token={new URLSearchParams(window.location.search).get('token')} />
      </>
    )
  }
  return <SignedInApp />
}

function SignedInApp() {
  const session = useSession()
  const [form, setForm] = useState<AccountFormMode | null>(null)
  const { state } = session
  const me = state.kind === 'ready' ? state.me : null
  const canSignIn = state.kind === 'signedOut' || me?.isGuest === true

  return (
    <>
      <AppHeader
        isGuest={me?.isGuest === true}
        email={me?.isGuest ? null : me?.email}
        onSignIn={canSignIn && form === null ? () => setForm('signIn') : undefined}
        onSignOut={session.signOut}
      />
      {form && canSignIn ? (
        <AccountForm
          mode={form}
          isGuest={me?.isGuest === true}
          onDone={(signedIn: Me) => {
            session.signedIn(signedIn)
            setForm(null)
          }}
          onCancel={() => setForm(null)}
          onSwitch={setForm}
        />
      ) : (
        <Screen session={session} onCreateAccount={() => setForm('signUp')} />
      )}
    </>
  )
}

function Screen({ session, onCreateAccount }: { session: UseSession; onCreateAccount: () => void }) {
  const { state, retry, markSignedOut } = session
  switch (state.kind) {
    case 'loading':
      return (
        <main className="session-message">
          <p>Loading...</p>
        </main>
      )
    case 'signedOut':
      return <GuestPrompt session={session} onCreateAccount={onCreateAccount} />
    case 'error':
      return (
        <main className="session-message" role="alert">
          <p>We couldn't reach StatusTide.</p>
          <button type="button" className="button" onClick={retry}>
            Try again
          </button>
        </main>
      )
    case 'ready':
      // A different user needs a new connection and a new snapshot, so the id is the key. A guest
      // who signs up keeps their id, and the hub still reaches them by that id.
      return (
        <DashboardPage
          key={state.me.id}
          isGuest={state.me.isGuest}
          unconfirmedEmail={state.me.emailConfirmed ? null : state.me.email}
          onSignedOut={markSignedOut}
          onCreateAccount={onCreateAccount}
        />
      )
  }
}

export default App
