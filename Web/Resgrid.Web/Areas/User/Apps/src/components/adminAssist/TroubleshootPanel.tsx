import { useEffect, useRef, useState } from 'react';
import { apiFetchJson, ApiError } from '../../runtime/api';
import { Chip, Ibox } from './SetupVisuals';

type Check = { id: string; outcome: string; basis: string; explanationKey: string; source: string; version: string; asOfUtc: string; value?: number | null; destination?: string | null };
type Change = { id: string; occurredOnUtc: string; settingId: string; revision: number; beforeCode: string; afterCode: string };
type Attempt = { id: string; resolverVersion: string; observed: number; missing: number; dropped: number; complete: boolean; events: { id: string; occurredOnUtc: string; sequence: number; stage: string; channel: string; reason: string }[] };
type Report = { runId: string; revision: number; flow: string; checkedOnUtc: string; outcome: string; checks: Check[]; changes: Change[]; attempts: Attempt[]; traceTruncated: boolean; timelineTruncated: boolean; statusHistory?: { timestamp: string; status: number; source: string }[] };
type Bundle = Report & { previewDigest: string };
type Run = { id: string; flow: string; createdOnUtc: string; revision: number };
type Tone = 'ok' | 'fail' | 'unknown' | 'info' | 'muted';
const endpoint = 'api/v4/AdminAssist/';
const flows = ['paging', 'map', 'access', 'imports', 'statuses', 'coverage', 'equipment', 'integration'];
const flowIcons: Record<string, string> = { paging: 'fa-bell-o', map: 'fa-map-marker', access: 'fa-key', imports: 'fa-inbox', statuses: 'fa-exchange', coverage: 'fa-users', equipment: 'fa-wrench', integration: 'fa-plug' };
const outcomeVisual: Record<string, { tone: Tone; icon: string }> = {
  ConfirmedCause: { tone: 'fail', icon: 'fa-exclamation-circle' }, PossibleCause: { tone: 'unknown', icon: 'fa-question-circle' },
  NoIssueFound: { tone: 'ok', icon: 'fa-check-circle' }, InsufficientEvidence: { tone: 'muted', icon: 'fa-minus-circle' }, Restricted: { tone: 'muted', icon: 'fa-lock' },
};
const visualFor = (outcome: string) => outcomeVisual[outcome] ?? { tone: 'info' as Tone, icon: 'fa-info-circle' };
const stageTone = (stage: string): Tone => ['Failed', 'ProviderDeclined', 'ServiceDeclined'].includes(stage) ? 'fail' : ['Skipped', 'Excluded'].includes(stage) ? 'muted'
  : ['ProviderAccepted', 'ServiceCompleted', 'BroadcastCompleted'].includes(stage) ? 'ok' : stage === 'ProviderResultUnknown' ? 'unknown' : 'info';
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
  const scopedMember = flow === 'paging' || flow === 'access' || (['map', 'statuses'].includes(flow) && target === 'member');
  const scopedUnit = flow === 'equipment' || (['map', 'statuses'].includes(flow) && target === 'unit');
  const noticeIsProblem = notice === 'Unavailable' || notice === 'Changed';
  const result = report && visualFor(report.outcome);

  return <div aria-busy={busy}>
    <p className="rgaa-lead">{d('Boundary')}</p>
    <div className="rgaa-workbench">
      <div>
        {busy && <div className="rgaa-busy-bar" role="status"><span><i className="fa fa-spinner fa-spin" aria-hidden="true" /><span className="sr-only">{t('Ui.Loading')}</span></span>
          <button type="button" className="btn btn-white btn-xs" onClick={() => { pending.current?.abort(); setBusy(false); setNotice('Cancelled'); }}><i className="fa fa-stop" aria-hidden="true" /> {d('Cancel')}</button></div>}
        {notice && <div className={`alert ${noticeIsProblem ? 'alert-warning' : 'alert-info'}`} role="status">{d(notice)}</div>}

        <Ibox title={<><i className="fa fa-stethoscope" aria-hidden="true" /> {d('Choose')}</>}>
          <form onSubmit={event => { event.preventDefault(); setBundle(null); void run(async signal => {
            const result = await request<Report>('Diagnose', { flow, fromUtc: new Date(from).toISOString(), untilUtc: new Date(until).toISOString(),
              memberId: scopedMember ? member.trim() : null, unitId: scopedUnit ? Number(unit) : null, callId: flow === 'paging' ? Number(call) : null,
              roleId: flow === 'coverage' ? Number(role) : null, groupId: flow === 'coverage' && group ? Number(group) : null,
              permission: flow === 'access' ? permission : null, capabilityId: ['access', 'integration'].includes(flow) ? capability : null }, signal);
            if (!signal.aborted) setReport(result);
          }); }}>
            <fieldset disabled={busy}>
              <div className="form-group"><span className="rgaa-field-label" id="rgaa-diag-flow">{d('Flow')}</span>
                <div className="rgaa-flow-picker" role="group" aria-labelledby="rgaa-diag-flow">
                  {flows.map(f => <button type="button" key={f} aria-pressed={flow === f} className="rgaa-plain rgaa-flow"
                    onClick={() => { setFlow(f); setReport(null); setBundle(null); }}><i className={`fa ${flowIcons[f]}`} aria-hidden="true" /><span>{d(`Flow.${f}`)}</span></button>)}
                </div>
              </div>
              <div className="rgaa-form-grid">
                {['map', 'statuses'].includes(flow) && <div className="form-group"><label htmlFor="rgaa-diag-target">{d('Target')}</label>
                  <select id="rgaa-diag-target" className="form-control input-sm" value={target} onChange={e => setTarget(e.target.value)}><option value="member">{d('Member')}</option><option value="unit">{d('Unit')}</option></select></div>}
                {scopedMember && <div className="form-group"><label htmlFor="rgaa-diag-member">{d('MemberId')}</label>
                  <input id="rgaa-diag-member" className="form-control input-sm" required maxLength={128} value={member} onChange={e => setMember(e.target.value)} /></div>}
                {scopedUnit && <div className="form-group"><label htmlFor="rgaa-diag-unit">{d('UnitId')}</label>
                  <input id="rgaa-diag-unit" className="form-control input-sm" type="number" min={1} step={1} required value={unit} onChange={e => setUnit(e.target.value)} /></div>}
                {flow === 'paging' && <div className="form-group"><label htmlFor="rgaa-diag-call">{d('CallId')}</label>
                  <input id="rgaa-diag-call" className="form-control input-sm" type="number" min={1} step={1} required value={call} onChange={e => setCall(e.target.value)} /></div>}
                {flow === 'coverage' && <>
                  <div className="form-group"><label htmlFor="rgaa-diag-role">{d('RoleId')}</label><input id="rgaa-diag-role" className="form-control input-sm" type="number" min={1} step={1} required value={role} onChange={e => setRole(e.target.value)} /></div>
                  <div className="form-group"><label htmlFor="rgaa-diag-group">{d('GroupId')}</label><input id="rgaa-diag-group" className="form-control input-sm" type="number" min={1} step={1} value={group} onChange={e => setGroup(e.target.value)} /></div>
                </>}
                {flow === 'access' && <div className="form-group"><label htmlFor="rgaa-diag-permission">{d('Permission')}</label>
                  <select id="rgaa-diag-permission" className="form-control input-sm" value={permission} onChange={e => setPermission(e.target.value)}>{['CreateCall', 'CreateNote', 'ViewPersonalInfo'].map(p => <option value={p} key={p}>{t(`Permission.${p}`)}</option>)}</select></div>}
                {['access', 'integration'].includes(flow) && <div className="form-group"><label htmlFor="rgaa-diag-capability">{d('Capability')}</label>
                  <select id="rgaa-diag-capability" className="form-control input-sm" required value={capability} onChange={e => setCapability(e.target.value)}><option value="">{d('Select')}</option>{capabilities.map(c => <option key={c.id} value={c.id}>{t(c.labelKey)}</option>)}</select></div>}
              </div>
              <p className="help-block"><i className="fa fa-info-circle" aria-hidden="true" /> {d('IdentifierHelp')}</p>
              <div className="rgaa-form-grid">
                <div className="form-group"><label htmlFor="rgaa-diag-from">{d('From')}</label><input id="rgaa-diag-from" className="form-control input-sm" type="datetime-local" required value={from} onChange={e => setFrom(e.target.value)} /></div>
                <div className="form-group"><label htmlFor="rgaa-diag-until">{d('Until')}</label><input id="rgaa-diag-until" className="form-control input-sm" type="datetime-local" required value={until} onChange={e => setUntil(e.target.value)} /></div>
              </div>
              <p className="help-block">{d('WindowHelp')}</p>
              <div className="rgaa-form-actions"><button type="submit" className="btn btn-primary btn-sm"><i className="fa fa-play" aria-hidden="true" /> {d('Run')}</button></div>
            </fieldset>
          </form>
        </Ibox>

        {report && result && <div aria-live="polite">
          <Ibox title={<><i className={`fa ${result.icon} rgaa-icon--${result.tone}`} aria-hidden="true" /> {d(report.outcome)}</>}
            tools={<span className="rgaa-small rgaa-muted"><i className="fa fa-clock-o" aria-hidden="true" /> {d('Checked')}: <time dateTime={report.checkedOnUtc}>{new Date(report.checkedOnUtc).toLocaleString()}</time></span>}>
            <p className="help-block">{d('CurrentBoundary')}</p>
            <div className="rgaa-evidence-grid">{report.checks.map((check, index) => {
              const visual = visualFor(check.outcome);
              const destination = localLink(check.destination);
              return <article className={`rgaa-evidence-card rgaa-evidence-card--${visual.tone}`} key={`${check.id}-${index}`}>
                <header className="rgaa-evidence-card__head"><Chip tone={visual.tone} icon={visual.icon}>{d(check.outcome)}</Chip>
                  <Chip tone="muted" icon={check.basis === 'Historical' ? 'fa-history' : 'fa-cog'}>{d(check.basis)}</Chip></header>
                <p>{t(check.explanationKey)}</p>
                {check.value != null && <p className="rgaa-evidence-card__value">{check.value.toLocaleString()}</p>}
                <footer className="rgaa-evidence-card__foot">
                  <small className="rgaa-muted">{check.source} · {check.version} · {new Date(check.asOfUtc).toLocaleString()}</small>
                  {destination && <a className="btn btn-white btn-xs" href={destination}><i className="fa fa-external-link" aria-hidden="true" /> {d('OpenSource')}</a>}
                </footer>
              </article>;
            })}</div>
          </Ibox>

          {report.flow === 'statuses' && <Ibox title={<><i className="fa fa-exchange" aria-hidden="true" /> {d('StatusHistory')}</>}>
            <p className="help-block">{d('Check.StatusWriterUnknown')}</p>
            {(report.statusHistory?.length ?? 0) > 0 && <div className="table-responsive"><table className="table table-condensed table-striped">
              <thead><tr><th>{t('Ui.HistoryTime')}</th><th>{d('StatusCode')}</th></tr></thead>
              <tbody>{report.statusHistory?.map((row, i) => <tr key={i}><td>{new Date(row.timestamp).toLocaleString()}</td><td>{row.status}</td></tr>)}</tbody></table></div>}
          </Ibox>}

          <Ibox title={<><i className="fa fa-sliders" aria-hidden="true" /> {d('Timeline')}</>}>
            <p className="help-block">{d('Correlation')}</p>
            {report.timelineTruncated && <div className="alert alert-warning" role="status">{d('Truncated')}</div>}
            {report.changes.length === 0 ? <p className="rgaa-muted"><i className="fa fa-check-circle rgaa-icon--ok" aria-hidden="true" /> {d('NoChanges')}</p>
              : <ol className="rgaa-timeline">{report.changes.map(change => <li key={change.id}>
                <time dateTime={change.occurredOnUtc}>{new Date(change.occurredOnUtc).toLocaleString()}</time>
                <div><code>{change.settingId}</code> <span className="rgaa-muted rgaa-small">#{change.revision}</span>
                  <p className="rgaa-change"><span className="rgaa-change__from">{change.beforeCode}</span><i className="fa fa-long-arrow-right" aria-hidden="true" /><span className="rgaa-change__to">{change.afterCode}</span></p></div>
              </li>)}</ol>}
          </Ibox>

          {report.flow === 'paging' && <Ibox title={<><i className="fa fa-bell-o" aria-hidden="true" /> {d('Trace')}</>}>
            <p className="help-block">{d('Handoff')}</p>
            {report.traceTruncated && <div className="alert alert-warning" role="status">{d('Truncated')}</div>}
            {report.attempts.length === 0 && <p className="rgaa-muted">{d('HistoricalUnavailable')}</p>}
            {report.attempts.map(attempt => <details key={attempt.id} className="rgaa-attempt">
              <summary><code>{attempt.id}</code> <Chip tone={attempt.complete ? 'ok' : 'unknown'} icon={attempt.complete ? 'fa-check' : 'fa-exclamation'}>{d(attempt.complete ? 'Complete' : 'Incomplete')}</Chip></summary>
              <p className="rgaa-plan-step__checks"><Chip tone="info">{d('Observed')}: {attempt.observed}</Chip><Chip tone={attempt.missing > 0 ? 'unknown' : 'muted'}>{d('Missing')}: {attempt.missing}</Chip><Chip tone={attempt.dropped > 0 ? 'fail' : 'muted'}>{d('Dropped')}: {attempt.dropped}</Chip></p>
              <div className="table-responsive"><table className="table table-condensed">
                <tbody>{attempt.events.map(e => <tr key={e.id}><td className="rgaa-small">{new Date(e.occurredOnUtc).toLocaleString()}</td><td><Chip tone={stageTone(e.stage)}>{d(`Stage.${e.stage}`)}</Chip></td><td>{e.channel}</td><td className="rgaa-muted">{d(`Reason.${e.reason}`)}</td></tr>)}</tbody>
              </table></div>
            </details>)}
          </Ibox>}

          <div className="rgaa-footbar">
            <p>{d('SupportHelp')}</p>
            <div className="rgaa-actions-row">
              <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void run(async signal => { const result = await request<Bundle>('DiagnosticSupportPreview', command(), signal); if (!signal.aborted) setBundle(result); })}><i className="fa fa-life-ring" aria-hidden="true" /> {d('Preview')}</button>
              <button type="button" className="btn btn-white btn-sm rgaa-btn-danger" disabled={busy} onClick={() => void run(async signal => { await request('DeleteDiagnostic', command(), signal); if (!signal.aborted) { setReport(null); setBundle(null); setNotice('Deleted'); } })}><i className="fa fa-trash-o" aria-hidden="true" /> {d('Delete')}</button>
            </div>
          </div>
          {bundle && <Ibox title={<><i className="fa fa-life-ring" aria-hidden="true" /> {d('Preview')}</>} tools={<button type="button" className="btn btn-primary btn-xs" disabled={busy} onClick={() => void run(async signal => {
            const data = await request<Bundle>('DiagnosticSupportExport', { ...command(), previewDigest: bundle.previewDigest }, signal); if (signal.aborted) return;
            const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })); const link = document.createElement('a'); link.href = url; link.download = 'resgrid-support-diagnostic.json'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
          })}><i className="fa fa-download" aria-hidden="true" /> {d('Export')}</button>}>
            <pre className="rgaa-pre">{JSON.stringify(bundle, null, 2)}</pre>
          </Ibox>}
        </div>}
      </div>

      <aside className="rgaa-workbench__side">
        <Ibox title={<><i className="fa fa-history" aria-hidden="true" /> {d('History')}</>}>
          {history.length > 0
            ? <ul className="rgaa-history-list">{history.map(item => <li key={item.id}>
              <button type="button" className="rgaa-plain rgaa-history-list__item" aria-current={report?.runId === item.id ? 'true' : undefined} disabled={busy}
                onClick={() => void run(async signal => { setBundle(null); const result = await request<Report>('Diagnostic', { runId: item.id, expectedRevision: item.revision }, signal); if (!signal.aborted) setReport(result); })}>
                <i className={`fa ${flowIcons[item.flow] ?? 'fa-stethoscope'}`} aria-hidden="true" />
                <span className="rgaa-history-list__text"><span>{d(`Flow.${item.flow}`)}</span><small><time dateTime={item.createdOnUtc}>{new Date(item.createdOnUtc).toLocaleString()}</time></small></span>
              </button>
            </li>)}</ul>
            : <p className="rgaa-muted rgaa-small">{t('Ui.None')}</p>}
        </Ibox>
        <div className="rgaa-privacy"><i className="fa fa-lock" aria-hidden="true" /><p>{d('Privacy')}</p></div>
      </aside>
    </div>
  </div>;
}
