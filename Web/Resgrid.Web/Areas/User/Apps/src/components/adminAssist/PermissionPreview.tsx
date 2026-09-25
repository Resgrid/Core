import { useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';

type Result = { asOfUtc: string; limitKeys: string[]; metrics: { labelKey: string; before: number | null; after: number | null }[] };
const scoped = new Set(['ViewGroupUsers', 'ViewGroupUnits', 'CanSeePersonnelLocations', 'CanSeeUnitLocations']);
export default function PermissionPreview({ revision, types, t }: { revision: string; types: string[]; t: (key: string) => string }) {
  const [type, setType] = useState(types[0] ?? '');
  const [action, setAction] = useState(0);
  const [lock, setLock] = useState(false);
  const [roles, setRoles] = useState<number[]>([]);
  const [roleOptions, setRoleOptions] = useState<{ id: number; name: string }[] | null>(null);
  const [roleError, setRoleError] = useState('');
  const [roleAttempt, setRoleAttempt] = useState(0);
  const [result, setResult] = useState<Result | null>(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const pending = useRef<AbortController | null>(null);
  useEffect(() => () => pending.current?.abort(), []);
  useEffect(() => {
    if (action !== 2 || roleOptions !== null) return;
    const request = new AbortController(); setRoleError('');
    void apiFetchJson<{ id: number; name: string }[]>(`api/v4/AdminAssist/PermissionRoles?expectedRevision=${encodeURIComponent(revision)}`, { signal: request.signal })
      .then(options => { if (!request.signal.aborted) setRoleOptions(options); })
      .catch(failure => { if (!request.signal.aborted) setRoleError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error')); });
    return () => request.abort();
  }, [action, revision, roleOptions, roleAttempt, t]);
  function edit(change: () => void) { pending.current?.abort(); setBusy(false); setResult(null); setError(''); change(); }
  async function preview() {
    pending.current?.abort(); const request = new AbortController(); pending.current = request;
    setBusy(true); setResult(null); setError('');
    try {
      const report = await apiFetchJson<Result>('api/v4/AdminAssist/PermissionImpact', {
        method: 'POST', signal: request.signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: revision, permissionType: type, action, lockToGroup: scoped.has(type) && lock, roleIds: action === 2 ? roles : [] }),
      });
      if (!request.signal.aborted) setResult(report);
    } catch (failure) { if (!request.signal.aborted) setError(t(failure instanceof ApiError && failure.status === 409 ? 'Impact.Stale' : 'Ui.Error')); }
    finally { if (!request.signal.aborted) setBusy(false); }
  }
  return <details className="rgaa-card"><summary>{t('Impact.PermissionPreview')}</summary><p>{t('Impact.PermissionHelp')}</p>
    <form onSubmit={event => { event.preventDefault(); void preview(); }}>
      <label>{t('Impact.PermissionType')} <select value={type} onChange={e => edit(() => { setType(e.target.value); setLock(false); })}>{types.map(id => <option key={id} value={id}>{t(`Permission.${id}`)}</option>)}</select></label>
      <label>{t('Impact.PermissionAction')} <select value={action} onChange={e => edit(() => setAction(Number(e.target.value)))}>{[0, 1, 2, 3].map(id => <option key={id} value={id}>{t(`Impact.PermissionAction${id}`)}</option>)}</select></label>
      {action === 2 && <fieldset><legend>{t('Impact.PermissionRoles')}</legend>
        {roleError ? <><p role="alert">{roleError}</p><button type="button" onClick={() => setRoleAttempt(value => value + 1)}>{t('Ui.Retry')}</button></> : roleOptions === null ? <p role="status">{t('Ui.Loading')}</p> : roleOptions.length === 0 ? <p>{t('Ui.None')}</p> :
          roleOptions.map(role => <label key={role.id}><input type="checkbox" checked={roles.includes(role.id)} disabled={!roles.includes(role.id) && roles.length >= 100} onChange={e => edit(() => setRoles(e.target.checked ? [...roles, role.id] : roles.filter(id => id !== role.id)))} />{role.name}</label>)}
      </fieldset>}
      {scoped.has(type) && <label><input type="checkbox" checked={lock} onChange={e => edit(() => setLock(e.target.checked))} />{t('Impact.PermissionLock')}</label>}
      <button type="submit" disabled={busy || !type || (action === 2 && roleOptions === null)}>{t('Impact.PermissionPreview')}</button>
    </form>
    {busy && <p role="status">{t('Ui.Loading')}</p>}{error && <p role="alert">{error}</p>}
    {result && <div role="region" aria-label={t('Impact.PermissionPreview')}><p>{t('Ui.Freshness')}: {new Date(result.asOfUtc).toLocaleString()}</p>
      <table><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{result.metrics.map(m => <tr key={m.labelKey}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? t('Ui.Unknown')}</td><td>{m.after ?? t('Ui.Unknown')}</td></tr>)}</tbody></table>
      {result.limitKeys.map(key => <p key={key}>{t(key)}</p>)}
    </div>}
  </details>;
}
