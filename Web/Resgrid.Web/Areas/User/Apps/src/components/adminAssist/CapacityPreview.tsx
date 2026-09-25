import { useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';

type Result = { asOfUtc: string; limitKeys: string[]; metrics: { labelKey: string; before: number | null; after: number | null }[] };
export default function CapacityPreview({ revision, t }: { revision: string; t: (key: string) => string }) {
  const [people, setPeople] = useState('');
  const [units, setUnits] = useState('');
  const [result, setResult] = useState<Result | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => pending.current?.abort(), []);
  function edit(value: string, personnel: boolean) {
    pending.current?.abort(); setBusy(false); setError(''); setResult(null);
    if (personnel) setPeople(value); else setUnits(value);
  }
  async function preview() {
    pending.current?.abort(); const request = new AbortController(); pending.current = request;
    setBusy(true); setResult(null); setError('');
    try {
      const response = await apiFetchJson<Result>('api/v4/AdminAssist/CapacityImpact', {
        method: 'POST', signal: request.signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, proposedPersonnelCount: Number(people), proposedUnitCount: Number(units) }),
      });
      if (!request.signal.aborted) setResult(response);
    } catch (failure) { if (!request.signal.aborted) setError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error')); }
    finally { if (!request.signal.aborted) setBusy(false); }
  }
  return <details className="rgaa-card"><summary>{t('Impact.CapacityPreview')}</summary><p>{t('Impact.CapacityHelp')}</p>
    <form onSubmit={event => { event.preventDefault(); void preview(); }}>
      <label>{t('Impact.PersonnelTotal')} <input type="number" min={0} max={1000000} step={1} required value={people} onChange={e => edit(e.target.value, true)} /></label>
      <label>{t('Impact.UnitTotal')} <input type="number" min={0} max={1000000} step={1} required value={units} onChange={e => edit(e.target.value, false)} /></label>
      <button type="submit" disabled={busy}>{t('Impact.Preview')}</button>
    </form>
    {busy && <p role="status">{t('Ui.Loading')}</p>}{error && <p role="alert">{error}</p>}
    {result && <div role="region" aria-label={t('Impact.CapacityPreview')}><p>{t('Ui.Freshness')}: {new Date(result.asOfUtc).toLocaleString()}</p>
      <table><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{result.metrics.map(m => <tr key={m.labelKey}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? t('Ui.Unknown')}</td><td>{m.after ?? t('Ui.Unknown')}</td></tr>)}</tbody></table>
      {result.limitKeys.map(key => <p key={key}>{t(key)}</p>)}
    </div>}
  </details>;
}
