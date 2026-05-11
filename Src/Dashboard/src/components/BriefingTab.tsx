import { useEffect, useState } from 'react';
import { fetchLatestBriefing } from '../api/colony';

type MinisterKey = 'mayor' | 'food';

export function BriefingTab() {
  const [minister, setMinister] = useState<MinisterKey>('mayor');
  const [briefing, setBriefing] = useState<unknown>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    setError(null);
    fetchLatestBriefing(minister, ctrl.signal)
      .then(setBriefing)
      .catch(err => {
        if ((err as Error).name !== 'AbortError') setError(String(err));
      });
    return () => ctrl.abort();
  }, [minister]);

  return (
    <div className="briefing-tab">
      <header className="alerts-header">
        <div>
          <div className="kicker">Latest Briefing</div>
          <h2>{minister === 'food' ? 'Food' : 'Mayor'}</h2>
        </div>
        <div className="briefing-switch">
          <button className={minister === 'mayor' ? 'active' : ''} onClick={() => setMinister('mayor')}>Mayor</button>
          <button className={minister === 'food' ? 'active' : ''} onClick={() => setMinister('food')}>Food</button>
        </div>
      </header>
      {error && <div className="prompt-full-status error">{error}</div>}
      {!error && <pre className="briefing-json">{JSON.stringify(briefing, null, 2)}</pre>}
    </div>
  );
}
