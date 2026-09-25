import { useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';

type Result = { asOfUtc: string; limitKeys: string[]; metrics: { labelKey: string; before: number | null; after: number | null }[] };
export default function SecurityPreview({ revision, t }: { revision: string; t: (key: string) => string }) {
  const fields = ['RequireMfa', 'RequireSso', 'SessionTimeoutMinutes', 'MaxConcurrentSessions', 'PasswordExpirationDays', 'MinPasswordLength'];
  const [field, setField] = useState(fields[0]);
  const [enabled, setEnabled] = useState(true);
  const [number, setNumber] = useState(30);
  const isBoolean = field === 'RequireMfa' || field === 'RequireSso';
  const maximum = field === 'MinPasswordLength' ? 128 : field === 'SessionTimeoutMinutes' ? 43200 : field === 'MaxConcurrentSessions' ? 1000 : 36500;
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
      const report = await apiFetchJson<Result>('api/v4/AdminAssist/SecurityImpact', {
        method: 'POST', signal: request.signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, settingId: `table.DepartmentSecurityPolicy.${field}`, boolean: isBoolean ? enabled : null, number: isBoolean ? null : number }),
      });
      if (!request.signal.aborted) setResult(report);
    } catch (failure) { if (!request.signal.aborted) setError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error')); }
    finally { if (!request.signal.aborted) setBusy(false); }
  }
  return <details className="rgaa-card"><summary>{t('Impact.SecurityPreview')}</summary><p>{t('Impact.SecurityHelp')}</p>
    <form onSubmit={event => { event.preventDefault(); void preview(); }}>
      <label>{t('Impact.SecurityField')} <select value={field} onChange={e => edit(() => { setField(e.target.value); setNumber(e.target.value === 'MinPasswordLength' ? 12 : 30); })}>{fields.map(id => <option key={id} value={id}>{t(`TableField.DepartmentSecurityPolicy.${id}`)}</option>)}</select></label>
      {isBoolean ? <label><input type="checkbox" checked={enabled} onChange={e => edit(() => setEnabled(e.target.checked))} />{t('Impact.SecurityProposed')}</label>
        : <label>{t('Impact.SecurityProposed')} <input type="number" required min={field === 'MinPasswordLength' ? 8 : 0} max={maximum} step={1} value={number} onChange={e => edit(() => setNumber(e.target.valueAsNumber))} /></label>}
      <button type="submit" disabled={busy || (!isBoolean && !Number.isInteger(number))}>{t('Impact.SecurityPreview')}</button>
    </form>
    {busy && <p role="status">{t('Ui.Loading')}</p>}{error && <p role="alert">{error}</p>}
    {result && <div role="region" aria-label={t('Impact.SecurityPreview')}><p>{t('Ui.Freshness')}: {new Date(result.asOfUtc).toLocaleString()}</p>
      <table><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{result.metrics.map(m => <tr key={m.labelKey}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? t('Ui.Unknown')}</td><td>{m.after ?? t('Ui.Unknown')}</td></tr>)}</tbody></table>
      {result.limitKeys.map(key => <p key={key}>{t(key)}</p>)}
    </div>}
  </details>;
}
