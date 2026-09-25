import { useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';

type Result = { maskedSource: string; providerPath: string; impact: { asOfUtc: string; limitKeys: string[]; metrics: { labelKey: string; before: number | null; after: number | null }[] } };
export default function TextImportPreview({ revision, t }: { revision: string; t: (key: string) => string }) {
  const [provider, setProvider] = useState('SignalWire');
  const [source, setSource] = useState('');
  const [calls, setCalls] = useState(false);
  const [commands, setCommands] = useState(false);
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
      const report = await apiFetchJson<Result>('api/v4/AdminAssist/TextImportImpact', {
        method: 'POST', signal: request.signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, providerPath: provider, sourceNumber: source, callsEnabled: calls, commandsEnabled: commands }),
      });
      if (!request.signal.aborted) setResult(report);
    } catch (failure) { if (!request.signal.aborted) setError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error')); }
    finally { if (!request.signal.aborted) setBusy(false); }
  }
  return <details className="rgaa-card"><summary>{t('Impact.TextPreview')}</summary><p>{t('Impact.TextHelp')}</p>
    <form onSubmit={event => { event.preventDefault(); void preview(); }}>
      <label>{t('Impact.TextPath')} <select value={provider} onChange={e => edit(() => setProvider(e.target.value))}><option value="SignalWire">SignalWire</option><option value="TwilioLegacy">{t('Impact.TextTwilioLegacy')}</option></select></label>
      <label>{t('Impact.TextSource')} <input type="tel" minLength={7} maxLength={40} autoComplete="off" required value={source} onChange={e => edit(() => setSource(e.target.value))} /></label>
      <label><input type="checkbox" checked={calls} onChange={e => edit(() => setCalls(e.target.checked))} />{t('Impact.TextProposedCalls')}</label>
      <label><input type="checkbox" checked={commands} onChange={e => edit(() => setCommands(e.target.checked))} />{t('Impact.TextProposedCommands')}</label>
      <button type="submit" disabled={busy}>{t('Impact.TextPreview')}</button>
    </form>
    {busy && <p role="status">{t('Ui.Loading')}</p>}{error && <p role="alert">{error}</p>}
    {result && <div role="region" aria-label={t('Impact.TextPreview')}><p>{result.providerPath} · {result.maskedSource} · {t('Ui.Freshness')}: {new Date(result.impact.asOfUtc).toLocaleString()}</p>
      <table><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{result.impact.metrics.map(m => <tr key={m.labelKey}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? t('Ui.Unknown')}</td><td>{m.after ?? t('Ui.Unknown')}</td></tr>)}</tbody></table>
      {result.impact.limitKeys.map(key => <p key={key}>{t(key)}</p>)}
    </div>}
  </details>;
}
