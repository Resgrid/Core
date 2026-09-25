import { useEffect, useRef, useState } from 'react';
import { apiFetchJson, ApiError } from '../../runtime/api';

type Check = { id: string; outcome: string; basis: string; explanationKey: string; source: string; version: string; asOfUtc: string; value?: number | null; destination?: string | null };
type Change = { id: string; occurredOnUtc: string; settingId: string; revision: number; beforeCode: string; afterCode: string };
type Attempt = { id: string; resolverVersion: string; observed: number; missing: number; dropped: number; complete: boolean; events: { id: string; occurredOnUtc: string; sequence: number; stage: string; channel: string; reason: string }[] };
type Report = { runId: string; revision: number; flow: string; checkedOnUtc: string; outcome: string; checks: Check[]; changes: Change[]; attempts: Attempt[]; traceTruncated: boolean; timelineTruncated: boolean; statusHistory?: { timestamp: string; status: number; source: string }[] };
type Bundle = Report & { previewDigest: string };
type Run = { id: string; flow: string; createdOnUtc: string; revision: number };
const endpoint = 'api/v4/AdminAssist/';
const flows = ['paging', 'map', 'access', 'imports', 'statuses', 'coverage', 'equipment', 'integration'];
const localTime = (date: Date) => new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);

export default function TroubleshootPanel({ t, localLink, capabilities }: { t: (key: string) => string; localLink: (url?: string | null) => string | undefined; capabilities: { id: string; labelKey: string }[] }) {
  const d = (key: string) => t(`Diagnostic.${key}`);
  const [flow, setFlow] = useState('paging'); const [member, setMember] = useState(''); const [unit, setUnit] = useState('');
  const [call, setCall] = useState(''); const [role, setRole] = useState(''); const [group, setGroup] = useState('');
  const [permission, setPermission] = useState('CreateCall'); const [capability, setCapability] = useState('');
  const [target, setTarget] = useState('member');
  const [from, setFrom] = useState(() => localTime(new Date(Date.now() - 86400000))); const [until, setUntil] = useState(() => localTime(new Date()));
  const [report, setReport] = useState<Report | null>(null); const [bundle, setBundle] = useState<Bundle | null>(null);
  const [history, setHistory] = useState<Run[]>([]); const [busy, setBusy] = useState(false); const [notice, setNotice] = useState('');
  const pending = useRef<AbortController | null>(null);
  const request = async <T,>(action: string, body: unknown, signal: AbortSignal) => apiFetchJson<T>(endpoint + action, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal });
  const clear = () => { setReport(null); setBundle(null); setHistory([]); };
  useEffect(() => {
    const refresh = () => {
      pending.current?.abort(); const abort = new AbortController(); pending.current = abort; clear(); setBusy(false);
      void apiFetchJson<Run[]>(endpoint + 'Diagnostics', { signal: abort.signal }).then(rows => { if (!abort.signal.aborted) setHistory(rows); }).catch(() => { if (!abort.signal.aborted) setNotice('Unavailable'); });
    };
    refresh(); window.addEventListener('resgrid:adp-reveal-changed', refresh);
    return () => { pending.current?.abort(); window.removeEventListener('resgrid:adp-reveal-changed', refresh); };
  }, []);
  async function run(action: (signal: AbortSignal) => Promise<void>) {
    if (busy) return; pending.current?.abort(); const abort = new AbortController(); pending.current = abort; setBusy(true); setNotice('');
    try {
      await action(abort.signal);
      if (!abort.signal.aborted) { const rows = await apiFetchJson<Run[]>(endpoint + 'Diagnostics', { signal: abort.signal }); if (!abort.signal.aborted) setHistory(rows); }
    } catch (error) {
      if (!abort.signal.aborted) { setNotice(error instanceof ApiError && error.status === 409 ? 'Changed' : 'Unavailable'); setBundle(null); if (error instanceof ApiError && error.status === 403) clear(); }
    } finally { if (pending.current === abort) setBusy(false); }
  }
  const command = () => ({ runId: report?.runId, expectedRevision: report?.revision });
  return <section aria-busy={busy}>
    <p>{d('Boundary')}</p><p>{d('Privacy')}</p>
    <form onSubmit={event => { event.preventDefault(); setBundle(null); void run(async signal => {
      const scopedMember = flow === 'paging' || flow === 'access' || (['map', 'statuses'].includes(flow) && target === 'member');
      const scopedUnit = flow === 'equipment' || (['map', 'statuses'].includes(flow) && target === 'unit');
      const result = await request<Report>('Diagnose', { flow, fromUtc: new Date(from).toISOString(), untilUtc: new Date(until).toISOString(),
        memberId: scopedMember ? member.trim() : null, unitId: scopedUnit ? Number(unit) : null, callId: flow === 'paging' ? Number(call) : null,
        roleId: flow === 'coverage' ? Number(role) : null, groupId: flow === 'coverage' && group ? Number(group) : null,
        permission: flow === 'access' ? permission : null, capabilityId: ['access', 'integration'].includes(flow) ? capability : null }, signal);
      if (!signal.aborted) setReport(result);
    }); }}>
      <fieldset disabled={busy}>
        <legend>{d('Choose')}</legend>
        <label>{d('Flow')} <select value={flow} onChange={e => { setFlow(e.target.value); setReport(null); setBundle(null); }}>{flows.map(f => <option key={f} value={f}>{d(`Flow.${f}`)}</option>)}</select></label>
        {['map', 'statuses'].includes(flow) && <label>{d('Target')} <select value={target} onChange={e => setTarget(e.target.value)}><option value="member">{d('Member')}</option><option value="unit">{d('Unit')}</option></select></label>}
        {(flow === 'paging' || flow === 'access' || (['map', 'statuses'].includes(flow) && target === 'member')) && <label>{d('MemberId')} <input required maxLength={128} value={member} onChange={e => setMember(e.target.value)} /></label>}
        {(flow === 'equipment' || (['map', 'statuses'].includes(flow) && target === 'unit')) && <label>{d('UnitId')} <input type="number" min={1} step={1} required value={unit} onChange={e => setUnit(e.target.value)} /></label>}
        {flow === 'paging' && <label>{d('CallId')} <input type="number" min={1} step={1} required value={call} onChange={e => setCall(e.target.value)} /></label>}
        {flow === 'coverage' && <><label>{d('RoleId')} <input type="number" min={1} step={1} required value={role} onChange={e => setRole(e.target.value)} /></label><label>{d('GroupId')} <input type="number" min={1} step={1} value={group} onChange={e => setGroup(e.target.value)} /></label></>}
        {flow === 'access' && <label>{d('Permission')} <select value={permission} onChange={e => setPermission(e.target.value)}>{['CreateCall', 'CreateNote', 'ViewPersonalInfo'].map(p => <option value={p} key={p}>{t(`Permission.${p}`)}</option>)}</select></label>}
        {['access', 'integration'].includes(flow) && <label>{d('Capability')} <select required value={capability} onChange={e => setCapability(e.target.value)}><option value="">{d('Select')}</option>{capabilities.map(c => <option key={c.id} value={c.id}>{t(c.labelKey)}</option>)}</select></label>}
        <p>{d('IdentifierHelp')}</p>
        <label>{d('From')} <input type="datetime-local" required value={from} onChange={e => setFrom(e.target.value)} /></label>
        <label>{d('Until')} <input type="datetime-local" required value={until} onChange={e => setUntil(e.target.value)} /></label>
        <p>{d('WindowHelp')}</p><button type="submit">{d('Run')}</button>
      </fieldset>
    </form>
    {busy && <button type="button" onClick={() => { pending.current?.abort(); setBusy(false); setNotice('Cancelled'); }}>{d('Cancel')}</button>}
    {notice && <p role="status">{d(notice)}</p>}
    <details><summary>{d('History')}</summary>{history.map(item => <p key={item.id}><button disabled={busy} onClick={() => void run(async signal => { setBundle(null); const result = await request<Report>('Diagnostic', { runId: item.id, expectedRevision: item.revision }, signal); if (!signal.aborted) setReport(result); })}>{d(`Flow.${item.flow}`)} · {new Date(item.createdOnUtc).toLocaleString()}</button></p>)}</details>
    {report && <div aria-live="polite">
      <h3>{d(report.outcome)}</h3><p>{d('Checked')}: {new Date(report.checkedOnUtc).toLocaleString()}</p>
      <p>{d('CurrentBoundary')}</p>
      {report.checks.map((check, index) => <article className="rgaa-card" key={`${check.id}-${index}`}><h4>{d(check.outcome)}</h4><p>{t(check.explanationKey)}</p><p>{d(check.basis)}{check.value != null && <> · {check.value.toLocaleString()}</>}</p>{localLink(check.destination) && <a href={localLink(check.destination)}>{d('OpenSource')}</a>}<p><small>{check.source} · {check.version} · {new Date(check.asOfUtc).toLocaleString()}</small></p></article>)}
      {report.flow === 'statuses' && <><h4>{d('StatusHistory')}</h4><p>{d('Check.StatusWriterUnknown')}</p><ol>{report.statusHistory?.map((row, i) => <li key={i}>{new Date(row.timestamp).toLocaleString()} · {d('StatusCode')}: {row.status}</li>)}</ol></>}
      <h4>{d('Timeline')}</h4><p>{d('Correlation')}</p>
      {report.timelineTruncated && <p role="status">{d('Truncated')}</p>}
      {report.changes.length === 0 && <p>{d('NoChanges')}</p>}
      {report.changes.map(change => <p key={change.id}>{new Date(change.occurredOnUtc).toLocaleString()} · {change.settingId} · {change.beforeCode} → {change.afterCode} · {change.revision}</p>)}
      {report.flow === 'paging' && <><h4>{d('Trace')}</h4><p>{d('Handoff')}</p>{report.traceTruncated && <p role="status">{d('Truncated')}</p>}{report.attempts.length === 0 && <p>{d('HistoricalUnavailable')}</p>}{report.attempts.map(attempt => <details key={attempt.id}><summary>{attempt.id} · {d(attempt.complete ? 'Complete' : 'Incomplete')}</summary><p>{d('Observed')}: {attempt.observed} · {d('Missing')}: {attempt.missing} · {d('Dropped')}: {attempt.dropped}</p><ol>{attempt.events.map(e => <li key={e.id}>{new Date(e.occurredOnUtc).toLocaleString()} · {d(`Stage.${e.stage}`)} · {e.channel} · {d(`Reason.${e.reason}`)}</li>)}</ol></details>)}</>}
      <p>{d('SupportHelp')}</p>
      <button disabled={busy} onClick={() => void run(async signal => { const result = await request<Bundle>('DiagnosticSupportPreview', command(), signal); if (!signal.aborted) setBundle(result); })}>{d('Preview')}</button>
      <button disabled={busy} onClick={() => void run(async signal => { await request('DeleteDiagnostic', command(), signal); if (!signal.aborted) { setReport(null); setBundle(null); setNotice('Deleted'); } })}>{d('Delete')}</button>
      {bundle && <><h4>{d('Preview')}</h4><pre style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{JSON.stringify(bundle, null, 2)}</pre><button disabled={busy} onClick={() => void run(async signal => {
        const data = await request<Bundle>('DiagnosticSupportExport', { ...command(), previewDigest: bundle.previewDigest }, signal); if (signal.aborted) return;
        const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })); const link = document.createElement('a'); link.href = url; link.download = 'resgrid-support-diagnostic.json'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
      })}>{d('Export')}</button></>}
    </div>}
  </section>;
}
