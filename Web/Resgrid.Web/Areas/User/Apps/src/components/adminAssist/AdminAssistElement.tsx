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
import SetupWizard from './SetupWizard';
import AreaSetupChoice from './AreaSetupChoice';
import { ModuleCard, ModuleReadiness, isKey, tiers, type Module } from './ModuleViews';
import { Chip, FindingGroups, FindingRow, Ibox, MetricRow, ResultChip, SeverityChip, byPriority, format, isSuggestion, type Finding } from './SetupVisuals';
import AskPanel from './AskPanel';
import PlansPanel, { type PlanSource } from './PlansPanel';
import TroubleshootPanel from './TroubleshootPanel';
import SetupChecklist, { type SetupPlan } from './SetupChecklist';

export interface AdminAssistElementProps {
  page: string;
  setup: boolean;
  loadingLabel: string;
  errorLabel: string;
  retryLabel?: string;
}
type AreaChoice = 'UseNow' | 'LearnLater' | 'NotApplicable';
type Workspace = { scopeRevision?: number; revision: number; mode: string; areas: Record<string, AreaChoice>; areaReasons?: Record<string, string>; learnedCapabilityIds: string[]; interestedCapabilityIds: string[]; reviewedOnUtc: string | null; revisitOnUtc?: string | null;
  reviewEvidence?: { scopeRevision?: number; catalogVersion: string; snapshotRevision: string; asOfUtc: string; required: number; verified: number; failed: number; unknown: number } | null };
type Location = { url: string; field: string | null };
type Capability = { id: string; areaId: string; prominence?: string; labelKey: string; purposeKey: string; valueKey: string; exampleKey: string; adoptionKey: string; releaseStatus: string; requirements: { kind: string; id: string }[] };
type Catalog = {
  plansAvailable?: boolean; askAvailable?: boolean; troubleshootingAvailable?: boolean;
  moduleImpactTypes: string[]; permissionImpactTypes: string[]; impactSettings: string[]; version: string; strings: Record<string, string>; rightToLeft: boolean; canSetup: boolean;
  areas: Module[];
  capabilities: Capability[];
  settings: { id: string; valueType: string; labelKey: string; helpKey: string; location: Location; impact: { risk: string; timingKey: string; reversibilityKey: string } }[];
  articles: { id: string; locale: string; body: string; sourcePath: string; anchor: string; packVersion: string }[];
  packs: { id: string; labelKey: string; purposeKey: string; areaIds: string[]; prerequisiteKeys: string[] }[];
};
type Overview = {
  setupPlan?: SetupPlan;
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

export default function AdminAssistElement({ page, setup, loadingLabel, errorLabel, retryLabel = 'Retry' }: AdminAssistElementProps) {
  const [tab, setTab] = useState(page);
  const [planSource, setPlanSource] = useState<PlanSource | null>(null);
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [overview, setOverview] = useState<Overview | null>(null);
  const localLink = (url: string | null | undefined) => {
    const destination = safeLocalLink(url);
    return !destination || !catalog?.canSetup ? destination : `${destination}${destination.includes('?') ? '&' : '?'}aaReturn=${tab === 'wizard' ? 'wizard' : 'report'}`;
  };
  const [query, setQuery] = useState('');
  const [addonsOnly, setAddonsOnly] = useState(false);
  const [healthArea, setHealthArea] = useState('');
  const [healthResult, setHealthResult] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [history, setHistory] = useState<History[]>([]);
  const [hasMore, setHasMore] = useState(true);
  const [worklist, setWorklist] = useState<WorkItem[]>([]);
  const [hits, setHits] = useState<SearchHit[]>([]);
  const [workFilter, setWorkFilter] = useState('active');
  const [followup, setFollowup] = useState<Followup | null>(null);
  const request = useRef<AbortController | null>(null);
  const protectionRevision = useRef(0);
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
  useEffect(() => {
    const refreshProtection = () => {
      protectionRevision.current++;
      setOverview(null); setWorklist([]); setHistory([]); setFollowup(null);
      void reload();
    };
    window.addEventListener('resgrid:adp-reveal-changed', refreshProtection);
    return () => window.removeEventListener('resgrid:adp-reveal-changed', refreshProtection);
  }, [reload]);
  useEffect(() => { if (catalog && !catalog.plansAvailable && tab === 'plans') setTab('overview'); }, [catalog, tab]);
  useEffect(() => { if (catalog && !catalog.troubleshootingAvailable && tab === 'troubleshoot') setTab('overview'); }, [catalog, tab]);
  useEffect(() => { if (catalog && !catalog.askAvailable && tab === 'ask') setTab('overview'); }, [catalog, tab]);

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
    const revision = protectionRevision.current;
    setBusy(true); setError('');
    try {
      const [items, preferences] = await Promise.all([apiFetchJson<WorkItem[]>(`${endpoint}Worklist`), apiFetchJson<Followup>(`${endpoint}Preferences`)]);
      if (revision !== protectionRevision.current) return;
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
    {error ? <div className="alert alert-danger" role="alert">{error}</div>
      : <p className="rgaa-muted" role="status"><i className="fa fa-spinner fa-spin" aria-hidden="true" /> {loadingLabel}</p>}
    {error && <button type="button" className="btn btn-white btn-sm" onClick={() => void reload()}><i className="fa fa-refresh" aria-hidden="true" /> {catalog ? ui('Retry') : retryLabel}</button>}
  </section>;
  const { workspace, report } = overview;
  const areas = [...catalog.areas].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
  const areaLabel = (id: string) => t(areas.find(area => area.id === id)?.labelKey ?? id);
  const operatingPacks = overview.report.snapshot.evidence?.operatingPackIds;
  const selectedPackIds = operatingPacks?.state === 'Known' ? (operatingPacks.code ?? '').split(',').filter(Boolean) : [];
  const suggestedAreas = new Set(catalog.packs.filter(pack => selectedPackIds.includes(pack.id)).flatMap(pack => pack.areaIds));
  const tabs = setup ? ['wizard', 'report', 'explore'] : ['overview', ...(catalog.canSetup ? ['wizard'] : []), 'report', 'explore', 'health', 'worklist', 'reference', 'history', ...(catalog.plansAvailable ? ['plans'] : []), ...(catalog.troubleshootingAvailable ? ['troubleshoot'] : []), ...(catalog.askAvailable ? ['ask'] : [])];
  const selected = (finding: Finding) => finding.areaId === 'security' || finding.scopeIndependent || report.selectedAreas.includes(finding.areaId);
  const scoped = report.findings.filter(selected);
  const next = scoped.filter(f => f.result === 'Fail' && !isSuggestion(f)).sort(byPriority).slice(0, 5);
  const criticalFailures = scoped.filter(f => f.result === 'Fail' && f.severity === 'Critical').length;
  const reportCounts = { ok: report.verified, fail: report.failed, unknown: report.unknown, total: report.required };
  const lower = (keys: string[]) => keys.map(k => t(k)).join(' ').toLocaleLowerCase();
  const needle = query.trim().toLocaleLowerCase();
  // A module matches its own text or any of its features, including detail features that are not listed on the card.
  const exploreModules = areas.filter(m => (!addonsOnly || m.tier === 'AddOn') && (!needle || lower([m.labelKey, m.purposeKey, m.valueKey]).includes(needle) ||
    catalog.capabilities.some(c => c.areaId === m.id && lower([c.labelKey, c.purposeKey]).includes(needle))));
  const reviewState: 'current' | 'changed' | 'none' = !workspace.reviewEvidence ? 'none'
    : workspace.reviewEvidence.catalogVersion !== catalog.version || workspace.reviewEvidence.snapshotRevision !== report.snapshot.revision || (workspace.reviewEvidence.scopeRevision ?? 0) !== (workspace.scopeRevision ?? 0) ? 'changed' : 'current';
  const showTab = (id: string) => { setTab(id); if (id === 'history') void loadHistory(true); if (id === 'worklist') void loadWorklist(); };
  const explore = (search = '', addons = false) => { setQuery(search); setAddonsOnly(addons); setTab('explore'); };
  const planFrom = catalog.plansAvailable ? (finding: Finding) => { setPlanSource({ source: 'finding', sourceId: finding.ruleId, goal: t(finding.titleKey) }); setTab('plans'); } : undefined;
  const link = (url: string) => localLink(url);

  // Setup teaches key features only; detail features (and older learned ids) are left to Explore and Ask.
  const keyFeatureIds = new Set(catalog.capabilities.filter(isKey).map(c => c.id));
  const summary = <>
    <MetricRow counts={reportCounts} critical={criticalFailures} learned={workspace.learnedCapabilityIds.filter(id => keyFeatureIds.has(id)).length} totalCapabilities={keyFeatureIds.size} ui={ui} />
    {report.hasCriticalUncertainty && <div className="alert alert-warning" role="status"><i className="fa fa-exclamation-triangle" aria-hidden="true" /> {ui('CriticalUnknown')}</div>}
    {!report.snapshot.consistent && <div className="alert alert-danger" role="alert"><i className="fa fa-refresh" aria-hidden="true" /> {ui('Inconsistent')}</div>}
    {(report.uncheckedAreaIds?.length ?? 0) > 0 && <div className="alert alert-info" role="status"><i className="fa fa-info-circle" aria-hidden="true" /> {ui('UncheckedAreas')}: {report.uncheckedAreaIds.map(areaLabel).join(', ')}.</div>}
    <p className="rgaa-small rgaa-muted"><i className="fa fa-clock-o" aria-hidden="true" /> {ui('Freshness')}: <time dateTime={report.snapshot.asOfUtc}>{new Date(report.snapshot.asOfUtc).toLocaleString()}</time>. {ui('ReportBoundary')} {ui('VerificationCoverage')}</p>
  </>;
  const nextActions = <Ibox title={ui('Next')} tools={next.length > 0 && <button type="button" className="btn btn-white btn-xs" onClick={() => { setHealthArea(''); setHealthResult('Fail'); setTab(setup ? 'report' : 'health'); }}>{ui('ViewAll')}</button>}>
    {next.length > 0 ? <ul className="rgaa-findings">{next.map(f => <FindingRow key={f.ruleId} finding={f} t={t} ui={ui} areaLabel={areaLabel(f.areaId)} link={link} onPlan={planFrom} />)}</ul>
      : <p className="rgaa-muted"><i className="fa fa-check-circle rgaa-icon--ok" aria-hidden="true" /> {ui('NoNextActions')}</p>}
  </Ibox>;
  const areaPanel = <Ibox title={ui('AreaReadiness')}>
    <p className="rgaa-small rgaa-muted">{ui('AreaReadinessHelp')}</p>
    <ModuleReadiness modules={areas} features={catalog.capabilities} setup={overview.capabilitySetup ?? []} findings={report.findings} choices={workspace.areas}
      reasons={workspace.areaReasons} t={t} ui={ui} onShowModule={setup ? undefined : moduleId => { setHealthArea(moduleId); setHealthResult(''); setTab('health'); }} />
    {catalog.canSetup && <p><button type="button" className="btn btn-white btn-xs" onClick={() => setTab('wizard')}><i className="fa fa-sliders" aria-hidden="true" /> {ui('ChangeScope')}</button></p>}
  </Ibox>;
  const reviewPanel = <Ibox title={ui('SetupReview')}>
    <p><strong>{ui('Reviewed')}:</strong> {workspace.reviewedOnUtc ? new Date(workspace.reviewedOnUtc).toLocaleString() : ui('NoReview')}
      {' '}{reviewState === 'current' && <Chip tone="ok" icon="fa-check">{ui('StepReviewed')}</Chip>}{reviewState === 'changed' && <Chip tone="unknown" icon="fa-exclamation">{ui('StepReviewChanged')}</Chip>}</p>
    {workspace.reviewEvidence && <p className="rgaa-small rgaa-muted">{ui('ReviewEvidence')}: {workspace.reviewEvidence.verified} / {workspace.reviewEvidence.required} · {ui('Failures')}: {workspace.reviewEvidence.failed} · {ui('UnknownChecks')}: {workspace.reviewEvidence.unknown} · {new Date(workspace.reviewEvidence.asOfUtc).toLocaleString()}</p>}
    {reviewState === 'changed' && <p className="rgaa-warning rgaa-small" role="status">{ui('ReviewChanged')}</p>}
    {catalog.canSetup && <p><button type="button" className="btn btn-primary btn-sm" disabled={busy || !report.snapshot.consistent} onClick={() => void save('review')}><i className="fa fa-check-square-o" aria-hidden="true" /> {ui('MarkReviewed')}</button></p>}
    {catalog.canSetup && <form key={`revisit-${workspace.revision}`} onSubmit={event => { event.preventDefault(); const value = new FormData(event.currentTarget).get('revisit')?.toString(); void save('revisit', null, null, null, value ? `${value}T12:00:00.000Z` : null); }}>
      <p className="rgaa-small rgaa-muted">{ui('RevisitHelp')}</p>
      <div className="form-inline"><div className="form-group"><label htmlFor="rgaa-revisit" className="control-label">{ui('RevisitDate')}</label>{' '}
        <input id="rgaa-revisit" className="form-control input-sm" type="date" name="revisit" min={new Date(Date.now() + 86400000).toISOString().slice(0, 10)} max={new Date(Date.now() + 364 * 86400000).toISOString().slice(0, 10)} defaultValue={workspace.revisitOnUtc?.slice(0, 10) ?? ''} /></div>{' '}
        <button type="submit" className="btn btn-white btn-sm" disabled={busy}>{ui('SaveRevisit')}</button></div>
    </form>}
    <p className="rgaa-actions" style={{ marginTop: 12 }}>
      {catalog.canSetup && workspace.revisitOnUtc && <><a className="btn btn-white btn-xs" href="/User/AdminAssist/ReviewCalendar"><i className="fa fa-calendar" aria-hidden="true" /> {ui('DownloadReminder')}</a>{' '}</>}
      <a className="btn btn-white btn-xs" href={`/User/AdminAssist/PrintReport?setup=${setup ? 'true' : 'false'}`} target="_blank" rel="noopener noreferrer"><i className="fa fa-print" aria-hidden="true" /> {ui('PrintableReport')}</a>
    </p>
  </Ibox>;
  const previews = !setup && <Ibox title={ui('ImpactPreviews')}>
    <p className="rgaa-small rgaa-muted">{ui('ImpactPreviewsHelp')}</p>
    <div className="rgaa-grid">
      <CapacityPreview key={report.snapshot.asOfUtc} revision={report.snapshot.revision} t={t} />
      <SecurityPreview key={`security-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />
      <NotificationPreview key={`notification-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />
      <RetentionPreview key={`retention-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />
      <TextImportPreview key={`text-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />
      <ModulePreview key={`module-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} types={catalog.moduleImpactTypes ?? []} t={t} />
      <PermissionPreview key={`permission-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} types={catalog.permissionImpactTypes ?? []} t={t} />
      <DispatchPreview key={`dispatch-${report.snapshot.asOfUtc}`} revision={report.snapshot.revision} t={t} />
    </div>
  </Ibox>;
  const healthFindings = report.findings.filter(f => (!healthArea || f.areaId === healthArea) && (!healthResult || f.result === healthResult));

  return <section className="rgaa" dir={catalog.rightToLeft ? 'rtl' : 'ltr'} aria-busy={busy}>
    <nav aria-label={ui('Title')}><ul className="nav nav-tabs rgaa-tabs">{tabs.map(id => <li key={id} className={tab === id ? 'active' : undefined}>
      <a href={`#${id}`} role="button" aria-current={tab === id ? 'page' : undefined} aria-disabled={busy || undefined} onClick={event => { event.preventDefault(); if (!busy) showTab(id); }}>{ui(id)}</a>
    </li>)}</ul></nav>
    <h2 className="sr-only">{ui(tab)}</h2>
    <div className="rgaa-toolbar">
      {busy && <span className="rgaa-muted rgaa-small" role="status"><i className="fa fa-spinner fa-spin" aria-hidden="true" /> {ui('Loading')}</span>}
      <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void verify()}><i className="fa fa-refresh" aria-hidden="true" /> {ui('Verify')}</button>
    </div>
    {error && <div role="alert" className="alert alert-danger">{error}</div>}
    {tab === 'overview' && <>{summary}
      <div className="rgaa-columns"><div>{nextActions}{areaPanel}</div><div>{reviewPanel}
        {catalog.canSetup && <Ibox title={ui('wizard')}><p>{ui('SetupPrompt')}</p><button type="button" className="btn btn-primary btn-sm" onClick={() => setTab('wizard')}><i className="fa fa-magic" aria-hidden="true" /> {ui('ResumeSetup')}</button></Ibox>}
      </div></div>
      {previews}
    </>}
    {tab === 'report' && <>{summary}
      <div className="rgaa-columns"><div>{nextActions}{areaPanel}</div><div>{reviewPanel}</div></div>
      {overview.setupPlan && overview.setupPlan.tasks.length > 0 && <Ibox title={ui('SetupChecklist')}><SetupChecklist plan={overview.setupPlan} t={t} capabilities={catalog.capabilities} areas={areas} localLink={localLink} /></Ibox>}
      <Ibox title={ui('AllChecks')}><FindingGroups findings={scoped.filter(f => f.result !== 'NotApplicable')} t={t} ui={ui} areas={areas} link={link} onPlan={planFrom} /></Ibox>
    </>}
    {tab === 'wizard' && <SetupWizard t={t} mode={workspace.mode} canSetup={catalog.canSetup} busy={busy} modules={areas} features={catalog.capabilities} access={overview.access}
      capabilitySetup={overview.capabilitySetup ?? []} packs={catalog.packs} choices={workspace.areas} reasons={workspace.areaReasons} learned={workspace.learnedCapabilityIds}
      findings={report.findings} selectedAreas={report.selectedAreas} profileKnown={operatingPacks?.state === 'Known'}
      selectedPackIds={selectedPackIds} suggestedAreas={suggestedAreas} reviewed={reviewState} localLink={localLink}
      reportCounts={{ failed: report.failed, unknown: report.unknown, required: report.required, verified: report.verified, criticalUnknown: report.hasCriticalUncertainty }}
      save={(operation, targetId = null, choice = null, reasonCode = null) => void save(operation, targetId, choice, reasonCode)}
      show={(page, search, addons) => page === 'explore' ? explore(search ?? '', addons ?? false) : setTab(page)} reviewPanel={reviewPanel} summary={summary} />}
    {tab === 'explore' && <>
      <p className="rgaa-lead">{ui('ExploreHelp')}</p>
      <div className="rgaa-explore-controls">
        <div className="form-group"><label htmlFor="rgaa-explore-search">{ui('Search')}</label><input id="rgaa-explore-search" className="form-control input-sm" type="search" value={query} onChange={e => setQuery(e.target.value)} /></div>
        <label className="rgaa-check"><input type="checkbox" checked={addonsOnly} onChange={e => setAddonsOnly(e.target.checked)} />{ui('AddonsOnly')}</label>
      </div>
      <p className="rgaa-small rgaa-muted"><i className="fa fa-info-circle" aria-hidden="true" /> {ui('NoAutomaticActions')}</p>
      {tiers.map(tier => {
        const modules = exploreModules.filter(m => m.tier === tier);
        if (modules.length === 0) return null;
        return <section key={tier} aria-labelledby={`rgaa-tier-${tier}`}>
          <div className="rgaa-area-heading"><h3 id={`rgaa-tier-${tier}`}>{ui(`Tier.${tier}`)}</h3><span className="rgaa-muted rgaa-small">{ui(`TierHelp.${tier}`)}</span></div>
          <div className="rgaa-module-grid">{modules.map(module => <ModuleCard key={module.id} module={module} features={catalog.capabilities} t={t} ui={ui}
            access={overview.access} setup={overview.capabilitySetup ?? []} findings={report.findings} choice={module.id === 'security' ? 'UseNow' : workspace.areas[module.id]}
            learned={workspace.learnedCapabilityIds} canLearn={catalog.canSetup} busy={busy} onLearn={(id, value) => void save('learn', id, String(value))} link={localLink} showStatus
            scopeControl={catalog.canSetup ? <div className="rgaa-module__scope">
              {suggestedAreas.has(module.id) && <p><Chip tone="info" icon="fa-star">{ui('SuggestedArea')}</Chip></p>}
              <AreaSetupChoice key={`${module.id}:${workspace.areas[module.id] ?? ''}:${workspace.areaReasons?.[module.id] ?? ''}`} areaId={module.id} label={t(module.labelKey)}
                choice={module.id === 'security' ? 'UseNow' : workspace.areas[module.id]} reason={workspace.areaReasons?.[module.id]} disabled={busy} t={t}
                save={(choice, reason) => void save('area', module.id, choice, reason)} />
            </div> : undefined} />)}</div>
        </section>;
      })}
      {exploreModules.length === 0 && <p>{ui('NoResults')}</p>}
    </>}
    {tab === 'health' && <>{summary}
      <Ibox title={ui('health')} tools={<form className="form-inline" onSubmit={e => e.preventDefault()}>
        <label className="sr-only" htmlFor="rgaa-health-area">{ui('Area')}</label>
        <select id="rgaa-health-area" className="form-control input-sm" value={healthArea} onChange={e => setHealthArea(e.target.value)}><option value="">{ui('AllAreas')}</option>{areas.map(area => <option key={area.id} value={area.id}>{t(area.labelKey)}</option>)}</select>{' '}
        <label className="sr-only" htmlFor="rgaa-health-result">{ui('Filter')}</label>
        <select id="rgaa-health-result" className="form-control input-sm" value={healthResult} onChange={e => setHealthResult(e.target.value)}><option value="">{ui('AllResults')}</option>{['Fail', 'Unknown', 'Pass', 'NotApplicable'].map(result => <option key={result} value={result}>{ui(result)}</option>)}</select>
      </form>}>
        <FindingGroups findings={healthFindings} t={t} ui={ui} areas={areas} link={link} onPlan={planFrom} showArea={!healthArea} />
      </Ibox>
    </>}
    {tab === 'worklist' && <>
      <p className="rgaa-lead">{ui('WorklistHelp')}</p>
      <div className="rgaa-explore-controls"><div className="form-group"><label htmlFor="rgaa-work-filter">{ui('Filter')}</label>
        <select id="rgaa-work-filter" className="form-control input-sm" value={workFilter} onChange={e => setWorkFilter(e.target.value)}><option value="active">{ui('ActiveFindings')}</option><option value="all">{ui('AllFindings')}</option><option value="exceptions">{ui('AcceptedException')}</option><option value="unknown">{ui('Unknown')}</option></select></div></div>
      {followup && <Ibox title={ui('Preferences')}><details><summary className="rgaa-small">{followup.worker.lastEvaluatedOn ? `${ui('WorkerLastRun')}: ${new Date(followup.worker.lastEvaluatedOn).toLocaleString()}` : ui('WorkerNever')}</summary>
        <p className="rgaa-small">{ui('DigestHelp')}</p>{!followup.digestsAvailable && <div className="alert alert-warning">{ui('DigestUnavailable')}</div>}
        <form key={followup.preferences.revision} onSubmit={e => { e.preventDefault(); void savePreferences(e.currentTarget); }}>
          <label className="rgaa-check"><input name="digest" type="checkbox" defaultChecked={followup.preferences.digestEnabled} />{ui('DigestOptIn')}</label>
          <div className="form-inline"><div className="form-group"><label htmlFor="rgaa-quiet-start">{ui('QuietStart')}</label> <input id="rgaa-quiet-start" className="form-control input-sm" name="start" type="number" min={0} max={23} required defaultValue={followup.preferences.quietStartHour} /></div>{' '}
            <div className="form-group"><label htmlFor="rgaa-quiet-end">{ui('QuietEnd')}</label> <input id="rgaa-quiet-end" className="form-control input-sm" name="end" type="number" min={0} max={23} required defaultValue={followup.preferences.quietEndHour} /></div>{' '}
            <button type="submit" className="btn btn-white btn-sm" disabled={busy}>{ui('SavePreferences')}</button></div>
        </form>{followup.preferences.lastAttemptOutcome && <p className="rgaa-small">{ui('DigestOutcome')}: {followup.preferences.lastAttemptOutcome}</p>}
      </details></Ibox>}
      <div className="rgaa-grid">{worklist.filter(item => workFilter === 'all' || (workFilter === 'exceptions' ? item.reviewStatus === 3 : workFilter === 'unknown' ? item.result === 2 : item.result === 1)).map(item => {
        const finding = report.findings.find(f => f.ruleId === item.ruleId);
        const result = ['Pass', 'Fail', 'Unknown', 'NotApplicable'][item.result] ?? 'Unknown';
        return <article className="rgaa-card rgaa-feature" key={item.adminAssistFindingId}>
          <div className="rgaa-finding__meta"><ResultChip result={result} ui={ui} /><Chip tone={item.reviewStatus === 3 ? 'info' : item.reviewStatus === 4 ? 'ok' : 'muted'}>{ui(['Unassigned', 'Assigned', 'InReview', 'AcceptedException', 'Resolved'][item.reviewStatus])}</Chip>{finding && <SeverityChip severity={finding.severity} ui={ui} />}</div>
          <h4>{finding ? t(finding.titleKey) : item.ruleId}</h4>
          <p className="rgaa-small rgaa-muted">{ui('Freshness')}: {new Date(item.lastObservedOn).toLocaleString()}
            {item.reviewOn && <><br />{ui('ReviewDate')}: {new Date(item.reviewOn).toLocaleString()}</>}{item.exceptionUntil && <><br />{ui('ExceptionExpiry')}: {new Date(item.exceptionUntil).toLocaleString()}</>}</p>
          {item.content && <p className="rgaa-note">{item.content}</p>}
          {finding && <p>{localLink(finding.destination) && <a className="btn btn-white btn-xs" href={localLink(finding.destination)}><i className="fa fa-external-link" aria-hidden="true" /> {t(finding.nextActionKey)}</a>}
            {planFrom && <> <button type="button" className="btn btn-white btn-xs" onClick={() => planFrom(finding)}>{t('Plan.New')}</button></>}</p>}
          {item.reviewStatus !== 4 && (item.result === 1 || item.result === 2) && <div className="rgaa-feature__foot">
            <button type="button" className="btn btn-white btn-xs" disabled={busy} onClick={() => void review(item, 'review')}>{ui('StartReview')}</button>
            <form className="form-inline" onSubmit={e => { e.preventDefault(); void review(item, 'claim', e.currentTarget); }}><label className="sr-only" htmlFor={`rgaa-claim-${item.adminAssistFindingId}`}>{ui('ReviewDate')}</label>
              <input id={`rgaa-claim-${item.adminAssistFindingId}`} className="form-control input-sm" type="datetime-local" name="date" title={ui('ReviewDate')} /> <button type="submit" className="btn btn-white btn-xs" disabled={busy}>{ui('AssignMe')}</button></form>
          </div>}
          {item.result === 1 && item.reviewStatus !== 4 && <details className="rgaa-small"><summary>{ui('AcceptException')}</summary><form onSubmit={e => { e.preventDefault(); void review(item, 'exception', e.currentTarget); }}>
            <div className="form-group"><label htmlFor={`rgaa-note-${item.adminAssistFindingId}`}>{ui('Reason')}</label><textarea id={`rgaa-note-${item.adminAssistFindingId}`} className="form-control" name="note" required maxLength={2000} /></div>
            <div className="form-group"><label htmlFor={`rgaa-exp-${item.adminAssistFindingId}`}>{ui('ExceptionExpiry')}</label><input id={`rgaa-exp-${item.adminAssistFindingId}`} className="form-control input-sm" type="datetime-local" name="date" required /></div>
            <p className="help-block">{ui('ExceptionHelp')}</p><button type="submit" className="btn btn-warning btn-xs" disabled={busy}>{ui('AcceptException')}</button>
          </form></details>}
        </article>;
      })}</div>{worklist.length === 0 && <p className="rgaa-muted">{ui('VerifyToPopulate')}</p>}
    </>}
    {tab === 'reference' && <>
      <form className="rgaa-explore-controls" onSubmit={e => { e.preventDefault(); void searchReference(); }}>
        <div className="form-group"><label htmlFor="rgaa-reference-search">{ui('Search')}</label><input id="rgaa-reference-search" className="form-control input-sm" type="search" value={query} maxLength={256} onChange={e => setQuery(e.target.value)} /></div>
        <button type="submit" className="btn btn-primary btn-sm" disabled={busy}><i className="fa fa-search" aria-hidden="true" /> {ui('SearchReference')}</button>
      </form>
      {hits.length > 0 && <Ibox title={ui('ReferenceResults')}>{hits.map(hit => <article key={hit.id} className="rgaa-finding"><span className="rgaa-finding__icon rgaa-muted"><i className="fa fa-file-text-o" aria-hidden="true" /></span><div className="rgaa-finding__body">
        <p className="rgaa-finding__title">{t(hit.titleKey)}</p><p lang={hit.locale}>{hit.excerpt}</p><p className="rgaa-small rgaa-muted">{hit.sourcePath}#{hit.anchor} · {hit.packVersion} · {hit.locale}</p>
        {catalog.articles.filter(a => a.id === hit.id && a.locale === hit.locale && a.packVersion === hit.packVersion).map(article => <details key={article.id}><summary>{ui('ReadSource')}</summary><div className="rgaa-source" lang={article.locale}>{article.body}</div></details>)}
      </div></article>)}</Ibox>}
      <div className="rgaa-grid">{catalog.settings.filter(s => `${t(s.labelKey)} ${t(s.helpKey)}`.toLocaleLowerCase().includes(query.toLocaleLowerCase())).map(setting => <article className="rgaa-card rgaa-feature" key={setting.id}>
        <h4>{t(setting.labelKey)}</h4><p>{t(setting.helpKey)}</p><p className="rgaa-small rgaa-muted"><i className="fa fa-clock-o" aria-hidden="true" /> {t(setting.impact.timingKey)}<br /><i className="fa fa-undo" aria-hidden="true" /> {t(setting.impact.reversibilityKey)}</p>
        {!setup && catalog.impactSettings.includes(setting.id) && <ImpactPreview key={`${setting.id}:${report.snapshot.asOfUtc}`} settingId={setting.id} valueType={setting.valueType} revision={report.snapshot.revision} t={t} />}
        <div className="rgaa-feature__foot">{localLink(setting.location.url) && <a className="btn btn-white btn-xs" href={localLink(setting.location.url)}>{ui('Configure')}</a>}</div>
      </article>)}</div></>}
    {tab === 'plans' && catalog.plansAvailable && <PlansPanel askAvailable={catalog.askAvailable} t={t} settings={catalog.settings.filter(s => catalog.impactSettings.includes(s.id))} capabilities={catalog.capabilities} source={planSource} />}
    {tab === 'troubleshoot' && catalog.troubleshootingAvailable && <TroubleshootPanel t={t} localLink={localLink} capabilities={catalog.capabilities} />}
    {tab === 'ask' && catalog.askAvailable && <AskPanel onPlan={catalog.plansAvailable ? source => { setPlanSource(source); setTab('plans'); } : undefined} t={t} localLink={localLink} settings={catalog.settings.filter(s => catalog.impactSettings.includes(s.id))} />}
    {tab === 'history' && <Ibox title={ui('history')}><p className="rgaa-small rgaa-muted">{ui('HistoryBoundary')}</p><div className="table-responsive"><table className="table table-striped table-hover"><thead><tr><th>{ui('HistoryTime')}</th><th>{ui('HistoryAction')}</th><th>{ui('HistorySubject')}</th><th>{ui('Before')}</th><th>{ui('After')}</th></tr></thead>
      <tbody>{history.map(row => <tr key={row.id}><td>{new Date(row.occurredOnUtc).toLocaleString()}</td><td>{row.action}</td><td>{row.subjectId}</td><td><code>{row.beforeCode}</code></td><td><code>{row.afterCode}</code></td></tr>)}</tbody></table></div>
      {history.length === 0 && !busy && <p className="rgaa-muted">{ui('None')}</p>}
      {hasMore && history.length > 0 && <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void loadHistory()}>{ui('More')}</button>}</Ibox>}
  </section>;
}
