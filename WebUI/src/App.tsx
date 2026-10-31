import { AppHeader } from './components/AppHeader'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { GuestPrompt } from './features/session/GuestPrompt'
import { useSession, type UseSession } from './features/session/useSession'

function App() {
  const session = useSession()
  const { state } = session
  return (
    <>
      <AppHeader isGuest={state.kind === 'ready' && state.me.isGuest} />
      <Screen session={session} />
    </>
  )
}

function Screen({ session }: { session: UseSession }) {
  const { state, retry, markSignedOut } = session
  switch (state.kind) {
    case 'loading':
      return (
        <main className="session-message">
          <p>Loading...</p>
        </main>
      )
    case 'signedOut':
      return <GuestPrompt session={session} />
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
      // A different user needs a new connection and a new snapshot, so the id is the key.
      return <DashboardPage key={state.me.id} isGuest={state.me.isGuest} onSignedOut={markSignedOut} />
  }
}

export default App
