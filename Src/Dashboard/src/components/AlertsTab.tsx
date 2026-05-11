import type { AdviceItem } from '../types/advice';

interface Props {
  advice: AdviceItem[];
}

export function AlertsTab({ advice }: Props) {
  if (advice.length === 0) {
    return (
      <div className="empty-console">
        <span className="empty-code">NO FEEDER MEMOS</span>
        Food has not emitted advice in this session.
      </div>
    );
  }

  return (
    <div className="alerts-console">
      <header className="alerts-header">
        <div>
          <div className="kicker">Feeder Minister Memos</div>
          <h2>Alerts</h2>
        </div>
        <span>{advice.length} active</span>
      </header>
      <div className="advice-list">
        {advice.map(item => (
          <article key={item.id} className={`advice-card severity-${item.severity}`}>
            <div className="advice-card-top">
              <span className="advice-minister">{item.minister}</span>
              <span className="advice-type">{item.advice_type}</span>
              <span className={`severity-pill ${item.severity}`}>{item.severity}</span>
            </div>
            <h3>{item.title}</h3>
            <p>{item.body}</p>
            <div className="advice-rationale">{item.rationale}</div>
            {item.resource_requests.length > 0 && (
              <div className="advice-block">
                <h4>Resource requests</h4>
                <ul>
                  {item.resource_requests.map((req, idx) => (
                    <li key={`${item.id}-req-${idx}`}>
                      <strong>{req.kind}</strong>
                      <span>{req.what}</span>
                      <small>{req.why}{req.requested_from ? ` · requested from ${req.requested_from}` : ''}</small>
                    </li>
                  ))}
                </ul>
              </div>
            )}
            {item.suggested_actions.length > 0 && (
              <div className="advice-block">
                <h4>Suggested actions</h4>
                <ul>
                  {item.suggested_actions.map((action, idx) => (
                    <li key={`${item.id}-act-${idx}`}>
                      <strong>{action.kind}</strong>
                      <span>{action.what}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </article>
        ))}
      </div>
    </div>
  );
}
