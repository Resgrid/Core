import { useEffect, useRef, useState } from 'react';
import { apiFetchJson, ApiError } from '../../runtime/api';

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
export type PlanSource = { source: 'finding' | 'conversation'; sourceId: string; goal: string };
const endpoint = 'api/v4/AdminAssist/';

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
  const ruleTable = (rules: Rule[]) => rules.length > 0 && <table><thead><tr><th>{p('Rules')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{rules.map(r => <tr key={r.ruleId}><th>{t(r.titleKey)}</th><td>{t('Ui.' + r.before)}</td><td>{t('Ui.' + r.after)}</td></tr>)}</tbody></table>;
  const editable = !!plan?.id && plan.owned && !['Closed', 'Superseded'].includes(plan.status);
  return <section aria-busy={busy}>
    <h3>{p('Title')}</h3><p>{p('Boundary')}</p><p>{p('Privacy')}</p>
    <details open={!plan}><summary>{p('New')}</summary>
      {source && <p>{p('Source')}: {p(source.source)}</p>}
      <form onSubmit={e => { e.preventDefault(); void run(async signal => {
        const selectedTemplate = templates.find(item => item.id === template);
        const set = selectedTemplate?.changeSet ?? { version: 'configuration-change-set-v1', source: 'manual', sourceId: null, changes };
        const proposed: Draft = { goal, changeSet: source ? { ...set, source: source.source, sourceId: source.sourceId } : set, dispatchScenario: callId && scenarioTime ? { callId: Number(callId), simulationTimeUtc: new Date(scenarioTime).toISOString() } : null };
        draft.current = proposed; const preview = await request<Plan>('PlanDraft', proposed, signal); if (!signal.aborted) setPlan(preview);
      }); }}><fieldset disabled={busy}>
        <label>{p('Goal')} <textarea required maxLength={1000} value={goal} onChange={e => { setGoal(e.target.value); invalidate(); }} /></label>
        <p>{p('TemplateScope')}</p><label>{p('Template')} <select value={template} onChange={e => { setTemplate(e.target.value); invalidate(); }}><option value="">{p('Manual')}</option>{templates.map(item => <option key={item.id} value={item.id}>{t(item.labelKey)}</option>)}</select></label>
        {template && <p>{t(templates.find(item => item.id === template)?.descriptionKey ?? '')}</p>}
        {!template && <>
          <label>{p('SelectStep')} <select value={target} onChange={e => { setTarget(e.target.value); setValue(''); }}><option value="">{p('Select')}</option>
            <optgroup label={p('Settings')}>{settings.filter(s => !changes.some(c => c.catalogId === s.id)).map(s => <option key={s.id} value={s.id}>{t(s.labelKey)}</option>)}</optgroup>
            <optgroup label={p('Areas')}>{capabilities.filter(c => c.releaseStatus === 'available' && !changes.some(s => s.catalogId === c.id)).map(c => <option key={c.id} value={c.id}>{t(c.labelKey)}</option>)}</optgroup>
          </select></label>
          {selected && <label>{p('ProposedValue')} {selected.valueType === 'boolean' ? <select value={value} onChange={e => setValue(e.target.value)}><option value="">{p('Select')}</option><option value="true">{t('Ui.True')}</option><option value="false">{t('Ui.False')}</option></select> : <input type="number" min={0} max={target === 'setting.Require2FAForAdmins' ? 2 : 525600} step={1} value={value} onChange={e => setValue(e.target.value)} />}</label>}
          <button type="button" disabled={!target || (selected && !value) || changes.length >= 8} onClick={() => {
            setChanges([...changes, { id: 'step-' + (Math.max(0, ...changes.map(c => Number(c.id.slice(5)))) + 1), catalogId: target, boolean: selected?.valueType === 'boolean' ? value === 'true' : null, number: selected && selected.valueType !== 'boolean' ? Number(value) : null, prerequisites: [] }]); setTarget(''); setValue(''); invalidate();
          }}>{p('AddStep')}</button>
          <ol>{changes.map(change => <li key={change.id}>
            {t(settings.find(s => s.id === change.catalogId)?.labelKey ?? capabilities.find(c => c.id === change.catalogId)?.labelKey ?? change.catalogId)} · {change.boolean == null ? change.number ?? p('Configured') : String(change.boolean)}
            <label>{p('Prerequisites')} <select multiple value={change.prerequisites} onChange={e => { setChanges(changes.map(c => c.id === change.id ? { ...c, prerequisites: Array.from(e.target.selectedOptions, o => o.value) } : c)); invalidate(); }}>{changes.filter(c => c.id !== change.id).map(c => <option key={c.id} value={c.id}>{c.id}</option>)}</select></label>
            <button type="button" onClick={() => { setChanges(changes.filter(c => c.id !== change.id).map(c => ({ ...c, prerequisites: c.prerequisites.filter(id => id !== change.id) }))); invalidate(); }}>{p('Remove')}</button>
          </li>)}</ol>
        </>}
        {(template === 'shift-routing' || changes.some(c => c.catalogId === 'setting.DispatchShiftInsteadOfGroup' || c.catalogId === 'setting.AutoSetStatusForShiftDispatchPersonnel')) && <><p>{p('DispatchScenarioHelp')}</p><label>{p('CallId')} <input type="number" min={1} step={1} value={callId} onChange={e => { setCallId(e.target.value); invalidate(); }} /></label><label>{p('ScenarioTime')} <input type="datetime-local" value={scenarioTime} onChange={e => { setScenarioTime(e.target.value); invalidate(); }} /></label></>}
        <button type="submit" disabled={!goal.trim() || (!template && changes.length === 0)}>{p('Preview')}</button>
      </fieldset></form>
    </details>
    {busy && <button onClick={() => { pending.current?.abort(); setBusy(false); setNotice('Cancelled'); }}>{p('Cancel')}</button>}
    {notice && <p role="status">{p(notice)}</p>}
    <details><summary>{p('History')}</summary><p>{p('HistoryLimit')}</p>{history.map(row => <p key={row.id}><button disabled={busy} onClick={() => void run(async signal => {
      const next = await request<Plan>('Plan', { planId: row.id, expectedRevision: row.revision }, signal); if (!signal.aborted) { setPlan(next); draft.current = null; setPropagation({}); setBehavior({}); }
    })}>{p('State.' + row.status)} · {new Date(row.updatedOnUtc).toLocaleString()} · {p(row.owned ? 'Owned' : 'Shared')}</button></p>)}</details>
    {plan && <div aria-live="polite">
      <h3>{plan.goal}</h3><p>{p('State.' + plan.status)} · {p(plan.shared ? 'Shared' : 'Private')}</p><p>{p('AsOf')}: {new Date(plan.asOfUtc).toLocaleString()} · {plan.catalogVersion} · {plan.snapshotRevision}</p>
      <p>{p('Risk')}: {plan.impact.risk} · {p('UniquePeople')}: {plan.impact.uniqueAffectedPeople ?? p('Unknown')}</p>
      {plan.impact.limitKeys.map(key => <p key={key}>{t(key)}</p>)}
      {askAvailable && template && <button disabled={busy} onClick={() => void explain()}>{p('AskDraft')}</button>}
      {explanation.map((card, i) => <aside className="rgaa-card" key={i}><h4>{t(card.titleKey)}</h4>{card.textKeys.map((key, n) => <p key={n}>{t(key)}</p>)}</aside>)}
      <h4>{p('FinalImpact')}</h4>{plan.impact.finalMetrics.map(m => <p key={m.labelKey}>{t(m.labelKey)}: {m.before ?? p('Unknown')} → {m.after ?? p('Unknown')}</p>)}{ruleTable(plan.impact.finalRuleChanges)}
      {!plan.id && draft.current && <button disabled={busy} onClick={() => void run(async signal => { const saved = await request<Plan>('PlanCreate', { draft: draft.current, previewDigest: plan.previewDigest }, signal); if (!signal.aborted) setPlan(saved); })}>{p('Save')}</button>}
      {plan.id && <button disabled={busy} onClick={() => void run(async signal => { const next = await request<Plan>('Plan', { planId: plan.id, expectedRevision: plan.revision }, signal); if (!signal.aborted) setPlan(next); })}>{p('Refresh')}</button>}
      {editable && <>
        <button disabled={busy} onClick={() => void act('share', { shared: !plan.shared })}>{p(plan.shared ? 'MakePrivate' : 'Share')}</button><p>{p('SharingHelp')}</p>
        <form onSubmit={e => { e.preventDefault(); void act('review', { windowUtc: new Date(windowTime).toISOString(), fallback }); }}><fieldset disabled={busy}>
          <legend>{p('Review')}</legend><p>{p('ReviewHelp')}</p><label>{p('Window')} <input type="datetime-local" required value={windowTime} onChange={e => setWindowTime(e.target.value)} /></label>
          <label>{p('Fallback')} <textarea maxLength={1000} required value={fallback} onChange={e => setFallback(e.target.value)} /></label><button type="submit">{p('RecordReview')}</button>
        </fieldset></form>
      </>}
      {plan.review && <p>{p(plan.review.previewDigest === plan.previewDigest ? 'ReviewCurrent' : 'ReviewExpired')} · {new Date(plan.review.windowUtc).toLocaleString()} · {plan.review.fallback}</p>}
      <ol>{plan.steps.map(step => <li key={step.change.id} className="rgaa-card">
        <h4>{t(step.labelKey)} · {p('State.' + step.state)}</h4><p>{displayValue(step.currentValue)} → {displayValue(step.proposedValue)}</p><p>{t(step.rationaleKey)}</p><p>{t(step.instructionsKey)}</p>
        {step.change.prerequisites.length > 0 && <p>{p('Prerequisites')}: {step.change.prerequisites.map(id => t(plan.steps.find(s => s.change.id === id)?.labelKey ?? id)).join(', ')}</p>}
        <p>{t(step.impact.profile.audienceKey)}</p><p>{t(step.impact.profile.operationKey)}</p><p>{t(step.impact.profile.timingKey)}</p><p>{t(step.rollbackKey)}</p>
        <details><summary>{p('StepImpact')}</summary><table><thead><tr><th>{t('Impact.Effect')}</th><th>{t('Ui.Before')}</th><th>{t('Ui.After')}</th></tr></thead><tbody>{step.impact.metrics.map((m, i) => <tr key={i}><th>{t(m.labelKey)}</th><td>{m.before ?? p('Unknown')}</td><td>{m.after ?? p('Unknown')} · {t('Ui.' + m.state)}</td></tr>)}</tbody></table>{ruleTable(step.impact.ruleChanges)}{step.impact.limitKeys.map(key => <p key={key}>{t(key)}</p>)}<small>{step.impact.evaluatorVersion}</small></details>
        {/^\/User\/[A-Za-z0-9]+\/[A-Za-z0-9]+(?:\?aa=[A-Za-z0-9._-]+)?$/.test(step.destination) && <p><a href={step.destination + (step.destination.includes('?') ? '&' : '?') + 'aaReturn=plans'}>{p('OpenSource')}</a></p>}
        <p>{p('Saved')}: {p(step.verification.saved)} · {p('Rules')}: {p(step.verification.rule)} · {p('Propagated')}: {p(step.verification.propagated)} · {p('BehaviorTested')}: {p(step.verification.behaviorTested)}</p>
        {step.verification.recheckAfterUtc && <p>{p('Recheck')}: {new Date(step.verification.recheckAfterUtc).toLocaleString()}</p>}
        {askAvailable && plan.id && <button disabled={busy} onClick={() => void explain(step.change.id)}>{p('AskVerification')}</button>}
        {editable && <fieldset disabled={busy}>
          <button disabled={(plan.impact.risk === 'High' || plan.impact.risk === 'Critical') && plan.review?.previewDigest !== plan.previewDigest || step.change.prerequisites.some(id => !plan.steps.some(s => s.change.id === id && s.state === 'Done'))} onClick={() => void act('start', { stepId: step.change.id })}>{p('Start')}</button><button onClick={() => void act('verify', { stepId: step.change.id })}>{p('Verify')}</button>
          <button onClick={() => void act('skip', { stepId: step.change.id })}>{p('Skip')}</button><button onClick={() => void act('supersede-step', { stepId: step.change.id })}>{p('SupersedeStep')}</button>
          <details><summary>{p('Attest')}</summary><p>{p('AttestHelp')}</p>
            <label><input type="checkbox" checked={propagation[step.change.id] ?? false} onChange={e => setPropagation({ ...propagation, [step.change.id]: e.target.checked })} />{p('PropagationCheck')}</label>
            <label><input type="checkbox" checked={behavior[step.change.id] ?? false} onChange={e => setBehavior({ ...behavior, [step.change.id]: e.target.checked })} />{p('BehaviorCheck')}</label>
            <button disabled={((plan.impact.risk === 'High' || plan.impact.risk === 'Critical') && plan.review?.previewDigest !== plan.previewDigest) || !propagation[step.change.id] || !behavior[step.change.id] || step.verification.saved !== 'Confirmed' || step.verification.rule !== 'Pass'} onClick={() => void act('attest', { stepId: step.change.id, propagationChecked: true, behaviorTested: true })}>{p('RecordVerification')}</button>
          </details>
        </fieldset>}
      </li>)}</ol>
      <h4>{p('Communication')}</h4><p>{t(plan.impact.communicationDraftKey)}</p>
      {editable && <><button disabled={busy} onClick={() => void act('close')}>{p('Close')}</button><button disabled={busy} onClick={() => void act('supersede')}>{p('Supersede')}</button></>}
      {plan.id && plan.owned && <button disabled={busy} onClick={() => void act('delete')}>{p('Delete')}</button>}
      {plan.id && <><p>{p('ExportBoundary')}</p><button disabled={busy} onClick={() => void run(async signal => {
        const file = await request<{ base64: string }>('PlanExport', command('export'), signal); if (signal.aborted) return;
        const bytes = Uint8Array.from(atob(file.base64), c => c.charCodeAt(0)); const url = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
        const link = document.createElement('a'); link.href = url; link.download = 'resgrid-change-plan.pdf'; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
      })}>{p('Export')}</button></>}
    </div>}
  </section>;
}
