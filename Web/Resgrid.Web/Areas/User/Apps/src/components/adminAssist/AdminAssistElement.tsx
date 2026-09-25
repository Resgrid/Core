import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, apiFetchJson } from '../../runtime/api';
import './adminAssist.css';
import ImpactPreview from './ImpactPreview';
import CapacityPreview from './CapacityPreview';
import DispatchPreview from './DispatchPreview';
import PermissionPreview from './PermissionPreview';
import ModulePreview from './ModulePreview';
import TextImportPreview from './TextImportPreview';
import RetentionPreview from './RetentionPreview';
import NotificationPreview from './NotificationPreview';
import SecurityPreview from './SecurityPreview';
import AreaSetupChoice from './AreaSetupChoice';
import SetupJourney from './SetupJourney';

export interface AdminAssistElementProps {
  page: string;
  setup: boolean;
  loadingLabel: string;
  errorLabel: string;
}
type AreaChoice = 'UseNow' | 'LearnLater' | 'NotApplicable';
type Workspace = { scopeRevision?: number; revision: number; mode: string; areas: Record<string, AreaChoice>; areaReasons?: Record<string, string>; learnedCapabilityIds: string[]; interestedCapabilityIds: string[]; reviewedOnUtc: string | null; revisitOnUtc?: string | null;
  reviewEvidence?: { scopeRevision?: number; catalogVersion: string; snapshotRevision: string; asOfUtc: string; required: number; verified: number; failed: number; unknown: number } | null };
type Location = { url: string; field: string | null };
type Capability = { id: string; areaId: string; labelKey: string; purposeKey: string; valueKey: string; exampleKey: string; adoptionKey: string; releaseStatus: string; requirements: { kind: string; id: string }[] };
type Finding = { ruleId: string; areaId: string; severity: string; result: string; titleKey: string; explanationKey: string; nextActionKey: string; destination: string; reasonCode: string | null; scopeIndependent: boolean };
type Catalog = {
  moduleImpactTypes: string[]; permissionImpactTypes: string[]; impactSettings: string[]; version: string; strings: Record<string, string>; rightToLeft: boolean; canSetup: boolean;
  areas: { id: string; labelKey: string; purposeKey: string }[];
  capabilities: Capability[];
  settings: { id: string; valueType: string; labelKey: string; helpKey: string; location: Location; impact: { risk: string; timingKey: string; reversibilityKey: string } }[];
  articles: { id: string; locale: string; body: string; sourcePath: string; anchor: string; packVersion: string }[];
  packs: { id: string; labelKey: string; purposeKey: string; areaIds: string[]; prerequisiteKeys: string[] }[];
};
type Overview = {
  catalogVersion: string; workspace: Workspace;
  report: { verified: number; required: number; unknown: number; failed: number; hasCriticalUncertainty: boolean; selectedAreas: string[]; uncheckedAreaIds: string[]; findings: Finding[]; snapshot: { asOfUtc: string; consistent: boolean; revision: string; evidence: Record<string, { state: string; code: string | null }> } };
  access: { capabilityId: string; state: string; reasonCodes: string[]; canConfigure: boolean; subscriptionDestination: string | null; destination: string | null }[];
  capabilitySetup?: { capabilityId: string; state: string; opportunityKey: string; guidanceKey: string; ruleIds: string[] }[];
};
type History = { id: string; occurredOnUtc: string; action: string; subjectId: string; beforeCode: string; afterCode: string };
type WorkItem = { adminAssistFindingId: string; ruleId: string; result: number; reviewStatus: number; ownerId: string | null; reviewOn: string | null; exceptionUntil: string | null; content: string | null; revision: number; lastObservedOn: string };
type SearchHit = { id: string; titleKey: string; excerpt: string; sourcePath: string; anchor: string; packVersion: string; locale: string };
type Followup = { digestsAvailable: boolean; preferences: { revision: number; digestEnabled: boolean; quietStartHour: number; quietEndHour: number; lastAttemptOutcome: string | null }; worker: { lastEvaluatedOn: string | null } };
const endpoint = 'api/v4/AdminAssist/';
// No external URLs, scheme-relative links or redirects from catalog data.
const safeLocalLink = (url: string | null | undefined) => url && /^\/User\/[A-Za-z0-9]+\/[A-Za-z0-9]+(?:\?aa=[A-Za-z0-9._-]+)?$/.test(url) ? url : undefined;

export default function AdminAssistElement({ page, setup, loadingLabel, errorLabel }: AdminAssistElementProps) {
  const [tab, setTab] = useState(page);
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [overview, setOverview] = useState<Overview | null>(null);
  const localLink = (url: string | null | undefined) => {
    const destination = safeLocalLink(url);
    return !destination || !catalog?.canSetup ? destination : `${destination}${destination.includes('?') ? '&' : '?'}aaReturn=${tab === 'wizard' ? 'wizard' : 'report'}`;
  };
  const [query, setQuery] = useState('');
  const [addonsOnly, setAddonsOnly] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [history, setHistory] = useState<History[]>([]);
  const [hasMore, setHasMore] = useState(true);
  const [worklist, setWorklist] = useState<WorkItem[]>([]);
  const [hits, setHits] = useState<SearchHit[]>([]);
  const [workFilter, setWorkFilter] = useState('active');
  const [followup, setFollowup] = useState<Followup | null>(null);
  const request = useRef<AbortController | null>(null);
  const t = (key: string) => catalog?.strings[key] ?? key;
  const ui = (key: string) => t(`Ui.${key}`);

  const reload = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError('');
    try {
      const [nextCatalog, nextOverview] = await Promise.all([
        apiFetchJson<Catalog>(`${endpoint}Catalog`, { signal: controller.signal }, { setup }),
        apiFetchJson<Overview>(`${endpoint}Overview`, { signal: controller.signal }, { setup }),
      ]);
      if (controller.signal.aborted) return;
      if (nextCatalog.version !== nextOverview.catalogVersion) throw new Error('CatalogChanged');
      setCatalog(nextCatalog);
      setOverview(nextOverview);
    } catch (failure) {
      if (!controller.signal.aborted) { setOverview(null); setError(errorLabel); }
    } finally { if (!controller.signal.aborted) setBusy(false); }
  }, [setup, errorLabel]);
  useEffect(() => { void reload(); return () => request.current?.abort(); }, [reload]);

  async function save(operation: string, targetId: string | null = null, choice: string | null = null, reasonCode: string | null = null, revisitOnUtc: string | null = null) {
    if (!overview || !catalog || busy) return;
    setBusy(true);
    setError('');
    try {
      const workspace = await apiFetchJson<Workspace>(`${endpoint}Setup`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision: overview.workspace.revision, operation, targetId, choice, catalogVersion: catalog.version, reasonCode, revisitOnUtc,
          evidenceRevision: operation === 'review' ? overview.report.snapshot.revision : null }),
      });
      setOverview(previous => previous && { ...previous, workspace });
      await reload();
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) { await reload(); setError(ui('Conflict')); }
      else setError(ui('SaveError'));
    } finally { setBusy(false); }
  }

  async function loadHistory(reset = false) {
    setBusy(true);
    try {
      const rows = await apiFetchJson<History[]>(`${endpoint}History`, undefined, { skip: reset ? 0 : history.length, take: 30 });
      setHistory(current => reset ? rows : [...current, ...rows]);
      setHasMore(rows.length === 30);
    } catch { setError(errorLabel); }
    finally { setBusy(false); }
  }

  async function loadWorklist() {
    setBusy(true); setError('');
    try {
      const [items, preferences] = await Promise.all([apiFetchJson<WorkItem[]>(`${endpoint}Worklist`), apiFetchJson<Followup>(`${endpoint}Preferences`)]);
      setWorklist(items); setFollowup(preferences);
    }
    catch { setWorklist([]); setError(errorLabel); }
    finally { setBusy(false); }
  }
  async function verify() {
    setBusy(true); setError('');
    try {
      if (!setup) await apiFetchJson(`${endpoint}Verify`, { method: 'POST' });
      await reload();
      if (!setup) await loadWorklist();
    } catch (failure) { setError(failure instanceof ApiError && failure.status === 409 ? ui('Conflict') : errorLabel); }
    finally { setBusy(false); }
  }
  async function review(item: WorkItem, operation: string, form?: HTMLFormElement) {
    setBusy(true); setError('');
    const fields = form ? new FormData(form) : null;
    const date = fields?.get('date')?.toString();
    try {
      await apiFetchJson(`${endpoint}Review`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
        findingId: item.adminAssistFindingId, expectedRevision: item.revision, operation,
        note: fields?.get('note')?.toString() ?? null,
        exceptionUntilUtc: operation === 'exception' && date ? new Date(date).toISOString() : null,
        reviewOnUtc: operation === 'claim' && date ? new Date(date).toISOString() : null,
      }) });
      await loadWorklist();
    } catch (failure) {
      if (failure instanceof ApiError && failure.status === 409) { await loadWorklist(); setError(ui('Conflict')); }
      else setError(ui('SaveError'));
    } finally { setBusy(false); }
  }
  async function searchReference() {
    setBusy(true); setError('');
    try { setHits(await apiFetchJson<SearchHit[]>(`${endpoint}Search`, undefined, { query, setup })); }
    catch { setHits([]); setError(errorLabel); }
    finally { setBusy(false); }
  }
  async function savePreferences(form: HTMLFormElement) {
    if (!followup) return;
    setBusy(true); setError(''); const fields = new FormData(form);
    try {
      await apiFetchJson(`${endpoint}Preferences`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
        expectedRevision: followup.preferences.revision, digestEnabled: fields.get('digest') === 'on', quietStartHour: Number(fields.get('start')), quietEndHour: Number(fields.get('end')),
      }) });
      await loadWorklist();
    } catch (failure) { setError(failure instanceof ApiError && failure.status === 409 ? ui('Conflict') : ui('SaveError')); }
    finally { setBusy(false); }
  }

  if (!catalog || !overview) return <section className="rgaa" aria-busy={busy}>
    <p role={error ? 'alert' : 'status'}>{error || loadingLabel}</p>
    {error && <button type="button" onClick={() => void reload()}>{catalog ? ui('Retry') : errorLabel}</button>}
  </section>;
  const { workspace, report } = overview;
  const operatingPacks = overview.report.snapshot.evidence?.operatingPackIds;
  const selectedPackIds = operatingPacks?.state === 'Known' ? (operatingPacks.code ?? '').split(',').filter(Boolean) : [];
  const suggestedAreas = new Set(catalog.packs.filter(pack => selectedPackIds.includes(pack.id)).flatMap(pack => pack.areaIds));
  const tabs = setup ? ['wizard', 'report', 'explore'] : ['overview', ...(catalog.canSetup ? ['wizard'] : []), 'report', 'explore', 'health', 'worklist', 'reference', 'history'];
  const selected = (finding: Finding) => finding.areaId === 'security' || finding.scopeIndependent || report.selectedAreas.includes(finding.areaId);
  const next = report.findings.filter(f => selected(f) && f.result === 'Fail').sort((a, b) => Number(b.severity === 'Critical') - Number(a.severity === 'Critical')).slice(0, 3);
  const filtered = catalog.capabilities.filter(c => (!addonsOnly || c.id.startsWith('addon-')) && `${t(c.labelKey)} ${t(c.purposeKey)} ${t(catalog.areas.find(a => a.id === c.areaId)?.labelKey ?? "")}`.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  const interests = catalog.capabilities.filter(c => workspace.interestedCapabilityIds.includes(c.id));
  const addonNames = (capability: Capability) => capability.requirements.filter(r => r.kind === 'addon').map(r => {
    const addon = catalog.capabilities.find(c => c.id.startsWith('addon-') && c.requirements.some(a => a.kind === 'addon' && a.id === r.id));
    return addon ? t(addon.labelKey) : ui('Unknown');
  });
  const selectedWorkflows = <><h3>{ui('SelectedWorkflows')}</h3><p>{ui('WorkflowProgressHelp')}</p>
    {interests.length === 0 && <p>{ui('SelectWorkflowHelp')}</p>}
    <div className="rgaa-grid">{interests.map(capability => {
      const availability = overview.access.find(a => a.capabilityId === capability.id);
      return <article className="rgaa-card" key={capability.id}>
        <h4>{t(capability.labelKey)}</h4>
        <p>{workspace.learnedCapabilityIds.includes(capability.id) ? ui('LearningComplete') : ui('LearningPending')}</p>
        <p><strong>{availability?.state === 'Known' ? ui('AvailableForSetup') : availability?.state === 'Unavailable' ? ui('WorkflowBlocked') : ui('WorkflowAccessUnknown')}</strong></p>
        {availability?.reasonCodes.map(reason => <p key={reason}>{ui(reason)}</p>)}
        {addonNames(capability).length > 0 && <p>{ui('RequiredAddons')}: {addonNames(capability).join(', ')}</p>}
        {(() => { const progress = overview.capabilitySetup?.find(s => s.capabilityId === capability.id); return <>
          <p>{ui(`SetupState.${progress?.state ?? 'NotAssessed'}`)}</p>
          <p>{t(progress?.guidanceKey ?? 'Ui.ConfigurationNotAssessed')}</p>
        </>; })()}
        <p>{t(capability.adoptionKey)}</p>
        {availability?.canConfigure && localLink(availability.destination) && <p><a href={localLink(availability.destination)}>{ui('Configure')}</a></p>}
        {availability?.subscriptionDestination && localLink(availability.subscriptionDestination) && <p><a href={localLink(availability.subscriptionDestination)}>{ui('SubscriptionOptions')}</a></p>}
        <button type="button" onClick={() => { setQuery(t(capability.labelKey)); setAddonsOnly(false); setTab('explore'); }}>{ui('LearnMore')}</button>
      </article>;
    })}</div>
  </>;
  const findingCard = (finding: Finding) => <article className="rgaa-card" key={finding.ruleId}>
    <p className={`rgaa-state rgaa-state-${finding.result}`}><strong>{ui(finding.result)}</strong> · {ui(finding.severity)}</p>
    <h3>{t(finding.titleKey)}</h3><p>{t(finding.explanationKey)}</p>
    <a href={localLink(finding.destination)}>{t(finding.nextActionKey)}</a>
  </article>;
  const summary = <>
    <p>{ui('ReportBoundary')}</p>
    <div className="rgaa-metrics">
      <p><strong>{report.verified} / {report.required}</strong>{ui('Checks')}</p>
      <p><strong>{report.failed}</strong>{ui('Failures')}</p><p><strong>{report.unknown}</strong>{ui('UnknownChecks')}</p>
      <p><strong>{workspace.learnedCapabilityIds.length} / {catalog.capabilities.length}</strong>{ui('Learning')}</p>
    </div>
    {report.hasCriticalUncertainty && <p className="rgaa-warning" role="status">{ui('CriticalUnknown')}</p>}
    {!report.snapshot.consistent && <p role="alert">{ui('Inconsistent')}</p>}
    {(report.uncheckedAreaIds?.length ?? 0) > 0 && <p className="rgaa-warning" role="status">{ui('UncheckedAreas')}: {report.uncheckedAreaIds.map(id => t(catalog.areas.find(area => area.id === id)?.labelKey ?? id)).join(', ')}.</p>}
    <p>{ui('VerificationCoverage')}</p>
    <p>{ui('Freshness')}: <time dateTime={report.snapshot.asOfUtc}>{new Date(report.snapshot.asOfUtc).toLocaleString()}</time></p>
    <p>{ui('Optional')}</p>
  </>;
  return <section className="rgaa" dir={catalog.rightToLeft ? 'rtl' : 'ltr'} aria-busy={busy}>
    <nav aria-label={ui('Title')} className="rgaa-nav">{tabs.map(id => <button key={id} type="button" disabled={busy} aria-current={tab === id ? 'page' : undefined} onClick={() => { setTab(id); if (id === 'history') void loadHistory(true); if (id === 'worklist') void loadWorklist(); }}>{ui(id)}</button>)}</nav>
    <div className="rgaa-toolbar"><h2>{ui(tab)}</h2><button type="button" disabled={busy} onClick={() => void verify()}>{ui('Verify')}</button></div>
    {busy && <p role="status">{ui('Loading')}</p>}{error && <p role="alert" className="rgaa-warning">{error}</p>}
    {(tab === 'overview' || tab === 'report') && <>{summary}<h3>{ui('Next')}</h3><div className="rgaa-grid">{next.map(findingCard)}{next.length === 0 && <p>{ui('None')}</p>}</div>
      <p>{ui('Reviewed')}: {workspace.reviewedOnUtc ? new Date(workspace.reviewedOnUtc).toLocaleString() : ui('NoReview')}</p>
      {workspace.reviewEvidence && <p>{ui('ReviewEvidence')}: {workspace.reviewEvidence.verified} / {workspace.reviewEvidence.required} · {ui('Failures')}: {workspace.reviewEvidence.failed} · {ui('UnknownChecks')}: {workspace.reviewEvidence.unknown} · {new Date(workspace.reviewEvidence.asOfUtc).toLocaleString()}</p>}
      {workspace.reviewEvidence && (workspace.reviewEvidence.catalogVersion !== catalog.version || workspace.reviewEvidence.snapshotRevision !== report.snapshot.revision || (workspace.reviewEvidence.scopeRevision ?? 0) !== (workspace.scopeRevision ?? 0)) && <p role="status">{ui('ReviewChanged')}</p>}
      {catalog.canSetup && <button type="button" disabled={busy || !report.snapshot.consistent} onClick={() => void save('review')}>{ui('MarkReviewed')}</button>}
      {catalog.canSetup && <form key={`revisit-${workspace.revision}`} onSubmit={event => { event.preventDefault(); const value = new FormData(event.currentTarget).get('revisit')?.toString(); void save('revisit', null, null, null, value ? `${value}T12:00:00.000Z` : null); }}>
        <p>{ui('RevisitHelp')}</p><label>{ui('RevisitDate')} <input type="date" name="revisit" min={new Date(Date.now() + 86400000).toISOString().slice(0,10)} max={new Date(Date.now() + 364 * 86400000).toISOString().slice(0,10)} defaultValue={workspace.revisitOnUtc?.slice(0,10) ?? ''} /></label><button type="submit" disabled={busy}>{ui('SaveRevisit')}</button>
      </form>}
      {!setup && <CapacityPreview key={report.snapshot.asOfUtc} revision={report.snapshot.revision} t={t} />}
      {!setup && <SecurityPreview key={`security-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />}
      {!setup && <NotificationPreview key={`notification-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />}
      {!setup && <RetentionPreview key={`retention-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />}
      {!setup && <TextImportPreview key={`text-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />}
      {!setup && <ModulePreview key={`module-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} types={catalog.moduleImpactTypes ?? []} t={t} />}
      {!setup && <PermissionPreview key={`permission-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} types={catalog.permissionImpactTypes ?? []} t={t} />}
      {!setup && <DispatchPreview key={`dispatch-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />}
      {tab === 'report' && <p><a href={`/User/AdminAssist/PrintReport?setup=${setup ? 'true' : 'false'}`} target="_blank" rel="noopener noreferrer">{ui('PrintableReport')}</a></p>}
      {selectedWorkflows}
      {tab === 'report' && <details><summary>{ui('CapabilitySetupTitle')}</summary><p>{ui('CapabilitySetupHelp')}</p><div className="rgaa-grid">{catalog.capabilities.filter(c => !c.id.startsWith('addon-')).map(capability => {
        const progress = overview.capabilitySetup?.find(s => s.capabilityId === capability.id);
        const availability = overview.access.find(a => a.capabilityId === capability.id);
        return <article className="rgaa-card" key={capability.id}><h4>{t(capability.labelKey)}</h4>
          <p>{t(progress?.opportunityKey ?? 'Ui.OpportunityUnknown')}</p><p><strong>{ui(`SetupState.${progress?.state ?? 'NotAssessed'}`)}</strong></p>
          <p>{t(progress?.guidanceKey ?? 'Ui.ConfigurationNotAssessed')}</p>
          {(progress?.ruleIds.length ?? 0) > 0 && <p>{ui('ConfigurationChecks')}: {progress!.ruleIds.map(id => t(report.findings.find(f => f.ruleId === id)?.titleKey ?? id)).join(', ')}</p>}
          {availability?.canConfigure && localLink(availability.destination) && <p><a href={localLink(availability.destination)}>{ui('Configure')}</a></p>}
          {availability?.subscriptionDestination && localLink(availability.subscriptionDestination) && <p><a href={localLink(availability.subscriptionDestination)}>{ui('SubscriptionOptions')}</a></p>}
          <button type="button" onClick={() => { setQuery(t(capability.labelKey)); setAddonsOnly(false); setTab('explore'); }}>{ui('LearnMore')}</button>
        </article>;
      })}</div></details>}
      {tab === 'report' && <div className="rgaa-grid">{report.findings.filter(f => selected(f) && f.result !== 'NotApplicable').map(findingCard)}</div>}
    </>}
    {tab === 'wizard' && <>
      <p>{ui('Welcome')}</p><p>{ui('NoAutomaticActions')}</p>
      <label>{ui('Mode')} <select value={workspace.mode} disabled={busy || !catalog.canSetup} onChange={e => void save('mode', null, e.target.value)}>{['Fresh', 'Review', 'Import'].map(mode => <option key={mode} value={mode}>{ui(mode)}</option>)}</select></label>
      {workspace.mode === 'Import' && <p>{ui('ImportHelp')}</p>}
      <SetupJourney t={t} capabilities={catalog.capabilities} access={overview.access} localLink={localLink} learned={workspace.learnedCapabilityIds.length} total={catalog.capabilities.length}
        show={page => { setTab(page); if (page === 'explore') { setQuery(''); setAddonsOnly(false); } }} showAddons={() => { setQuery(''); setAddonsOnly(true); setTab('explore'); }} />
      <h3>{ui('OperatingProfile')}</h3>{operatingPacks?.state !== 'Known' && <p>{ui('ProfileUnavailable')}</p>}<p>{ui('ProfileHelp')}</p><a href={localLink("/User/Department/OperatingProfile")}>{ui('OperatingProfile')}</a>
      <details><summary>{ui('Packs')}</summary><div className="rgaa-grid">{catalog.packs.map(pack => <article key={pack.id}><h4>{t(pack.labelKey)}</h4>{selectedPackIds.includes(pack.id) && <p><strong>{ui('SelectedPack')}</strong></p>}<p>{t(pack.purposeKey)}</p>{pack.prerequisiteKeys.map(key => <p key={key}>{t(key)}</p>)}</article>)}</div></details>
      <div className="rgaa-grid">{catalog.areas.map(area => <article className="rgaa-card" key={area.id}><h3>{t(area.labelKey)}</h3>{suggestedAreas.has(area.id) && <p><strong>{ui('SuggestedArea')}</strong></p>}<p>{t(area.purposeKey)}</p>
        <AreaSetupChoice key={`${area.id}:${workspace.revision}`} areaId={area.id} label={t(area.labelKey)} choice={workspace.areas[area.id] ?? 'LearnLater'} reason={workspace.areaReasons?.[area.id]} disabled={busy || !catalog.canSetup} t={t} save={(choice, reason) => void save('area', area.id, choice, reason)} />
        <button type="button" onClick={() => { setQuery(t(area.labelKey)); setAddonsOnly(false); setTab('explore'); }}>{ui('explore')}</button>
      </article>)}</div>
    </>}
    {tab === 'explore' && <>
      <p>{ui('NoAutomaticActions')}</p><label><input type="checkbox" checked={addonsOnly} onChange={e => setAddonsOnly(e.target.checked)} /> {ui('AddonsOnly')}</label><label>{ui('Search')} <input type="search" value={query} onChange={e => setQuery(e.target.value)} /></label>
      <div className="rgaa-grid">{filtered.map(capability => {
        const access = overview.access.find(a => a.capabilityId === capability.id);
        return <article className="rgaa-card" key={capability.id}>
          <h3>{t(capability.labelKey)}</h3><p>{t(capability.purposeKey)}</p>
          <h4>{ui('Value')}</h4><p>{t(capability.valueKey)}</p><h4>{ui('Example')}</h4><p>{t(capability.exampleKey)}</p><h4>{ui('Adoption')}</h4><p>{t(capability.adoptionKey)}</p>
          {addonNames(capability).length > 0 && <p>{ui('RequiredAddons')}: {addonNames(capability).join(', ')}</p>}
          {capability.id.startsWith('addon-') && <details><summary>{ui('AddonFeatures')}</summary><ul>{catalog.capabilities.filter(child => !child.id.startsWith('addon-') && child.requirements.some(r => r.kind === 'addon' && capability.requirements.some(a => a.kind === 'addon' && a.id === r.id))).map(child => <li key={child.id}><button type="button" onClick={() => { setQuery(t(child.labelKey)); setAddonsOnly(false); }}>{t(child.labelKey)}</button></li>)}</ul></details>}
          <p>{ui('AvailableReasons')}: {ui(access?.state ?? 'Unknown')}</p>{access?.reasonCodes.map(reason => <p key={reason}>{ui(reason)}</p>)}
          {access?.canConfigure && localLink(access.destination) && <p><a href={localLink(access.destination)}>{ui('Configure')}</a></p>}
          {access?.subscriptionDestination && localLink(access.subscriptionDestination) && <p><a href={localLink(access.subscriptionDestination)}>{ui('SubscriptionOptions')}</a></p>}
          {catalog.canSetup && <><label><input type="checkbox" disabled={busy} checked={workspace.learnedCapabilityIds.includes(capability.id)} onChange={e => void save('learn', capability.id, String(e.target.checked))} /> {ui('Learned')}</label><label><input type="checkbox" disabled={busy} checked={workspace.interestedCapabilityIds.includes(capability.id)} onChange={e => void save('interest', capability.id, String(e.target.checked))} /> {ui('Interested')}</label></>}
        </article>;
      })}</div>{filtered.length === 0 && <p>{ui('NoResults')}</p>}
    </>}
    {tab === 'health' && <>{summary}<div className="rgaa-grid">{report.findings.map(findingCard)}</div></>}
    {tab === 'worklist' && <>
      <p>{ui('WorklistHelp')}</p><label>{ui('Filter')} <select value={workFilter} onChange={e => setWorkFilter(e.target.value)}><option value="active">{ui('ActiveFindings')}</option><option value="all">{ui('AllFindings')}</option><option value="exceptions">{ui('AcceptedException')}</option><option value="unknown">{ui('Unknown')}</option></select></label>
      {followup && <details><summary>{ui('Preferences')}</summary><p>{followup.worker.lastEvaluatedOn ? `${ui('WorkerLastRun')}: ${new Date(followup.worker.lastEvaluatedOn).toLocaleString()}` : ui('WorkerNever')}</p><p>{ui('DigestHelp')}</p>{!followup.digestsAvailable && <p>{ui('DigestUnavailable')}</p>}
        <form key={followup.preferences.revision} onSubmit={e => { e.preventDefault(); void savePreferences(e.currentTarget); }}>
          <label><input name="digest" type="checkbox" defaultChecked={followup.preferences.digestEnabled} /> {ui('DigestOptIn')}</label>
          <label>{ui('QuietStart')} <input name="start" type="number" min={0} max={23} required defaultValue={followup.preferences.quietStartHour} /></label>
          <label>{ui('QuietEnd')} <input name="end" type="number" min={0} max={23} required defaultValue={followup.preferences.quietEndHour} /></label>
          <button type="submit" disabled={busy}>{ui('SavePreferences')}</button>
        </form>{followup.preferences.lastAttemptOutcome && <p>{ui('DigestOutcome')}: {followup.preferences.lastAttemptOutcome}</p>}
      </details>}
      <div className="rgaa-grid">{worklist.filter(item => workFilter === 'all' || (workFilter === 'exceptions' ? item.reviewStatus === 3 : workFilter === 'unknown' ? item.result === 2 : item.result === 1)).map(item => {
        const finding = report.findings.find(f => f.ruleId === item.ruleId);
        return <article className="rgaa-card" key={item.adminAssistFindingId}>
          <h3>{finding ? t(finding.titleKey) : item.ruleId}</h3><p>{ui(['Pass', 'Fail', 'Unknown', 'NotApplicable'][item.result])} · {ui(['Unassigned', 'Assigned', 'InReview', 'AcceptedException', 'Resolved'][item.reviewStatus])}</p>
          <p>{ui('Freshness')}: {new Date(item.lastObservedOn).toLocaleString()}</p>
          {item.reviewOn && <p>{ui('ReviewDate')}: {new Date(item.reviewOn).toLocaleString()}</p>}{item.exceptionUntil && <p>{ui('ExceptionExpiry')}: {new Date(item.exceptionUntil).toLocaleString()}</p>}
          {item.content && <p className="rgaa-note">{item.content}</p>}
          {finding && <p><a href={localLink(finding.destination)}>{t(finding.nextActionKey)}</a></p>}
          {item.reviewStatus !== 4 && (item.result === 1 || item.result === 2) && <><button type="button" disabled={busy} onClick={() => void review(item, 'review')}>{ui('StartReview')}</button>
          <form onSubmit={e => { e.preventDefault(); void review(item, 'claim', e.currentTarget); }}><label>{ui('ReviewDate')} <input type="datetime-local" name="date" /></label><button type="submit" disabled={busy}>{ui('AssignMe')}</button></form></>}
          {item.result === 1 && item.reviewStatus !== 4 && <details><summary>{ui('AcceptException')}</summary><form onSubmit={e => { e.preventDefault(); void review(item, 'exception', e.currentTarget); }}>
            <label>{ui('Reason')} <textarea name="note" required maxLength={2000} /></label><label>{ui('ExceptionExpiry')} <input type="datetime-local" name="date" required /></label><p>{ui('ExceptionHelp')}</p><button type="submit" disabled={busy}>{ui('AcceptException')}</button>
          </form></details>}
        </article>;
      })}</div>{worklist.length === 0 && <p>{ui('VerifyToPopulate')}</p>}
    </>}
    {tab === 'reference' && <><form onSubmit={e => { e.preventDefault(); void searchReference(); }}><label>{ui('Search')} <input type="search" value={query} maxLength={256} onChange={e => setQuery(e.target.value)} /></label><button type="submit" disabled={busy}>{ui('SearchReference')}</button></form>
      {hits.map(hit => <article key={hit.id} className="rgaa-card"><h3>{t(hit.titleKey)}</h3><p lang={hit.locale}>{hit.excerpt}</p><small>{hit.sourcePath}#{hit.anchor} · {hit.packVersion} · {hit.locale}</small>{catalog.articles.filter(a => a.id === hit.id && a.locale === hit.locale && a.packVersion === hit.packVersion).map(article => <details key={article.id}><summary>{ui('ReadSource')}</summary><div className="rgaa-source" lang={article.locale}>{article.body}</div></details>)}</article>)}
      <div className="rgaa-grid">{catalog.settings.filter(s => `${t(s.labelKey)} ${t(s.helpKey)}`.toLocaleLowerCase().includes(query.toLocaleLowerCase())).map(setting => <article className="rgaa-card" key={setting.id}><h3>{t(setting.labelKey)}</h3><p>{t(setting.helpKey)}</p><p>{t(setting.impact.timingKey)}</p><p>{t(setting.impact.reversibilityKey)}</p><a href={localLink(setting.location.url)}>{ui('Configure')}</a>{!setup && catalog.impactSettings.includes(setting.id) && <ImpactPreview key={`${setting.id}:${report.snapshot.asOfUtc}`} settingId={setting.id} valueType={setting.valueType} revision={report.snapshot.revision} t={t} />}</article>)}</div></>}
    {tab === 'history' && <><p>{ui('HistoryBoundary')}</p><table><thead><tr><th>{ui('HistoryTime')}</th><th>{ui('HistoryAction')}</th><th>{ui('HistorySubject')}</th><th>{ui('Before')}</th><th>{ui('After')}</th></tr></thead><tbody>{history.map(row => <tr key={row.id}><td>{new Date(row.occurredOnUtc).toLocaleString()}</td><td>{row.action}</td><td>{row.subjectId}</td><td><code>{row.beforeCode}</code></td><td><code>{row.afterCode}</code></td></tr>)}</tbody></table>{hasMore && <button type="button" disabled={busy} onClick={() => void loadHistory()}>{ui('More')}</button>}</>}
  </section>;
}
