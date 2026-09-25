import { useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';

type Result = { asOfUtc: string; limitKeys: string[]; metrics: { labelKey: string; before: number | null; after: number | null }[] };
export default function ModulePreview({ revision, types, t }: { revision: string; types: string[]; t: (key: string) => string }) {
  const [module, setModule] = useState(types[0] ?? '');
  const [disabled, setDisabled] = useState(true);
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
      const report = await apiFetchJson<Result>('api/v4/AdminAssist/ModuleImpact', {
        method: 'POST', signal: request.signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, module, disabled }),
      });
      if (!request.signal.aborted) setResult(report);
    } catch (failure) { if (!request.signal.aborted) setError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error')); }
    finally { if (!request.signal.aborted) setBusy(false); }
  }
  return <details className="rgaa-card"><summary>{t('Impact.ModulePreview')}</summary><p>{t('Impact.ModuleHelp')}</p>
    <form onSubmit={event => { event.preventDefault(); void preview(); }}>
      <label>{t('Impact.ModuleChoice')} <select value={module} onChange={e => edit(() => setModule(e.target.value))}>{types.map(id => <option key={id} value={id}>{t(`Module.${id}Disabled`)}</option>)}</select></label>
      <label><input type="checkbox" checked={disabled} onChange={e => edit(() => setDisabled(e.target.checked))} />{t('Impact.ModuleDisabled')}</label>
      <button type="submit" disabled={busy || !module}>{t('Impact.ModulePreview')}</button>
    </form>
    {busy && <p role="status">{t('Ui.Loading')}</p>}{error && <p role="alert">{error}</p>}
    {result && <div role="region" aria-label={t('Impact.ModulePreview')}><p>{t('Ui.Freshness')}: {new Date(result.asOfUtc).toLocaleString()}</p>
      <table><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{result.metrics.map(m => <tr key={m.labelKey}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? t('Ui.Unknown')}</td><td>{m.after ?? t('Ui.Unknown')}</td></tr>)}</tbody></table>
      {result.limitKeys.map(key => <p key={key}>{t(key)}</p>)}
    </div>}
  </details>;
}
