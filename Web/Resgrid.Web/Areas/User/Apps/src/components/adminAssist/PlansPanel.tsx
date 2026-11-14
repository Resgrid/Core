import { useEffect, useRef, useState } from 'react';
import { apiFetchJson, ApiError } from '../../runtime/api';
import { Chip, Ibox } from './SetupVisuals';

type Change = { id: string; catalogId: string; boolean: boolean | null; number: number | null; prerequisites: string[] };
type ChangeSet = { version: string; source: string; sourceId: string | null; changes: Change[] };
type Draft = { goal: string; templateId?: string | null; changeSet?: ChangeSet | null; dispatchScenario?: { callId: number; simulationTimeUtc: string } | null };
type Template = { id: string; labelKey: string; descriptionKey: string; changeSet: ChangeSet };
type Rule = { ruleId: string; titleKey: string; before: string; after: string };
type Verification = { saved: string; rule: string; propagated: string; behaviorTested: string; recheckAfterUtc: string | null };
type Step = { change: Change; labelKey: string; state: string; currentValue: string; proposedValue: string; rationaleKey: string; instructionsKey: string; destination: string; rollbackKey: string;
  verification: Verification; impact: { evaluatorVersion: string; profile: { risk: string; audienceKey: string; operationKey: string; timingKey: string }; metrics: { labelKey: string; before: number | null; after: number | null; state: string }[]; ruleChanges: Rule[]; limitKeys: string[] } };
type Plan = { id: string | null; revision: number; owned: boolean; shared: boolean; status: string; goal: string; changeSet: ChangeSet; steps: Step[];
  impact: { risk: string; finalMetrics: { labelKey: string; before: number | null; after: number | null }[]; uniqueAffectedPeople: number | null; finalRuleChanges: Rule[]; limitKeys: string[]; communicationDraftKey: string };
  asOfUtc: string; catalogVersion: string; snapshotRevision: string; previewDigest: string; operatingPacksAtCreation: string[];
  review: { previewDigest: string; windowUtc: string; fallback: string } | null };
type Row = { id: string; revision: number; owned: boolean; shared: boolean; status: string; updatedOnUtc: string };
type Tone = 'ok' | 'fail' | 'unknown' | 'info' | 'muted' | 'critical';
export type PlanSource = { source: 'finding' | 'conversation'; sourceId: string; goal: string };
const endpoint = 'api/v4/AdminAssist/';
const stateTone: Record<string, Tone> = { Proposed: 'info', InProgress: 'unknown', Done: 'ok', Complete: 'ok', Drifted: 'fail', Skipped: 'muted', Superseded: 'muted', Closed: 'muted' };
const checkTone = (value: string): Tone => ['Confirmed', 'Pass', 'HumanConfirmed', 'Configured'].includes(value) ? 'ok' : ['Fail', 'NotMatched', 'NotConfigured'].includes(value) ? 'fail' : 'unknown';
const riskTone = (risk: string): Tone => risk === 'Critical' ? 'critical' : risk === 'High' ? 'fail' : risk === 'Medium' ? 'unknown' : risk === 'Low' ? 'ok' : 'muted';
const stepNumber = (state: string) => state === 'Done' ? ' rgaa-step__num--done' : state === 'Drifted' ? ' rgaa-step__num--attention' : state === 'InProgress' ? ' rgaa-step__num--progress' : '';

export default function PlansPanel({ t, settings, capabilities, source, askAvailable }: { askAvailable?: boolean;
  t: (key: string) => string; settings: { id: string; valueType: string; labelKey: string }[];
  capabilities: { id: string; labelKey: string; releaseStatus: string }[]; source?: PlanSource | null;
}) {
  const p = (key: string) => t('Plan.' + key);
  const [goal, setGoal] = useState(source?.goal ?? ''); const [template, setTemplate] = useState('');
  const [templates, setTemplates] = useState<Template[]>([]); const [changes, setChanges] = useState<Change[]>([]);
  const [callId, setCallId] = useState(''); const [scenarioTime, setScenarioTime] = useState('');
  const [target, setTarget] = useState(''); const [value, setValue] = useState('');
  const [plan, setPlan] = useState<Plan | null>(null); const [history, setHistory] = useState<Row[]>([]);
  const [explanation, setExplanation] = useState<{ titleKey: string; textKeys: string[] }[]>([]);
  const [busy, setBusy] = useState(false); const [notice, setNotice] = useState('');
  const [windowTime, setWindowTime] = useState(''); const [fallback, setFallback] = useState('');
  const [propagation, setPropagation] = useState<Record<string, boolean>>({}); const [behavior, setBehavior] = useState<Record<string, boolean>>({});
  const pending = useRef<AbortController | null>(null); const draft = useRef<Draft | null>(null);
  const request = <T,>(action: string, body: unknown, signal: AbortSignal) => apiFetchJson<T>(endpoint + action, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal });
  const clear = () => { setExplanation([]); setPlan(null); setHistory([]); setGoal(''); setChanges([]); setFallback(''); setPropagation({}); setBehavior({}); draft.current = null; };
  useEffect(() => {
    const load = (conceal = false) => {
      pending.current?.abort(); const abort = new AbortController(); pending.current = abort; if (conceal) clear(); setBusy(false);
      void Promise.all([apiFetchJson<Template[]>(endpoint + 'PlanTemplates', { signal: abort.signal }), apiFetchJson<Row[]>(endpoint + 'Plans', { signal: abort.signal })])
        .then(([items, rows]) => { if (!abort.signal.aborted) { setTemplates(items); setHistory(rows); } }).catch(() => { if (!abort.signal.aborted) setNotice('Unavailable'); });
    };
    const conceal = () => load(true); load(); window.addEventListener('resgrid:adp-reveal-changed', conceal);
    return () => { pending.current?.abort(); window.removeEventListener('resgrid:adp-reveal-changed', conceal); };
  }, []);
  async function run(action: (signal: AbortSignal) => Promise<void>) {
    if (busy) return; pending.current?.abort(); const abort = new AbortController(); pending.current = abort; setBusy(true); setNotice('');
    try { await action(abort.signal); if (!abort.signal.aborted) { const rows = await apiFetchJson<Row[]>(endpoint + 'Plans', { signal: abort.signal }); if (!abort.signal.aborted) setHistory(rows); } }
    catch (error) { if (!abort.signal.aborted) { setNotice(error instanceof ApiError && error.status === 409 ? 'Changed' : 'Unavailable'); setPlan(null); if (error instanceof ApiError && error.status === 403) clear(); } }
    finally { if (pending.current === abort) setBusy(false); }
  }
  const command = (operation: string, more: object = {}) => ({ planId: plan?.id, expectedRevision: plan?.revision, previewDigest: plan?.previewDigest, operation, ...more });
  const act = (operation: string, more: object = {}) => run(async signal => {
    const next = await request<Plan>('PlanCommand', command(operation, more), signal);
    if (!signal.aborted) { setPlan(operation === 'delete' ? null : next); setPropagation({}); setBehavior({}); }
  });
  const invalidate = () => { setExplanation([]); setPlan(null); draft.current = null; };
  const explain = (stepId?: string) => run(async signal => {
    setExplanation([]);
    const answer = await request<{ evidence: { titleKey: string; textKeys: string[] }[] }>('Ask', { question: p('AskQuestion'), topic: 'plans',
      planTemplateId: stepId ? null : template, planId: stepId ? plan?.id : null, planStepId: stepId ?? null }, signal);
    if (!signal.aborted) setExplanation(answer.evidence);
  });
  const selected = settings.find(s => s.id === target);
  const displayValue = (value: string) => value === 'true' ? t('Ui.True') : value === 'false' ? t('Ui.False') : ['Unknown', 'Configured', 'NotConfigured'].includes(value) ? p(value) : value;
  const ruleTable = (rules: Rule[]) => rules.length > 0 && <div className="table-responsive"><table className="table table-condensed rgaa-impact-table">
    <thead><tr><th>{p('Rules')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead>
    <tbody>{rules.map(r => <tr key={r.ruleId}><th scope="row">{t(r.titleKey)}</th><td>{t('Ui.' + r.before)}</td><td>{t('Ui.' + r.after)}</td></tr>)}</tbody></table></div>;
  const editable = !!plan?.id && plan.owned && !['Closed', 'Superseded'].includes(plan.status);
  const highRisk = !!plan && (plan.impact.risk === 'High' || plan.impact.risk === 'Critical');
  const reviewCurrent = !!plan?.review && plan.review.previewDigest === plan.previewDigest;
  const changeLabel = (catalogId: string) => t(settings.find(s => s.id === catalogId)?.labelKey ?? capabilities.find(c => c.id === catalogId)?.labelKey ?? catalogId);
  const scenario = template === 'shift-routing' || changes.some(c => c.catalogId === 'setting.DispatchShiftInsteadOfGroup' || c.catalogId === 'setting.AutoSetStatusForShiftDispatchPersonnel');
  const noticeIsProblem = notice === 'Unavailable' || notice === 'Changed';

  return <div aria-busy={busy}>
    <p className="rgaa-lead">{p('Boundary')}</p>
    <div className="rgaa-workbench">
      <div>
        {busy && <div className="rgaa-busy-bar" role="status"><span><i className="fa fa-spinner fa-spin" aria-hidden="true" /><span className="sr-only">{t('Ui.Loading')}</span></span>
          <button type="button" className="btn btn-white btn-xs" onClick={() => { pending.current?.abort(); setBusy(false); setNotice('Cancelled'); }}><i className="fa fa-stop" aria-hidden="true" /> {p('Cancel')}</button></div>}
        {notice && <div className={`alert ${noticeIsProblem ? 'alert-warning' : 'alert-info'}`} role="status">{p(notice)}</div>}

        <details className="ibox rgaa-fold" open={!plan}>
          <summary className="ibox-title"><h5><i className="fa fa-plus-circle" aria-hidden="true" /> {p('New')}</h5></summary>
          <div className="ibox-content">
            <form onSubmit={e => { e.preventDefault(); void run(async signal => {
              const selectedTemplate = templates.find(item => item.id === template);
              const set = selectedTemplate?.changeSet ?? { version: 'configuration-change-set-v1', source: 'manual', sourceId: null, changes };
              const proposed: Draft = { goal, changeSet: source ? { ...set, source: source.source, sourceId: source.sourceId } : set, dispatchScenario: callId && scenarioTime ? { callId: Number(callId), simulationTimeUtc: new Date(scenarioTime).toISOString() } : null };
              draft.current = proposed; const preview = await request<Plan>('PlanDraft', proposed, signal); if (!signal.aborted) setPlan(preview);
            }); }}><fieldset disabled={busy}>
              {source && <p><Chip tone="info" icon="fa-link">{p('Source')}: {p(source.source)}</Chip></p>}
              <div className="form-group"><label htmlFor="rgaa-plan-goal">{p('Goal')}</label>
                <textarea id="rgaa-plan-goal" className="form-control" rows={2} required maxLength={1000} value={goal} onChange={e => { setGoal(e.target.value); invalidate(); }} /></div>
              <div className="form-group"><label htmlFor="rgaa-plan-template">{p('Template')}</label>
                <select id="rgaa-plan-template" className="form-control input-sm" value={template} onChange={e => { setTemplate(e.target.value); invalidate(); }}>
                  <option value="">{p('Manual')}</option>{templates.map(item => <option key={item.id} value={item.id}>{t(item.labelKey)}</option>)}
                </select>
                <p className="help-block">{p('TemplateScope')}</p></div>
              {template && <p className="rgaa-note">{t(templates.find(item => item.id === template)?.descriptionKey ?? '')}</p>}
              {!template && <>
                <div className="rgaa-form-row">
                  <div className="form-group"><label htmlFor="rgaa-plan-target">{p('SelectStep')}</label>
                    <select id="rgaa-plan-target" className="form-control input-sm" value={target} onChange={e => { setTarget(e.target.value); setValue(''); }}><option value="">{p('Select')}</option>
                      <optgroup label={p('Settings')}>{settings.filter(s => !changes.some(c => c.catalogId === s.id)).map(s => <option key={s.id} value={s.id}>{t(s.labelKey)}</option>)}</optgroup>
                      <optgroup label={p('Areas')}>{capabilities.filter(c => c.releaseStatus === 'available' && !changes.some(s => s.catalogId === c.id)).map(c => <option key={c.id} value={c.id}>{t(c.labelKey)}</option>)}</optgroup>
                    </select></div>
                  {selected && <div className="form-group"><label htmlFor="rgaa-plan-value">{p('ProposedValue')}</label>
                    {selected.valueType === 'boolean'
                      ? <select id="rgaa-plan-value" className="form-control input-sm" value={value} onChange={e => setValue(e.target.value)}><option value="">{p('Select')}</option><option value="true">{t('Ui.True')}</option><option value="false">{t('Ui.False')}</option></select>
                      : <input id="rgaa-plan-value" className="form-control input-sm" type="number" min={0} max={target === 'setting.Require2FAForAdmins' ? 2 : 525600} step={1} value={value} onChange={e => setValue(e.target.value)} />}</div>}
                  <button type="button" className="btn btn-white btn-sm" disabled={!target || (selected && !value) || changes.length >= 8} onClick={() => {
                    setChanges([...changes, { id: 'step-' + (Math.max(0, ...changes.map(c => Number(c.id.slice(5)))) + 1), catalogId: target, boolean: selected?.valueType === 'boolean' ? value === 'true' : null, number: selected && selected.valueType !== 'boolean' ? Number(value) : null, prerequisites: [] }]); setTarget(''); setValue(''); invalidate();
                  }}><i className="fa fa-plus" aria-hidden="true" /> {p('AddStep')}</button>
                </div>
                {changes.length > 0 && <ol className="rgaa-plan-changes">{changes.map((change, index) => <li key={change.id}>
                  <span className="rgaa-step__num">{index + 1}</span>
                  <div className="rgaa-plan-changes__main">
                    <p className="rgaa-change"><strong>{changeLabel(change.catalogId)}</strong><i className="fa fa-long-arrow-right" aria-hidden="true" />
                      <span className="rgaa-change__to">{change.boolean == null ? change.number ?? p('Configured') : displayValue(String(change.boolean))}</span></p>
                    {changes.length > 1 && <div className="form-group"><label htmlFor={`rgaa-plan-pre-${change.id}`} className="rgaa-small">{p('Prerequisites')}</label>
                      <select id={`rgaa-plan-pre-${change.id}`} className="form-control input-sm" multiple value={change.prerequisites} onChange={e => { setChanges(changes.map(c => c.id === change.id ? { ...c, prerequisites: Array.from(e.target.selectedOptions, o => o.value) } : c)); invalidate(); }}>
                        {changes.filter(c => c.id !== change.id).map(c => <option key={c.id} value={c.id}>{changes.indexOf(c) + 1}. {changeLabel(c.catalogId)}</option>)}
                      </select></div>}
                  </div>
                  <button type="button" className="btn btn-white btn-xs rgaa-btn-danger" onClick={() => { setChanges(changes.filter(c => c.id !== change.id).map(c => ({ ...c, prerequisites: c.prerequisites.filter(id => id !== change.id) }))); invalidate(); }}><i className="fa fa-times" aria-hidden="true" /> {p('Remove')}</button>
                </li>)}</ol>}
              </>}
              {scenario && <div className="rgaa-note"><p>{p('DispatchScenarioHelp')}</p>
                <div className="rgaa-form-row">
                  <div className="form-group"><label htmlFor="rgaa-plan-call">{p('CallId')}</label><input id="rgaa-plan-call" className="form-control input-sm" type="number" min={1} step={1} value={callId} onChange={e => { setCallId(e.target.value); invalidate(); }} /></div>
                  <div className="form-group"><label htmlFor="rgaa-plan-time">{p('ScenarioTime')}</label><input id="rgaa-plan-time" className="form-control input-sm" type="datetime-local" value={scenarioTime} onChange={e => { setScenarioTime(e.target.value); invalidate(); }} /></div>
                </div></div>}
              <div className="rgaa-form-actions"><button type="submit" className="btn btn-primary btn-sm" disabled={!goal.trim() || (!template && changes.length === 0)}><i className="fa fa-eye" aria-hidden="true" /> {p('Preview')}</button></div>
            </fieldset></form>
          </div>
        </details>

        {plan && <div aria-live="polite">
          <Ibox title={plan.goal} tools={<span className="rgaa-ibox-chips"><Chip tone={stateTone[plan.status] ?? 'muted'}>{p('State.' + plan.status)}</Chip>
            <Chip tone={plan.shared ? 'info' : 'muted'} icon={plan.shared ? 'fa-users' : 'fa-lock'}>{p(plan.shared ? 'Shared' : 'Private')}</Chip></span>}>
            <div className="rgaa-plan-summary">
              <div><span className="rgaa-metric__label">{p('Risk')}</span><span><Chip tone={riskTone(plan.impact.risk)} icon={highRisk ? 'fa-exclamation-triangle' : undefined}>{plan.impact.risk}</Chip></span></div>
              <div><span className="rgaa-metric__label">{p('UniquePeople')}</span><span className="rgaa-plan-summary__value">{plan.impact.uniqueAffectedPeople ?? p('Unknown')}</span></div>
              <div><span className="rgaa-metric__label">{p('AsOf')}</span><span><time dateTime={plan.asOfUtc}>{new Date(plan.asOfUtc).toLocaleString()}</time></span>
                <span className="rgaa-small rgaa-muted">{plan.catalogVersion} · {plan.snapshotRevision}</span></div>
            </div>
            {plan.impact.limitKeys.map(key => <p key={key} className="rgaa-small rgaa-muted"><i className="fa fa-info-circle" aria-hidden="true" /> {t(key)}</p>)}
            <div className="rgaa-actions-row">
              {!plan.id && draft.current && <button type="button" className="btn btn-primary btn-sm" disabled={busy} onClick={() => void run(async signal => { const saved = await request<Plan>('PlanCreate', { draft: draft.current, previewDigest: plan.previewDigest }, signal); if (!signal.aborted) setPlan(saved); })}><i className="fa fa-floppy-o" aria-hidden="true" /> {p('Save')}</button>}
              {plan.id && <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void run(async signal => { const next = await request<Plan>('Plan', { planId: plan.id, expectedRevision: plan.revision }, signal); if (!signal.aborted) setPlan(next); })}><i className="fa fa-refresh" aria-hidden="true" /> {p('Refresh')}</button>}
              {editable && <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void act('share', { shared: !plan.shared })}><i className={`fa ${plan.shared ? 'fa-lock' : 'fa-users'}`} aria-hidden="true" /> {p(plan.shared ? 'MakePrivate' : 'Share')}</button>}
              {askAvailable && template && <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void explain()}><i className="fa fa-comments-o" aria-hidden="true" /> {p('AskDraft')}</button>}
            </div>
            {editable && <p className="help-block">{p('SharingHelp')}</p>}
            {explanation.length > 0 && <div className="rgaa-evidence-grid rgaa-plan-explain">{explanation.map((card, i) => <aside className="rgaa-evidence-card" key={i}>
              <header className="rgaa-evidence-card__head"><h5>{t(card.titleKey)}</h5></header>{card.textKeys.map((key, n) => <p key={n}>{t(key)}</p>)}</aside>)}</div>}
            <h4 className="rgaa-section-title">{p('FinalImpact')}</h4>
            {plan.impact.finalMetrics.length > 0 && <div className="table-responsive"><table className="table table-condensed rgaa-impact-table">
              <thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead>
              <tbody>{plan.impact.finalMetrics.map(m => <tr key={m.labelKey}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? p('Unknown')}</td><td>{m.after ?? p('Unknown')}</td></tr>)}</tbody></table></div>}
            {ruleTable(plan.impact.finalRuleChanges)}
          </Ibox>

          {(editable || plan.review) && <Ibox title={<><i className="fa fa-calendar-check-o" aria-hidden="true" /> {p('Review')}</>}
            tools={plan.review && <Chip tone={reviewCurrent ? 'ok' : 'unknown'} icon={reviewCurrent ? 'fa-check' : 'fa-exclamation'}>{p(reviewCurrent ? 'ReviewCurrent' : 'ReviewExpired')}</Chip>}>
            {plan.review && <p className="rgaa-small"><i className="fa fa-clock-o" aria-hidden="true" /> {new Date(plan.review.windowUtc).toLocaleString()} · {plan.review.fallback}</p>}
            {editable && <form onSubmit={e => { e.preventDefault(); void act('review', { windowUtc: new Date(windowTime).toISOString(), fallback }); }}><fieldset disabled={busy}>
              <p className="help-block">{p('ReviewHelp')}</p>
              <div className="rgaa-form-row">
                <div className="form-group"><label htmlFor="rgaa-plan-window">{p('Window')}</label><input id="rgaa-plan-window" className="form-control input-sm" type="datetime-local" required value={windowTime} onChange={e => setWindowTime(e.target.value)} /></div>
              </div>
              <div className="form-group"><label htmlFor="rgaa-plan-fallback">{p('Fallback')}</label><textarea id="rgaa-plan-fallback" className="form-control" rows={2} maxLength={1000} required value={fallback} onChange={e => setFallback(e.target.value)} /></div>
              <div className="rgaa-form-actions"><button type="submit" className="btn btn-primary btn-sm"><i className="fa fa-check-square-o" aria-hidden="true" /> {p('RecordReview')}</button></div>
            </fieldset></form>}
          </Ibox>}

          <Ibox title={<><i className="fa fa-list-ol" aria-hidden="true" /> {p('Verification')}</>}>
            <p className="help-block">{p('VerificationBoundary')}</p>
            <ol className="rgaa-plan-steps">{plan.steps.map((step, index) => {
              const prerequisitesDone = !step.change.prerequisites.some(id => !plan.steps.some(s => s.change.id === id && s.state === 'Done'));
              const destination = /^\/User\/[A-Za-z0-9]+\/[A-Za-z0-9]+(?:\?aa=[A-Za-z0-9._-]+)?$/.test(step.destination) ? step.destination + (step.destination.includes('?') ? '&' : '?') + 'aaReturn=plans' : undefined;
              return <li key={step.change.id} className="rgaa-plan-step">
                <header className="rgaa-plan-step__head">
                  <span className={`rgaa-step__num${stepNumber(step.state)}`}>{index + 1}</span>
                  <h4>{t(step.labelKey)}</h4>
                  <Chip tone={stateTone[step.state] ?? 'muted'}>{p('State.' + step.state)}</Chip>
                </header>
                <p className="rgaa-change"><span className="rgaa-change__from">{displayValue(step.currentValue)}</span><i className="fa fa-long-arrow-right" aria-hidden="true" /><span className="rgaa-change__to">{displayValue(step.proposedValue)}</span></p>
                <p>{t(step.rationaleKey)}</p>
                <p>{t(step.instructionsKey)}</p>
                {step.change.prerequisites.length > 0 && <p className="rgaa-small"><strong>{p('Prerequisites')}:</strong> {step.change.prerequisites.map(id => t(plan.steps.find(s => s.change.id === id)?.labelKey ?? id)).join(', ')}</p>}
                <ul className="rgaa-plan-step__facts">
                  <li><i className="fa fa-users" aria-hidden="true" /><span>{t(step.impact.profile.audienceKey)}</span></li>
                  <li><i className="fa fa-cogs" aria-hidden="true" /><span>{t(step.impact.profile.operationKey)}</span></li>
                  <li><i className="fa fa-clock-o" aria-hidden="true" /><span>{t(step.impact.profile.timingKey)}</span></li>
                  <li><i className="fa fa-undo" aria-hidden="true" /><span>{t(step.rollbackKey)}</span></li>
                </ul>
                <details className="rgaa-plan-step__more"><summary className="rgaa-small">{p('StepImpact')}</summary>
                  <div className="table-responsive"><table className="table table-condensed rgaa-impact-table"><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead>
                    <tbody>{step.impact.metrics.map((m, i) => <tr key={i}><th scope="row">{t(m.labelKey)}</th><td>{m.before ?? p('Unknown')}</td><td>{m.after ?? p('Unknown')} · {t('Ui.' + m.state)}</td></tr>)}</tbody></table></div>
                  {ruleTable(step.impact.ruleChanges)}{step.impact.limitKeys.map(key => <p key={key} className="rgaa-small rgaa-muted">{t(key)}</p>)}<small className="rgaa-muted">{step.impact.evaluatorVersion}</small>
                </details>
                <div className="rgaa-plan-step__checks">
                  <Chip tone={checkTone(step.verification.saved)}>{p('Saved')}: {p(step.verification.saved)}</Chip>
                  <Chip tone={checkTone(step.verification.rule)}>{p('Rules')}: {p(step.verification.rule)}</Chip>
                  <Chip tone={checkTone(step.verification.propagated)}>{p('Propagated')}: {p(step.verification.propagated)}</Chip>
                  <Chip tone={checkTone(step.verification.behaviorTested)}>{p('BehaviorTested')}: {p(step.verification.behaviorTested)}</Chip>
                </div>
                {step.verification.recheckAfterUtc && <p className="rgaa-small rgaa-muted"><i className="fa fa-clock-o" aria-hidden="true" /> {p('Recheck')}: {new Date(step.verification.recheckAfterUtc).toLocaleString()}</p>}
                <div className="rgaa-actions-row">
                  {destination && <a className="btn btn-white btn-xs" href={destination}><i className="fa fa-external-link" aria-hidden="true" /> {p('OpenSource')}</a>}
                  {askAvailable && plan.id && <button type="button" className="btn btn-white btn-xs" disabled={busy} onClick={() => void explain(step.change.id)}><i className="fa fa-comments-o" aria-hidden="true" /> {p('AskVerification')}</button>}
                </div>
                {editable && <fieldset disabled={busy}>
                  <div className="rgaa-actions-row">
                    <button type="button" className="btn btn-primary btn-xs" disabled={(highRisk && !reviewCurrent) || !prerequisitesDone} onClick={() => void act('start', { stepId: step.change.id })}><i className="fa fa-play" aria-hidden="true" /> {p('Start')}</button>
                    <button type="button" className="btn btn-white btn-xs" onClick={() => void act('verify', { stepId: step.change.id })}><i className="fa fa-refresh" aria-hidden="true" /> {p('Verify')}</button>
                    <button type="button" className="btn btn-white btn-xs" onClick={() => void act('skip', { stepId: step.change.id })}><i className="fa fa-step-forward" aria-hidden="true" /> {p('Skip')}</button>
                    <button type="button" className="btn btn-white btn-xs" onClick={() => void act('supersede-step', { stepId: step.change.id })}><i className="fa fa-clone" aria-hidden="true" /> {p('SupersedeStep')}</button>
                  </div>
                  <details className="rgaa-plan-step__more"><summary className="rgaa-small">{p('Attest')}</summary>
                    <p className="help-block">{p('AttestHelp')}</p>
                    <label className="rgaa-check"><input type="checkbox" checked={propagation[step.change.id] ?? false} onChange={e => setPropagation({ ...propagation, [step.change.id]: e.target.checked })} />{p('PropagationCheck')}</label>
                    <label className="rgaa-check"><input type="checkbox" checked={behavior[step.change.id] ?? false} onChange={e => setBehavior({ ...behavior, [step.change.id]: e.target.checked })} />{p('BehaviorCheck')}</label>
                    <button type="button" className="btn btn-primary btn-xs" disabled={(highRisk && !reviewCurrent) || !propagation[step.change.id] || !behavior[step.change.id] || step.verification.saved !== 'Confirmed' || step.verification.rule !== 'Pass'} onClick={() => void act('attest', { stepId: step.change.id, propagationChecked: true, behaviorTested: true })}><i className="fa fa-check" aria-hidden="true" /> {p('RecordVerification')}</button>
                  </details>
                </fieldset>}
              </li>;
            })}</ol>
          </Ibox>

          <Ibox title={<><i className="fa fa-envelope-o" aria-hidden="true" /> {p('Communication')}</>}><p className="rgaa-note">{t(plan.impact.communicationDraftKey)}</p></Ibox>

          {plan.id && <div className="rgaa-footbar">
            <p>{p('ExportBoundary')}</p>
            <div className="rgaa-actions-row">
              <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void run(async signal => {
                const file = await request<{ base64: string }>('PlanExport', command('export'), signal); if (signal.aborted) return;
                const bytes = Uint8Array.from(atob(file.base64), c => c.charCodeAt(0)); const url = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
                const link = document.createElement('a'); link.href = url; link.download = 'resgrid-change-plan.pdf'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
              })}><i className="fa fa-file-pdf-o" aria-hidden="true" /> {p('Export')}</button>
              {editable && <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void act('close')}><i className="fa fa-check-circle-o" aria-hidden="true" /> {p('Close')}</button>}
              {editable && <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void act('supersede')}><i className="fa fa-clone" aria-hidden="true" /> {p('Supersede')}</button>}
              {plan.id && plan.owned && <button type="button" className="btn btn-white btn-sm rgaa-btn-danger" disabled={busy} onClick={() => void act('delete')}><i className="fa fa-trash-o" aria-hidden="true" /> {p('Delete')}</button>}
            </div>
          </div>}
        </div>}
      </div>

      <aside className="rgaa-workbench__side">
        <Ibox title={<><i className="fa fa-history" aria-hidden="true" /> {p('History')}</>}>
          <p className="rgaa-small rgaa-muted">{p('HistoryLimit')}</p>
          {history.length > 0
            ? <ul className="rgaa-history-list">{history.map(row => <li key={row.id}>
              <button type="button" className="rgaa-plain rgaa-history-list__item" aria-current={plan?.id === row.id ? 'true' : undefined} disabled={busy} onClick={() => void run(async signal => {
                const next = await request<Plan>('Plan', { planId: row.id, expectedRevision: row.revision }, signal); if (!signal.aborted) { setPlan(next); draft.current = null; setPropagation({}); setBehavior({}); }
              })}>
                <i className={`fa ${row.shared ? 'fa-users' : 'fa-file-text-o'}`} aria-hidden="true" />
                <span className="rgaa-history-list__text"><time dateTime={row.updatedOnUtc}>{new Date(row.updatedOnUtc).toLocaleString()}</time>
                  <small>{p(row.owned ? 'Owned' : 'Shared')}</small></span>
                <Chip tone={stateTone[row.status] ?? 'muted'}>{p('State.' + row.status)}</Chip>
              </button>
            </li>)}</ul>
            : <p className="rgaa-muted rgaa-small">{t('Ui.None')}</p>}
        </Ibox>
        <div className="rgaa-privacy"><i className="fa fa-lock" aria-hidden="true" /><p>{p('Privacy')}</p></div>
      </aside>
    </div>
  </div>;
}
