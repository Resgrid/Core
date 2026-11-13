import { useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';

type Result = {
  snapshotRevision: string; asOfUtc: string; evaluatorVersion: string; limitKeys: string[];
  metrics: { labelKey: string; state: string; before: number | null; after: number | null }[];
  ruleChanges: { ruleId: string; titleKey: string; before: string; after: string }[];
};

export default function ImpactPreview({ settingId, valueType, revision, t }: {
  settingId: string; valueType: string; revision: string; t: (key: string) => string;
}) {
  const [value, setValue] = useState(valueType === 'boolean' ? 'false' : '0');
  const [result, setResult] = useState<Result | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => pending.current?.abort(), []);
  function edit(next: string) { pending.current?.abort(); setBusy(false); setResult(null); setError(''); setValue(next); }
  async function preview() {
    pending.current?.abort();
    const request = new AbortController(); pending.current = request;
    setBusy(true); setResult(null); setError('');
    try {
      const report = await apiFetchJson<Result>('api/v4/AdminAssist/Impact', {
        signal: request.signal, method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ settingId, expectedRevision: revision, boolean: valueType === 'boolean' ? value === 'true' : null, number: valueType === 'boolean' ? null : Number(value) }),
      });
      if (!request.signal.aborted) setResult(report);
    } catch (failure) {
      if (!request.signal.aborted) setError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error'));
    } finally { if (!request.signal.aborted) setBusy(false); }
  }
  const fieldId = `rgaa-impact-${settingId.replace(/[^A-Za-z0-9_-]/g, '-')}`;
  return <details className="rgaa-plan-step__more rgaa-impact"><summary className="rgaa-small"><i className="fa fa-line-chart" aria-hidden="true" /> {t('Impact.Preview')}</summary>
    <div className="rgaa-impact__body">
      <p className="help-block">{t('Impact.NoMutation')}</p>
      <form className="rgaa-inline-form" onSubmit={e => { e.preventDefault(); void preview(); }}>
        <label htmlFor={fieldId}>{t('Impact.ProposedValue')}</label>
        {valueType === 'boolean'
          ? <select id={fieldId} className="form-control input-sm" value={value} onChange={e => edit(e.target.value)}><option value="false">{t('Profile.No')}</option><option value="true">{t('Profile.Yes')}</option></select>
          : <input id={fieldId} className="form-control input-sm" type="number" min={0} max={settingId === 'setting.Require2FAForAdmins' ? 2 : 525600} step={1} required value={value} onChange={e => edit(e.target.value)} />}
        <button type="submit" className="btn btn-white btn-sm" disabled={busy}><i className={`fa ${busy ? 'fa-spinner fa-spin' : 'fa-eye'}`} aria-hidden="true" /> {t('Impact.Preview')}</button>
      </form>
      {busy && <p className="sr-only" role="status">{t('Ui.Loading')}</p>}{error && <div className="alert alert-warning rgaa-small" role="alert">{error}</div>}
      {result && <div role="region" aria-label={t('Impact.Preview')}>
        <p className="rgaa-small rgaa-muted"><i className="fa fa-clock-o" aria-hidden="true" /> {t('Ui.Freshness')}: {new Date(result.asOfUtc).toLocaleString()} · {result.evaluatorVersion}</p>
        <div className="table-responsive"><table className="table table-condensed rgaa-impact-table"><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>
          {result.metrics.map(metric => <tr key={metric.labelKey}><th scope="row">{t(metric.labelKey)}</th><td>{metric.before ?? t('Ui.Unknown')}</td><td>{metric.after ?? t('Ui.Unknown')}</td></tr>)}
          {result.ruleChanges.map(rule => <tr key={rule.ruleId}><th scope="row">{t(rule.titleKey)}</th><td>{t(`Ui.${rule.before}`)}</td><td>{t(`Ui.${rule.after}`)}</td></tr>)}
        </tbody></table></div>
        {result.limitKeys.map(key => <p key={key} className="rgaa-small rgaa-muted">{t(key)}</p>)}
      </div>}
    </div>
  </details>;
}
