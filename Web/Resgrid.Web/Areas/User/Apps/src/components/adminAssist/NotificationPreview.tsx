import { useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';

type Result = { asOfUtc: string; limitKeys: string[]; metrics: { labelKey: string; before: number | null; after: number | null }[] };
export default function NotificationPreview({ revision, t }: { revision: string; t: (key: string) => string }) {
  const [suppressStaffing, setSuppressStaffing] = useState(true);
  const [windowDays, setWindowDays] = useState(7);
  const [eventsPerMember, setEventsPerMember] = useState(1);
  const [result, setResult] = useState<Result | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => pending.current?.abort(), []);
  function edit(change: () => void) { pending.current?.abort(); setBusy(false); setResult(null); setError(''); change(); }
  async function preview() {
    pending.current?.abort(); const request = new AbortController(); pending.current = request;
    setBusy(true); setResult(null); setError('');
    try {
      const report = await apiFetchJson<Result>('api/v4/AdminAssist/NotificationImpact', {
        method: 'POST', signal: request.signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, suppressStaffing, windowDays, eventsPerMember }),
      });
      if (!request.signal.aborted) setResult(report);
    } catch (failure) { if (!request.signal.aborted) setError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error')); }
    finally { if (!request.signal.aborted) setBusy(false); }
  }
  return <details className="rgaa-card"><summary>{t('Impact.NotificationPreview')}</summary><p>{t('Impact.NotificationHelp')}</p>
    <form onSubmit={event => { event.preventDefault(); void preview(); }}>
      <label>{t('Impact.NotificationDaysInput')} <input type="number" required min={1} max={30} step={1} value={windowDays} onChange={e => edit(() => setWindowDays(e.target.valueAsNumber))} /></label>
      <label>{t('Impact.NotificationEventsInput')} <input type="number" required min={0} max={10000} step={1} value={eventsPerMember} onChange={e => edit(() => setEventsPerMember(e.target.valueAsNumber))} /></label>
      <label><input type="checkbox" checked={suppressStaffing} onChange={e => edit(() => setSuppressStaffing(e.target.checked))} />{t('Impact.NotificationSuppress')}</label>
      <button type="submit" disabled={busy || !Number.isInteger(windowDays) || !Number.isInteger(eventsPerMember)}>{t('Impact.NotificationPreview')}</button>
    </form>
    {busy && <p role="status">{t('Ui.Loading')}</p>}{error && <p role="alert">{error}</p>}
    {result && <div role="region" aria-label={t('Impact.NotificationPreview')}><p>{t('Ui.Freshness')}: {new Date(result.asOfUtc).toLocaleString()}</p>
      <table><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{result.metrics.map(m => <tr key={m.labelKey}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? t('Ui.Unknown')}</td><td>{m.after ?? t('Ui.Unknown')}</td></tr>)}</tbody></table>
      {result.limitKeys.map(key => <p key={key}>{t(key)}</p>)}
    </div>}
  </details>;
}
