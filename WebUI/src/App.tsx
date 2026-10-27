import Header from './components/Header'
import Dashboard from './features/dashboard/Dashboard'
import { sampleData } from './features/dashboard/sampleData'

function App() {
  return (
    <>
      <Header />
      <main>
        <Dashboard vendors={sampleData.vendors} activity={sampleData.activity} />
      </main>
    </>
  )
}

export default App
