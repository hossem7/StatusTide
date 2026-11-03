import { useState } from 'react'
import type { Me } from './api/types'
import { AppHeader } from './components/AppHeader'
import { usePath } from './components/navigation'
import { catalogPath } from './features/catalog/catalogLabels'
import { CatalogPage } from './features/catalog/CatalogPage'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { AccountForm, type AccountFormMode } from './features/session/AccountForm'
import { ConfirmEmailPage, confirmEmailPath } from './features/session/ConfirmEmailPage'
import { GuestPrompt } from './features/session/GuestPrompt'
import { useSession, type UseSession } from './features/session/useSession'

function App() {
  const path = usePath()
  // The emailed link works for anyone holding it, so it doesn't wait for a session.
  if (path === confirmEmailPath) {
    return (
      <>
        <AppHeader isGuest={false} />
        <ConfirmEmailPage token={new URLSearchParams(window.location.search).get('token')} />
      </>
    )
  }
  return <SignedInApp path={path} />
}

function SignedInApp({ path }: { path: string }) {
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
        hasSession={me !== null}
        onNavigate={() => setForm(null)}
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
        <Screen session={session} path={path} onCreateAccount={() => setForm('signUp')} />
      )}
    </>
  )
}

function Screen({ session, path, onCreateAccount }: { session: UseSession; path: string; onCreateAccount: () => void }) {
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
      if (path === catalogPath) {
        return (
          <CatalogPage
            key={state.me.id}
            isGuest={state.me.isGuest}
            onSignedOut={markSignedOut}
            onCreateAccount={onCreateAccount}
          />
        )
      }
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
