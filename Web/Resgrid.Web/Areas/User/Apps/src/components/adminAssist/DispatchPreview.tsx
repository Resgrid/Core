import { useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';

type Result = { asOfUtc: string; limitKeys: string[]; metrics: { labelKey: string; before: number | null; after: number | null }[] };
export default function DispatchPreview({ revision, t }: { revision: string; t: (key: string) => string }) {
  const [callId, setCallId] = useState('');
  const [time, setTime] = useState(() => new Date().toISOString().slice(0, 16));
  const [shift, setShift] = useState(false);
  const [crew, setCrew] = useState(false);
  const [group, setGroup] = useState(false);
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
      const report = await apiFetchJson<Result>('api/v4/AdminAssist/DispatchImpact', {
        method: 'POST', signal: request.signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, callId: Number(callId), simulationTimeUtc: `${time}:00Z`, shiftInsteadOfGroup: shift, unitCrew: crew, unitGroup: group }),
      });
      if (!request.signal.aborted) setResult(report);
    } catch (failure) { if (!request.signal.aborted) setError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error')); }
    finally { if (!request.signal.aborted) setBusy(false); }
  }
  return <details className="rgaa-card"><summary>{t('Impact.DispatchPreview')}</summary><p>{t('Impact.DispatchHelp')}</p>
    <form onSubmit={event => { event.preventDefault(); void preview(); }}>
      <label>{t('Impact.CallId')} <input type="number" min={1} max={2147483647} step={1} required value={callId} onChange={e => edit(() => setCallId(e.target.value))} /></label>
      <label>{t('Impact.SimulationUtc')} <input type="datetime-local" required value={time} onChange={e => edit(() => setTime(e.target.value))} /></label>
      <label><input type="checkbox" checked={shift} onChange={e => edit(() => setShift(e.target.checked))} />{t('Impact.ProposedShift')}</label>
      <label><input type="checkbox" checked={crew} onChange={e => edit(() => setCrew(e.target.checked))} />{t('Impact.ProposedCrew')}</label>
      <label><input type="checkbox" checked={group} onChange={e => edit(() => setGroup(e.target.checked))} />{t('Impact.ProposedUnitGroup')}</label>
      <button type="submit" disabled={busy}>{t('Impact.DispatchPreview')}</button>
    </form>
    {busy && <p role="status">{t('Ui.Loading')}</p>}{error && <p role="alert">{error}</p>}
    {result && <div role="region" aria-label={t('Impact.DispatchPreview')}><p>{t('Impact.SimulationUtc')}: {new Date(result.asOfUtc).toISOString()}</p>
      <table><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{result.metrics.map(m => <tr key={m.labelKey}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? t('Ui.Unknown')}</td><td>{m.after ?? t('Ui.Unknown')}</td></tr>)}</tbody></table>
      {result.limitKeys.map(key => <p key={key}>{t(key)}</p>)}
    </div>}
  </details>;
}
