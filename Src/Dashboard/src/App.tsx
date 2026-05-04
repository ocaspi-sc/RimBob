import { useEffect, useState } from 'react'

type HealthStatus = 'checking' | 'ok' | 'error'

export default function App() {
  const [health, setHealth] = useState<HealthStatus>('checking')

  useEffect(() => {
    fetch('/api/health')
      .then(r => (r.ok ? setHealth('ok') : setHealth('error')))
      .catch(() => setHealth('error'))
  }, [])

  return (
    <main style={{ fontFamily: 'sans-serif', padding: '2rem', maxWidth: '600px' }}>
      <h1 style={{ margin: '0 0 0.25rem' }}>RimAI</h1>
      <p style={{ color: '#666', margin: '0 0 2rem' }}>Colony advisor dashboard</p>

      <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
        <span
          style={{
            width: 10,
            height: 10,
            borderRadius: '50%',
            background: health === 'ok' ? '#22c55e' : health === 'error' ? '#ef4444' : '#a3a3a3',
            display: 'inline-block',
          }}
        />
        <span>
          {health === 'checking' && 'Connecting to backend…'}
          {health === 'ok' && 'Backend connected'}
          {health === 'error' && 'Backend not reachable — start dotnet run --project Host'}
        </span>
      </div>

      <p style={{ color: '#999', fontSize: '0.85rem', marginTop: '3rem' }}>
        M0 scaffold — memo feed arrives in M1.
      </p>
    </main>
  )
}
