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
import { ModuleCard, ModuleReadiness, ModuleTile, isKey, tiers, type Module } from './ModuleViews';
import { Chip, FilterPills, FindingGroups, FindingRow, FindingSubjectsContext, Ibox, MetricRow, ResultChip, SeverityChip, StatusBar, byPriority, countFindings, countsLabel, format, isSuggestion, type Finding, type FindingSubject } from './SetupVisuals';
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
  settings: { id: string; areaId: string; valueType: string; labelKey: string; helpKey: string; location: Location; impact: { risk: string; timingKey: string; reversibilityKey: string } }[];
  articles: { id: string; locale: string; body: string; sourcePath: string; anchor: string; packVersion: string }[];
  packs: { id: string; labelKey: string; purposeKey: string; areaIds: string[]; prerequisiteKeys: string[] }[];
};
type Overview = {
  setupPlan?: SetupPlan;
  catalogVersion: string; workspace: Workspace;
  report: { verified: number; required: number; unknown: number; failed: number; hasCriticalUncertainty: boolean; selectedAreas: string[]; uncheckedAreaIds: string[]; findings: Finding[]; snapshot: { asOfUtc: string; consistent: boolean; revision: string; evidence: Record<string, { state: string; code: string | null }> } };
  access: { capabilityId: string; state: string; reasonCodes: string[]; canConfigure: boolean; subscriptionDestination: string | null; destination: string | null }[];
  capabilitySetup?: { capabilityId: string; state: string; opportunityKey: string; guidanceKey: string; ruleIds: string[] }[];
  findingSubjects?: Record<string, FindingSubject[]> | null;
};
type History = { id: string; occurredOnUtc: string; action: string; subjectId: string; beforeCode: string; afterCode: string };
type WorkItem = { adminAssistFindingId: string; ruleId: string; result: number; reviewStatus: number; ownerId: string | null; reviewOn: string | null; exceptionUntil: string | null; content: string | null; revision: number; lastObservedOn: string };
type SearchHit = { id: string; titleKey: string; excerpt: string; sourcePath: string; anchor: string; packVersion: string; locale: string };
type Followup = { digestsAvailable: boolean; preferences: { revision: number; digestEnabled: boolean; quietStartHour: number; quietEndHour: number; lastAttemptOutcome: string | null }; worker: { lastEvaluatedOn: string | null } };
const endpoint = 'api/v4/AdminAssist/';
// Admin Assist tabs: Setup Wizard, Setup Report and Explore, then Admin AI, which holds the workspace tools. The page runs in
// setup mode until Admin.Assist and Ai.AdminAssist are both on; Admin AI is shown but disabled until then. The setup summary
// lives only on Setup Report (with the impact previews once Admin AI is on); Admin AI holds the tools that need it.
const aiTabIds = ['ask', 'health', 'worklist', 'reference', 'history', 'plans', 'troubleshoot'];
const tabIcons: Record<string, string> = { wizard: 'fa-magic', report: 'fa-clipboard', explore: 'fa-compass', ai: 'fa-comments', ask: 'fa-comments-o',
  health: 'fa-heartbeat', worklist: 'fa-tasks', reference: 'fa-book', history: 'fa-history', plans: 'fa-list-ol', troubleshoot: 'fa-stethoscope' };
const tabsFor = (catalog: Catalog, setup: boolean) => ({
  primary: [...(catalog.canSetup ? ['wizard'] : []), 'report', 'explore'],
  ai: setup ? [] : aiTabIds.filter(id => id === 'ask' ? catalog.askAvailable : id === 'plans' ? catalog.plansAvailable : id === 'troubleshoot' ? catalog.troubleshootingAvailable : true),
});
type Tone = 'ok' | 'fail' | 'unknown' | 'info' | 'muted';
const resultIcons: Record<string, { icon: string; tone: Tone }> = {
  Fail: { icon: 'fa-times-circle', tone: 'fail' }, Unknown: { icon: 'fa-question-circle', tone: 'unknown' }, Pass: { icon: 'fa-check-circle', tone: 'ok' }, NotApplicable: { icon: 'fa-minus-circle', tone: 'muted' },
};
const healthResults = ['Fail', 'Unknown', 'Pass', 'NotApplicable'];
const referencePage = 50;
// Indexed by WorkItem.reviewStatus.
const reviewStatuses: { key: string; icon: string; tone: Tone }[] = [
  { key: 'Unassigned', icon: 'fa-circle-o', tone: 'muted' }, { key: 'Assigned', icon: 'fa-user', tone: 'info' }, { key: 'InReview', icon: 'fa-eye', tone: 'info' },
  { key: 'AcceptedException', icon: 'fa-shield', tone: 'info' }, { key: 'Resolved', icon: 'fa-check', tone: 'ok' },
];
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
  // Explore: a tier filter over the module tiles, and the module open in full ('' shows the tiles).
  const [exploreTier, setExploreTier] = useState('');
  const [exploreOpen, setExploreOpen] = useState('');
  const lastExplored = useRef('');
  const [healthArea, setHealthArea] = useState('');
  const [healthResult, setHealthResult] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [history, setHistory] = useState<History[]>([]);
  const [hasMore, setHasMore] = useState(true);
  const [worklist, setWorklist] = useState<WorkItem[]>([]);
  const [hits, setHits] = useState<SearchHit[]>([]);
  const [workFilter, setWorkFilter] = useState('active');
  const [referenceArea, setReferenceArea] = useState('');
  const [referenceLimit, setReferenceLimit] = useState(referencePage);
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
  // A tab that is not available here (the wizard without setup rights, an Admin AI tool that is off, or Admin AI itself before
  // it launches) opens the nearest one that is.
  useEffect(() => {
    if (!catalog) return;
    const { primary, ai } = tabsFor(catalog, setup);
    if (!primary.includes(tab) && !ai.includes(tab)) setTab(aiTabIds.includes(tab) && ai.length > 0 ? ai[0] : primary[0]);
  }, [catalog, setup, tab]);
  // Opening a module moves focus to its heading; returning to the tiles moves it back to the tile that was opened.
  useEffect(() => {
    const target = exploreOpen ? `rgaa-module-${exploreOpen}` : lastExplored.current ? `rgaa-tile-${lastExplored.current}` : '';
    if (target) document.getElementById(target)?.focus();
  }, [exploreOpen]);
  // The worklist and change history load when their tab opens, however it was opened (a tab click, a link or the page's first tab).
  const catalogReady = catalog !== null;
  useEffect(() => {
    if (!catalog || !tabsFor(catalog, setup).ai.includes(tab)) return;
    if (tab === 'history') void loadHistory(true);
    if (tab === 'worklist') void loadWorklist();
  }, [tab, catalogReady]);

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
  const { primary: tabs, ai: aiTabs } = tabsFor(catalog, setup);
  const inAi = aiTabs.includes(tab);
  const selected = (finding: Finding) => finding.areaId === 'security' || finding.scopeIndependent || report.selectedAreas.includes(finding.areaId);
  const scoped = report.findings.filter(selected);
  const next = scoped.filter(f => f.result === 'Fail' && !isSuggestion(f)).sort(byPriority).slice(0, 5);
  const criticalFailures = scoped.filter(f => f.result === 'Fail' && f.severity === 'Critical').length;
  const reportCounts = { ok: report.verified, fail: report.failed, unknown: report.unknown, total: report.required };
  const lower = (keys: string[]) => keys.map(k => t(k)).join(' ').toLocaleLowerCase();
  const needle = query.trim().toLocaleLowerCase();
  // A module matches its own text or any of its features, including detail features that are not listed on the card.
  const featureMatches = (moduleId: string) => !needle ? [] : catalog.capabilities.filter(c => c.areaId === moduleId && lower([c.labelKey, c.purposeKey]).includes(needle)).map(c => t(c.labelKey));
  const searchModules = areas.filter(m => !needle || lower([m.labelKey, m.purposeKey, m.valueKey]).includes(needle) || featureMatches(m.id).length > 0);
  const exploreModules = searchModules.filter(m => !exploreTier || m.tier === exploreTier);
  // Tiles are shown tier by tier; previous and next follow the same order.
  const exploreSequence = tiers.flatMap(tier => exploreModules.filter(m => m.tier === tier));
  const openModule = areas.find(m => m.id === exploreOpen);
  const openIndex = exploreSequence.findIndex(m => m.id === exploreOpen);
  const showModule = (id: string) => { lastExplored.current = id; setExploreOpen(id); };
  const reviewState: 'current' | 'changed' | 'none' = !workspace.reviewEvidence ? 'none'
    : workspace.reviewEvidence.catalogVersion !== catalog.version || workspace.reviewEvidence.snapshotRevision !== report.snapshot.revision || (workspace.reviewEvidence.scopeRevision ?? 0) !== (workspace.scopeRevision ?? 0) ? 'changed' : 'current';
  const showTab = (id: string) => { if (id === 'explore') setExploreOpen(''); setTab(id); };
  // Own classes, not Bootstrap .nav: the theme's sidebar rule `.nav > li.active` would otherwise paint these tabs.
  const tabButton = (id: string, className: string) => <li key={id}>
    <button type="button" className={`rgaa-plain ${className}`} aria-current={tab === id ? 'page' : undefined} aria-disabled={busy || undefined}
      onClick={() => { if (!busy) showTab(id); }}><i className={`fa ${tabIcons[id] ?? 'fa-circle-o'}`} aria-hidden="true" />{ui(id)}</button>
  </li>;
  const explore = (search = '', addons = false) => { setQuery(search); setExploreTier(addons ? 'AddOn' : ''); setExploreOpen(''); setTab('explore'); };
  const moduleCard = (module: Module) => <ModuleCard key={module.id} module={module} features={catalog.capabilities} t={t} ui={ui}
    access={overview.access} setup={overview.capabilitySetup ?? []} findings={report.findings} choice={module.id === 'security' ? 'UseNow' : workspace.areas[module.id]}
    learned={workspace.learnedCapabilityIds} canLearn={catalog.canSetup} busy={busy} onLearn={(id, value) => void save('learn', id, String(value))} link={localLink} showStatus
    scopeControl={catalog.canSetup ? <div className="rgaa-module__scope">
      {suggestedAreas.has(module.id) && <p><Chip tone="info" icon="fa-star">{ui('SuggestedArea')}</Chip></p>}
      <AreaSetupChoice key={`${module.id}:${workspace.areas[module.id] ?? ''}:${workspace.areaReasons?.[module.id] ?? ''}`} areaId={module.id} label={t(module.labelKey)}
        choice={module.id === 'security' ? 'UseNow' : workspace.areas[module.id]} reason={workspace.areaReasons?.[module.id]} disabled={busy} t={t}
        save={(choice, reason) => void save('area', module.id, choice, reason)} />
    </div> : undefined} />;
  const planFrom = catalog.plansAvailable ? (finding: Finding) => { setPlanSource({ source: 'finding', sourceId: finding.ruleId, goal: t(finding.titleKey) }); setTab('plans'); } : undefined;
  const link = (url: string) => localLink(url);
  // Import & Migration (bringing people, stations, units and settings from spreadsheets or another system) starts here: a
  // toolbar button on every tab and a card beside the review, while its flag is on and the reader may run it.
  const importLink = localLink(overview.access.find(a => a.capabilityId === 'import-migration' && a.canConfigure)?.destination);
  const importPanel = importLink && <Ibox title={<><i className="fa fa-cloud-upload" aria-hidden="true" /> {t('Feature.import-migration')}</>}>
    <p>{ui('ImportHelp')}</p>
    <a className="btn btn-primary btn-sm" href={importLink}><i className="fa fa-cloud-upload" aria-hidden="true" /> {ui('OpenImport')}</a>
  </Ibox>;

  // Setup teaches key features only; detail features (and older learned ids) are left to Explore and Ask.
  const keyFeatureIds = new Set(catalog.capabilities.filter(isKey).map(c => c.id));
  const summary = <>
    <MetricRow counts={reportCounts} critical={criticalFailures} learned={workspace.learnedCapabilityIds.filter(id => keyFeatureIds.has(id)).length} totalCapabilities={keyFeatureIds.size} ui={ui} />
    {report.hasCriticalUncertainty && <div className="alert alert-warning" role="status"><i className="fa fa-exclamation-triangle" aria-hidden="true" /> {ui('CriticalUnknown')}</div>}
    {!report.snapshot.consistent && <div className="alert alert-danger" role="alert"><i className="fa fa-refresh" aria-hidden="true" /> {ui('Inconsistent')}</div>}
    {(report.uncheckedAreaIds?.length ?? 0) > 0 && <div className="alert alert-info" role="status"><i className="fa fa-info-circle" aria-hidden="true" /> {ui('UncheckedAreas')}: {report.uncheckedAreaIds.map(areaLabel).join(', ')}.</div>}
    <p className="rgaa-small rgaa-muted"><i className="fa fa-clock-o" aria-hidden="true" /> {ui('Freshness')}: <time dateTime={report.snapshot.asOfUtc}>{new Date(report.snapshot.asOfUtc).toLocaleString()}</time>. {ui('ReportBoundary')} {ui('VerificationCoverage')}</p>
  </>;
  const nextActions = <Ibox title={<><i className="fa fa-flag-o" aria-hidden="true" /> {ui('Next')}</>} tools={next.length > 0 && <button type="button" className="btn btn-white btn-xs" onClick={() => { setHealthArea(''); setHealthResult('Fail'); setTab(setup ? 'report' : 'health'); }}>{ui('ViewAll')}</button>}>
    {next.length > 0 ? <ul className="rgaa-findings">{next.map(f => <FindingRow key={f.ruleId} finding={f} t={t} ui={ui} areaLabel={areaLabel(f.areaId)} link={link} onPlan={planFrom} />)}</ul>
      : <p className="rgaa-muted"><i className="fa fa-check-circle rgaa-icon--ok" aria-hidden="true" /> {ui('NoNextActions')}</p>}
  </Ibox>;
  const areaPanel = <Ibox title={<><i className="fa fa-th-large" aria-hidden="true" /> {ui('AreaReadiness')}</>}>
    <p className="rgaa-small rgaa-muted">{ui('AreaReadinessHelp')}</p>
    <ModuleReadiness modules={areas} features={catalog.capabilities} setup={overview.capabilitySetup ?? []} findings={report.findings} choices={workspace.areas}
      reasons={workspace.areaReasons} t={t} ui={ui} onShowModule={setup ? undefined : moduleId => { setHealthArea(moduleId); setHealthResult(''); setTab('health'); }} />
    {catalog.canSetup && <div className="rgaa-form-actions"><button type="button" className="btn btn-white btn-xs" onClick={() => setTab('wizard')}><i className="fa fa-sliders" aria-hidden="true" /> {ui('ChangeScope')}</button></div>}
  </Ibox>;
  const reviewPanel = <Ibox title={<><i className="fa fa-check-square-o" aria-hidden="true" /> {ui('SetupReview')}</>}>
    <p><strong>{ui('Reviewed')}:</strong> {workspace.reviewedOnUtc ? new Date(workspace.reviewedOnUtc).toLocaleString() : ui('NoReview')}
      {' '}{reviewState === 'current' && <Chip tone="ok" icon="fa-check">{ui('StepReviewed')}</Chip>}{reviewState === 'changed' && <Chip tone="unknown" icon="fa-exclamation">{ui('StepReviewChanged')}</Chip>}</p>
    {workspace.reviewEvidence && <p className="rgaa-small rgaa-muted">{ui('ReviewEvidence')}: {workspace.reviewEvidence.verified} / {workspace.reviewEvidence.required} · {ui('Failures')}: {workspace.reviewEvidence.failed} · {ui('UnknownChecks')}: {workspace.reviewEvidence.unknown} · {new Date(workspace.reviewEvidence.asOfUtc).toLocaleString()}</p>}
    {reviewState === 'changed' && <p className="rgaa-warning rgaa-small" role="status">{ui('ReviewChanged')}</p>}
    {catalog.canSetup && <p><button type="button" className="btn btn-primary btn-sm rgaa-review-button" disabled={busy || !report.snapshot.consistent} onClick={() => void save('review')}><i className="fa fa-check-square-o" aria-hidden="true" /> {ui('MarkReviewed')}</button></p>}
    {catalog.canSetup && <form key={`revisit-${workspace.revision}`} onSubmit={event => { event.preventDefault(); const value = new FormData(event.currentTarget).get('revisit')?.toString(); void save('revisit', null, null, null, value ? `${value}T12:00:00.000Z` : null); }}>
      <label htmlFor="rgaa-revisit" className="rgaa-field-label">{ui('RevisitDate')}</label>
      <div className="rgaa-inline-form">
        <input id="rgaa-revisit" className="form-control input-sm" type="date" name="revisit" min={new Date(Date.now() + 86400000).toISOString().slice(0, 10)} max={new Date(Date.now() + 364 * 86400000).toISOString().slice(0, 10)} defaultValue={workspace.revisitOnUtc?.slice(0, 10) ?? ''} />
        <button type="submit" className="btn btn-white btn-sm" disabled={busy}><i className="fa fa-calendar-check-o" aria-hidden="true" /> {ui('SaveRevisit')}</button>
      </div>
      <p className="help-block">{ui('RevisitHelp')}</p>
    </form>}
    <div className="rgaa-form-actions">
      {catalog.canSetup && workspace.revisitOnUtc && <a className="btn btn-white btn-xs" href="/User/AdminAssist/ReviewCalendar"><i className="fa fa-calendar" aria-hidden="true" /> {ui('DownloadReminder')}</a>}
      <a className="btn btn-white btn-xs" href={`/User/AdminAssist/PrintReport?setup=${setup ? 'true' : 'false'}`} target="_blank" rel="noopener noreferrer"><i className="fa fa-print" aria-hidden="true" /> {ui('PrintableReport')}</a>
    </div>
  </Ibox>;
  const previews = !setup && <Ibox title={<><i className="fa fa-line-chart" aria-hidden="true" /> {ui('ImpactPreviews')}</>}>
    <p className="help-block">{ui('ImpactPreviewsHelp')}</p>
    <div className="rgaa-grid rgaa-previews">
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
  // Settings reference: the search narrows the module counts; a module narrows the list, shown a page at a time (the catalog has hundreds).
  const referenceNeedle = query.trim().toLocaleLowerCase();
  const referenceScope = catalog.settings.filter(s => !referenceNeedle || `${t(s.labelKey)} ${t(s.helpKey)}`.toLocaleLowerCase().includes(referenceNeedle));
  const referenceSettings = referenceScope.filter(s => !referenceArea || s.areaId === referenceArea);
  const referenceAreas = areas.filter(area => catalog.settings.some(s => s.areaId === area.id));
  // Change history, newest first, grouped by local day; subjects read as their catalog names where one exists.
  const historyDays = history.reduce<[string, History[]][]>((days, row) => {
    const day = new Date(row.occurredOnUtc).toLocaleDateString(undefined, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' });
    const last = days[days.length - 1];
    if (last && last[0] === day) last[1].push(row); else days.push([day, [row]]);
    return days;
  }, []);
  const subjectLabel = (id: string) => {
    const key = catalog.settings.find(s => s.id === id)?.labelKey ?? catalog.capabilities.find(c => c.id === id)?.labelKey ?? areas.find(a => a.id === id)?.labelKey;
    return key ? t(key) : id;
  };
  const healthScope = report.findings.filter(f => !healthArea || f.areaId === healthArea);
  const healthFindings = healthScope.filter(f => !healthResult || f.result === healthResult);
  const healthAreas = areas.filter(area => report.findings.some(f => f.areaId === area.id));
  const allHealthCounts = countFindings(report.findings);
  // Module counts match the Needs attention filter (suggestions included), so a module's number is what that filter lists.
  const failingIn = (areaId?: string) => report.findings.filter(f => f.result === 'Fail' && (!areaId || f.areaId === areaId)).length;
  const workFilters: { value: string; label: string; icon?: string; tone?: Tone; match: (item: WorkItem) => boolean }[] = [
    { value: 'active', label: ui('ActiveFindings'), icon: resultIcons.Fail.icon, tone: resultIcons.Fail.tone, match: item => item.result === 1 },
    { value: 'unknown', label: ui('Unknown'), icon: resultIcons.Unknown.icon, tone: resultIcons.Unknown.tone, match: item => item.result === 2 },
    { value: 'exceptions', label: ui('AcceptedException'), icon: 'fa-shield', tone: 'info', match: item => item.reviewStatus === 3 },
    { value: 'all', label: ui('AllFindings'), match: () => true },
  ];
  const visibleWork = worklist.filter(workFilters.find(filter => filter.value === workFilter)?.match ?? (() => true));

  return <FindingSubjectsContext.Provider value={overview.findingSubjects ?? {}}><section className="rgaa" dir={catalog.rightToLeft ? 'rtl' : 'ltr'} aria-busy={busy}>
    <div className="rgaa-head">
      <nav aria-label={ui('Title')}><ul className="rgaa-tabs">{tabs.map(id => tabButton(id, 'rgaa-tab'))}
        {aiTabs.length > 0
          ? <li><button type="button" className="rgaa-plain rgaa-tab" aria-current={inAi ? 'true' : undefined} aria-disabled={busy || undefined}
            onClick={() => { if (!busy && !inAi) showTab(aiTabs[0]); }}><i className={`fa ${tabIcons.ai}`} aria-hidden="true" />{ui('ai')}</button></li>
          : <li><button type="button" className="rgaa-plain rgaa-tab rgaa-tab--off" aria-disabled="true"><i className="fa fa-lock" aria-hidden="true" />{ui('ai')}</button></li>}
      </ul></nav>
      <div className="rgaa-toolbar">
        {busy && <span className="rgaa-muted rgaa-small" role="status"><i className="fa fa-spinner fa-spin" aria-hidden="true" /> {ui('Loading')}</span>}
        {importLink && <a className="btn btn-white btn-sm" href={importLink}><i className="fa fa-cloud-upload" aria-hidden="true" /> {t('Feature.import-migration')}</a>}
        <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void verify()}><i className="fa fa-refresh" aria-hidden="true" /> {ui('Verify')}</button>
      </div>
    </div>
    {inAi && <nav aria-label={ui('ai')}><ul className="rgaa-subnav">{aiTabs.map(id => tabButton(id, 'rgaa-subtab'))}</ul></nav>}
    <h2 className="sr-only">{ui(tab)}</h2>
    {error && <div role="alert" className="alert alert-danger">{error}</div>}
    {tab === 'report' && <>{summary}
      {/* Work first (next actions, the per-feature checklist, every check); the review, module readiness and import sit beside it. */}
      <div className="rgaa-workbench rgaa-report">
        <div>
          {nextActions}
          {overview.setupPlan && overview.setupPlan.tasks.length > 0 && <Ibox title={<><i className="fa fa-tasks" aria-hidden="true" /> {ui('SetupChecklist')}</>}><SetupChecklist plan={overview.setupPlan} t={t} capabilities={catalog.capabilities} areas={areas} localLink={localLink} /></Ibox>}
          <Ibox title={<><i className="fa fa-heartbeat" aria-hidden="true" /> {ui('AllChecks')}</>}><FindingGroups findings={scoped.filter(f => f.result !== 'NotApplicable')} t={t} ui={ui} areas={areas} link={link} onPlan={planFrom} /></Ibox>
          {previews}
        </div>
        <aside className="rgaa-workbench__side">{reviewPanel}{areaPanel}{importPanel}</aside>
      </div>
    </>}
    {tab === 'wizard' && <SetupWizard t={t} mode={workspace.mode} canSetup={catalog.canSetup} busy={busy} modules={areas} features={catalog.capabilities} access={overview.access}
      capabilitySetup={overview.capabilitySetup ?? []} packs={catalog.packs} choices={workspace.areas} reasons={workspace.areaReasons} learned={workspace.learnedCapabilityIds}
      findings={report.findings} selectedAreas={report.selectedAreas} profileKnown={operatingPacks?.state === 'Known'}
      selectedPackIds={selectedPackIds} suggestedAreas={suggestedAreas} reviewed={reviewState} localLink={localLink}
      reportCounts={{ failed: report.failed, unknown: report.unknown, required: report.required, verified: report.verified, criticalUnknown: report.hasCriticalUncertainty }}
      save={(operation, targetId = null, choice = null, reasonCode = null) => void save(operation, targetId, choice, reasonCode)}
      show={(page, search, addons) => page === 'explore' ? explore(search ?? '', addons ?? false) : setTab(page)} reviewPanel={reviewPanel} summary={summary} />}
    {tab === 'explore' && openModule && <div className="rgaa-explore-detail">
      <nav className="rgaa-explore-detail__bar" aria-label={t(openModule.labelKey)}>
        <button type="button" className="btn btn-white btn-sm" onClick={() => setExploreOpen('')}><i className="fa fa-th-large" aria-hidden="true" /> {ui('AllAreas')}</button>
        <span className="rgaa-explore-detail__step">
          {openIndex > 0 && <button type="button" className="btn btn-white btn-sm" onClick={() => showModule(exploreSequence[openIndex - 1].id)}>
            <i className="fa fa-chevron-left" aria-hidden="true" /> {t(exploreSequence[openIndex - 1].labelKey)}</button>}
          {openIndex >= 0 && openIndex < exploreSequence.length - 1 && <button type="button" className="btn btn-white btn-sm" onClick={() => showModule(exploreSequence[openIndex + 1].id)}>
            {t(exploreSequence[openIndex + 1].labelKey)} <i className="fa fa-chevron-right" aria-hidden="true" /></button>}
        </span>
      </nav>
      {moduleCard(openModule)}
    </div>}
    {tab === 'explore' && !openModule && <>
      <p className="rgaa-lead">{ui('ExploreHelp')}</p>
      <div className="rgaa-search-bar">
        <label htmlFor="rgaa-explore-search" className="sr-only">{ui('Search')}</label>
        <span className="rgaa-search-bar__field"><i className="fa fa-search" aria-hidden="true" />
          <input id="rgaa-explore-search" className="form-control" type="search" value={query} placeholder={ui('Search')} onChange={e => setQuery(e.target.value)} /></span>
      </div>
      <FilterPills label={ui('Area')} value={exploreTier} onChange={setExploreTier} options={[
        { value: '', label: ui('AllAreas'), count: searchModules.length },
        ...tiers.map(tier => ({ value: tier, label: ui(`Tier.${tier}`), icon: tier === 'AddOn' ? 'fa-tag' : undefined, count: searchModules.filter(m => m.tier === tier).length })),
      ]} />
      <p className="rgaa-small rgaa-muted"><i className="fa fa-info-circle" aria-hidden="true" /> {ui('NoAutomaticActions')}</p>
      {tiers.map(tier => {
        const modules = exploreModules.filter(m => m.tier === tier);
        if (modules.length === 0) return null;
        return <section key={tier} aria-labelledby={`rgaa-tier-${tier}`}>
          <div className="rgaa-area-heading"><h3 id={`rgaa-tier-${tier}`}>{ui(`Tier.${tier}`)}</h3><span className="rgaa-muted rgaa-small">{ui(`TierHelp.${tier}`)}</span></div>
          <ul className="rgaa-tile-grid">{modules.map(module => <li key={module.id}><ModuleTile module={module} features={catalog.capabilities} access={overview.access}
            setup={overview.capabilitySetup ?? []} findings={report.findings} choice={module.id === 'security' ? 'UseNow' : workspace.areas[module.id]} t={t} ui={ui}
            matches={featureMatches(module.id)} suggested={suggestedAreas.has(module.id)} showStatus onOpen={() => showModule(module.id)} /></li>)}</ul>
        </section>;
      })}
      {exploreModules.length === 0 && <div className="rgaa-empty"><span className="rgaa-ask__empty-icon" aria-hidden="true"><i className="fa fa-search" /></span><p>{ui('NoResults')}</p></div>}
    </>}
    {tab === 'health' && <>{summary}
      <div className="rgaa-workbench rgaa-workbench--side-first">
        <div>
          <Ibox title={<><i className="fa fa-heartbeat" aria-hidden="true" /> {healthArea ? areaLabel(healthArea) : ui('health')}</>}>
            <FilterPills label={ui('Filter')} value={healthResult} onChange={setHealthResult} options={[
              { value: '', label: ui('AllResults'), count: healthScope.length },
              ...healthResults.map(result => ({ value: result, label: ui(result), icon: resultIcons[result].icon, tone: resultIcons[result].tone, count: healthScope.filter(f => f.result === result).length })),
            ]} />
            <FindingGroups findings={healthFindings} t={t} ui={ui} areas={areas} link={link} onPlan={planFrom} showArea={!healthArea} />
          </Ibox>
        </div>
        <aside className="rgaa-workbench__side">
          <Ibox title={<><i className="fa fa-th-large" aria-hidden="true" /> {ui('Area')}</>}>
            <ul className="rgaa-history-list rgaa-area-list">
              <li><button type="button" className="rgaa-plain rgaa-history-list__item" aria-current={!healthArea ? 'true' : undefined} onClick={() => setHealthArea('')}>
                <span className="rgaa-history-list__text"><span>{ui('AllAreas')}</span><StatusBar thin counts={allHealthCounts} label={countsLabel(allHealthCounts, ui)} /></span>
                <span className={`rgaa-history-list__count${failingIn() > 0 ? ' rgaa-history-list__count--fail' : ''}`} title={ui('Fail')}><span className="sr-only">{ui('Fail')}: </span>{failingIn()}</span>
              </button></li>
              {healthAreas.map(area => {
                const counts = countFindings(report.findings.filter(f => f.areaId === area.id));
                return <li key={area.id}><button type="button" className="rgaa-plain rgaa-history-list__item" aria-current={healthArea === area.id ? 'true' : undefined} onClick={() => setHealthArea(area.id)}>
                  <span className="rgaa-history-list__text"><span>{t(area.labelKey)}</span><StatusBar thin counts={counts} label={countsLabel(counts, ui)} /></span>
                  <span className={`rgaa-history-list__count${failingIn(area.id) > 0 ? ' rgaa-history-list__count--fail' : ''}`} title={ui('Fail')}><span className="sr-only">{ui('Fail')}: </span>{failingIn(area.id)}</span>
                </button></li>;
              })}
            </ul>
          </Ibox>
        </aside>
      </div>
    </>}
    {tab === 'worklist' && <>
      <p className="rgaa-lead">{ui('WorklistHelp')}</p>
      <div className="rgaa-workbench">
        <div>
          <Ibox title={<><i className="fa fa-tasks" aria-hidden="true" /> {ui('worklist')}</>}>
            {worklist.length === 0 ? <div className="rgaa-empty">
              <span className="rgaa-ask__empty-icon" aria-hidden="true"><i className="fa fa-tasks" /></span>
              <p>{ui('VerifyToPopulate')}</p>
              <button type="button" className="btn btn-primary btn-sm" disabled={busy} onClick={() => void verify()}><i className="fa fa-refresh" aria-hidden="true" /> {ui('Verify')}</button>
            </div> : <>
              <FilterPills label={ui('Filter')} value={workFilter} onChange={setWorkFilter} options={workFilters.map(filter => ({ value: filter.value, label: filter.label, icon: filter.icon, tone: filter.tone, count: worklist.filter(filter.match).length }))} />
              {visibleWork.length === 0 ? <p className="rgaa-muted">{ui('None')}</p> : <ul className="rgaa-findings">{visibleWork.map(item => {
                const finding = report.findings.find(f => f.ruleId === item.ruleId);
                const result = ['Pass', 'Fail', 'Unknown', 'NotApplicable'][item.result] ?? 'Unknown';
                const status = reviewStatuses[item.reviewStatus] ?? reviewStatuses[0];
                const destination = finding ? localLink(finding.destination) : undefined;
                const id = item.adminAssistFindingId;
                return <li className="rgaa-finding rgaa-work-item" key={id}>
                  <span className={`rgaa-finding__icon rgaa-icon--${(resultIcons[result] ?? resultIcons.Unknown).tone}`}><i className={`fa ${(resultIcons[result] ?? resultIcons.Unknown).icon}`} aria-hidden="true" /></span>
                  <div className="rgaa-finding__body">
                    <div className="rgaa-finding__meta"><ResultChip result={result} ui={ui} /><Chip tone={status.tone} icon={status.icon}>{ui(status.key)}</Chip>
                      {finding && <SeverityChip severity={finding.severity} ui={ui} />}{finding && <span className="rgaa-muted rgaa-small">{areaLabel(finding.areaId)}</span>}</div>
                    <p className="rgaa-finding__title">{finding ? t(finding.titleKey) : item.ruleId}</p>
                    {finding && <p>{t(finding.explanationKey)}</p>}
                    <ul className="rgaa-work-item__dates">
                      <li><i className="fa fa-clock-o" aria-hidden="true" /> {ui('Freshness')}: <time dateTime={item.lastObservedOn}>{new Date(item.lastObservedOn).toLocaleString()}</time></li>
                      {item.reviewOn && <li><i className="fa fa-calendar" aria-hidden="true" /> {ui('ReviewDate')}: <time dateTime={item.reviewOn}>{new Date(item.reviewOn).toLocaleString()}</time></li>}
                      {item.exceptionUntil && <li><i className="fa fa-hourglass-half" aria-hidden="true" /> {ui('ExceptionExpiry')}: <time dateTime={item.exceptionUntil}>{new Date(item.exceptionUntil).toLocaleString()}</time></li>}
                    </ul>
                    {item.content && <p className="rgaa-note">{item.content}</p>}
                    {(destination || (finding && planFrom)) && <div className="rgaa-finding__actions">
                      {destination && finding && <a className="btn btn-white btn-xs" href={destination}><i className="fa fa-external-link" aria-hidden="true" /> {t(finding.nextActionKey)}</a>}
                      {finding && planFrom && <button type="button" className="btn btn-white btn-xs" onClick={() => planFrom(finding)}><i className="fa fa-list-ol" aria-hidden="true" /> {t('Plan.New')}</button>}
                    </div>}
                    {item.reviewStatus !== 4 && (item.result === 1 || item.result === 2) && <div className="rgaa-work-item__review">
                      <button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void review(item, 'review')}><i className="fa fa-eye" aria-hidden="true" /> {ui('StartReview')}</button>
                      <form className="rgaa-inline-form" onSubmit={e => { e.preventDefault(); void review(item, 'claim', e.currentTarget); }}>
                        <label htmlFor={`rgaa-claim-${id}`}>{ui('ReviewDate')}</label>
                        <input id={`rgaa-claim-${id}`} className="form-control input-sm" type="datetime-local" name="date" />
                        <button type="submit" className="btn btn-white btn-sm" disabled={busy}><i className="fa fa-user" aria-hidden="true" /> {ui('AssignMe')}</button>
                      </form>
                    </div>}
                    {item.result === 1 && item.reviewStatus !== 4 && <details className="rgaa-plan-step__more rgaa-work-item__exception"><summary className="rgaa-small">{ui('AcceptException')}</summary>
                      <form onSubmit={e => { e.preventDefault(); void review(item, 'exception', e.currentTarget); }}>
                        <div className="form-group"><label htmlFor={`rgaa-note-${id}`}>{ui('Reason')}</label><textarea id={`rgaa-note-${id}`} className="form-control" rows={2} name="note" required maxLength={2000} /></div>
                        <div className="rgaa-form-row"><div className="form-group"><label htmlFor={`rgaa-exp-${id}`}>{ui('ExceptionExpiry')}</label><input id={`rgaa-exp-${id}`} className="form-control input-sm" type="datetime-local" name="date" required /></div></div>
                        <p className="help-block">{ui('ExceptionHelp')}</p>
                        <button type="submit" className="btn btn-warning btn-xs" disabled={busy}><i className="fa fa-shield" aria-hidden="true" /> {ui('AcceptException')}</button>
                      </form>
                    </details>}
                  </div>
                </li>;
              })}</ul>}
            </>}
          </Ibox>
        </div>
        <aside className="rgaa-workbench__side">
          {followup && <Ibox title={<><i className="fa fa-bell-o" aria-hidden="true" /> {ui('Preferences')}</>}>
            <p className="rgaa-small rgaa-muted"><i className="fa fa-clock-o" aria-hidden="true" /> {followup.worker.lastEvaluatedOn
              ? <>{ui('WorkerLastRun')}: <time dateTime={followup.worker.lastEvaluatedOn}>{new Date(followup.worker.lastEvaluatedOn).toLocaleString()}</time></> : ui('WorkerNever')}</p>
            {!followup.digestsAvailable && <div className="alert alert-warning rgaa-small">{ui('DigestUnavailable')}</div>}
            <form key={followup.preferences.revision} onSubmit={e => { e.preventDefault(); void savePreferences(e.currentTarget); }}>
              <label className="rgaa-check"><input name="digest" type="checkbox" defaultChecked={followup.preferences.digestEnabled} />{ui('DigestOptIn')}</label>
              <div className="form-group"><label htmlFor="rgaa-quiet-start">{ui('QuietStart')}</label><input id="rgaa-quiet-start" className="form-control input-sm" name="start" type="number" min={0} max={23} required defaultValue={followup.preferences.quietStartHour} /></div>
              <div className="form-group"><label htmlFor="rgaa-quiet-end">{ui('QuietEnd')}</label><input id="rgaa-quiet-end" className="form-control input-sm" name="end" type="number" min={0} max={23} required defaultValue={followup.preferences.quietEndHour} /></div>
              <p className="help-block">{ui('DigestHelp')}</p>
              <div className="rgaa-form-actions"><button type="submit" className="btn btn-white btn-sm" disabled={busy}><i className="fa fa-floppy-o" aria-hidden="true" /> {ui('SavePreferences')}</button></div>
            </form>
            {followup.preferences.lastAttemptOutcome && <p className="rgaa-small rgaa-muted">{ui('DigestOutcome')}: {followup.preferences.lastAttemptOutcome}</p>}
          </Ibox>}
        </aside>
      </div>
    </>}
    {tab === 'reference' && <>
      <form className="rgaa-search-bar" onSubmit={e => { e.preventDefault(); void searchReference(); }}>
        <label htmlFor="rgaa-reference-search" className="sr-only">{ui('Search')}</label>
        <span className="rgaa-search-bar__field"><i className="fa fa-search" aria-hidden="true" />
          <input id="rgaa-reference-search" className="form-control" type="search" value={query} maxLength={256} placeholder={ui('Search')} onChange={e => { setQuery(e.target.value); setReferenceLimit(referencePage); }} /></span>
        <button type="submit" className="btn btn-white btn-sm" disabled={busy || !query.trim()}><i className="fa fa-book" aria-hidden="true" /> {ui('SearchReference')}</button>
      </form>
      {hits.length > 0 && <Ibox title={<><i className="fa fa-book" aria-hidden="true" /> {ui('ReferenceResults')}</>}><ul className="rgaa-findings">{hits.map(hit => <li key={hit.id} className="rgaa-finding">
        <span className="rgaa-finding__icon rgaa-icon--muted"><i className="fa fa-file-text-o" aria-hidden="true" /></span><div className="rgaa-finding__body">
          <p className="rgaa-finding__title">{t(hit.titleKey)}</p><p lang={hit.locale}>{hit.excerpt}</p><p className="rgaa-small rgaa-muted">{hit.sourcePath}#{hit.anchor} · {hit.packVersion} · {hit.locale}</p>
          {catalog.articles.filter(a => a.id === hit.id && a.locale === hit.locale && a.packVersion === hit.packVersion).map(article => <details key={article.id} className="rgaa-plan-step__more"><summary className="rgaa-small">{ui('ReadSource')}</summary><div className="rgaa-source" lang={article.locale}>{article.body}</div></details>)}
        </div></li>)}</ul></Ibox>}
      <div className="rgaa-workbench rgaa-workbench--side-first">
        <div>
          <Ibox title={<><i className="fa fa-sliders" aria-hidden="true" /> {referenceArea ? areaLabel(referenceArea) : ui('reference')}</>}
            tools={<span className="rgaa-history-list__count">{referenceSettings.length}</span>}>
            {referenceSettings.length === 0 ? <p className="rgaa-muted">{ui('None')}</p> : <ul className="rgaa-settings">{referenceSettings.slice(0, referenceLimit).map(setting => {
              const previewable = !setup && catalog.impactSettings.includes(setting.id);
              const destination = localLink(setting.location.url);
              return <li key={setting.id}><details className="rgaa-setting">
                <summary>
                  <span className="rgaa-setting__main"><span className="rgaa-setting__name">{t(setting.labelKey)}</span>
                    {!referenceArea && <span className="rgaa-setting__area">{areaLabel(setting.areaId)}</span>}</span>
                  {previewable && <span className="rgaa-setting__flag" title={ui('ImpactPreviews')}><i className="fa fa-line-chart" aria-hidden="true" /><span className="sr-only">{ui('ImpactPreviews')}</span></span>}
                </summary>
                <div className="rgaa-setting__body">
                  <p>{t(setting.helpKey)}</p>
                  <ul className="rgaa-plan-step__facts">
                    <li><i className="fa fa-clock-o" aria-hidden="true" /><span>{t(setting.impact.timingKey)}</span></li>
                    <li><i className="fa fa-undo" aria-hidden="true" /><span>{t(setting.impact.reversibilityKey)}</span></li>
                  </ul>
                  {previewable && <ImpactPreview key={`${setting.id}:${report.snapshot.asOfUtc}`} settingId={setting.id} valueType={setting.valueType} revision={report.snapshot.revision} t={t} />}
                  {destination && <div className="rgaa-actions-row"><a className="btn btn-white btn-xs" href={destination}><i className="fa fa-external-link" aria-hidden="true" /> {ui('Configure')}</a></div>}
                </div>
              </details></li>;
            })}</ul>}
            {referenceSettings.length > referenceLimit && <div className="rgaa-form-actions"><button type="button" className="btn btn-white btn-sm" onClick={() => setReferenceLimit(limit => limit + referencePage)}>
              <i className="fa fa-chevron-down" aria-hidden="true" /> {ui('More')} <span className="rgaa-muted">({referenceLimit} / {referenceSettings.length})</span></button></div>}
          </Ibox>
        </div>
        <aside className="rgaa-workbench__side">
          <Ibox title={<><i className="fa fa-th-large" aria-hidden="true" /> {ui('Area')}</>}>
            <ul className="rgaa-history-list rgaa-area-list">
              <li><button type="button" className="rgaa-plain rgaa-history-list__item" aria-current={!referenceArea ? 'true' : undefined} onClick={() => { setReferenceArea(''); setReferenceLimit(referencePage); }}>
                <span className="rgaa-history-list__text">{ui('AllAreas')}</span><span className="rgaa-history-list__count">{referenceScope.length}</span></button></li>
              {referenceAreas.map(area => {
                const count = referenceScope.filter(s => s.areaId === area.id).length;
                return <li key={area.id}><button type="button" className={`rgaa-plain rgaa-history-list__item${count === 0 ? ' rgaa-history-list__item--empty' : ''}`} aria-current={referenceArea === area.id ? 'true' : undefined}
                  onClick={() => { setReferenceArea(area.id); setReferenceLimit(referencePage); }}>
                  <span className="rgaa-history-list__text">{t(area.labelKey)}</span><span className="rgaa-history-list__count">{count}</span></button></li>;
              })}
            </ul>
          </Ibox>
        </aside>
      </div>
    </>}
    {tab === 'plans' && catalog.plansAvailable && <PlansPanel askAvailable={catalog.askAvailable} t={t} settings={catalog.settings.filter(s => catalog.impactSettings.includes(s.id))} capabilities={catalog.capabilities} source={planSource} />}
    {tab === 'troubleshoot' && catalog.troubleshootingAvailable && <TroubleshootPanel t={t} localLink={localLink} capabilities={catalog.capabilities} />}
    {tab === 'ask' && catalog.askAvailable && <AskPanel onPlan={catalog.plansAvailable ? source => { setPlanSource(source); setTab('plans'); } : undefined} t={t} localLink={localLink} settings={catalog.settings.filter(s => catalog.impactSettings.includes(s.id))} />}
    {tab === 'history' && <Ibox title={<><i className="fa fa-history" aria-hidden="true" /> {ui('history')}</>}>
      <p className="help-block"><i className="fa fa-info-circle" aria-hidden="true" /> {ui('HistoryBoundary')}</p>
      {history.length === 0 ? !busy && <div className="rgaa-empty"><span className="rgaa-ask__empty-icon" aria-hidden="true"><i className="fa fa-history" /></span><p>{ui('None')}</p></div>
        : <ol className="rgaa-changelog">{historyDays.map(([day, rows]) => <li key={day}>
          <h4 className="rgaa-section-title">{day}</h4>
          <ol className="rgaa-changelog__day">{rows.map(row => {
            const subject = subjectLabel(row.subjectId);
            return <li key={row.id} className="rgaa-changelog__row">
              <time dateTime={row.occurredOnUtc} title={new Date(row.occurredOnUtc).toLocaleString()}><span className="sr-only">{ui('HistoryTime')}: </span>{new Date(row.occurredOnUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</time>
              <div className="rgaa-changelog__body">
                <p className="rgaa-changelog__head"><span title={ui('HistoryAction')}><Chip tone="info">{row.action}</Chip></span>
                  <span className="sr-only">{ui('HistorySubject')}: </span><strong>{subject}</strong>{subject !== row.subjectId && <code className="rgaa-changelog__id">{row.subjectId}</code>}</p>
                <p className="rgaa-change"><span className="rgaa-change__from" title={ui('Before')}><span className="sr-only">{ui('Before')}: </span>{row.beforeCode}</span>
                  <i className="fa fa-long-arrow-right" aria-hidden="true" /><span className="rgaa-change__to" title={ui('After')}><span className="sr-only">{ui('After')}: </span>{row.afterCode}</span></p>
              </div>
            </li>;
          })}</ol>
        </li>)}</ol>}
      {hasMore && history.length > 0 && <div className="rgaa-form-actions"><button type="button" className="btn btn-white btn-sm" disabled={busy} onClick={() => void loadHistory()}><i className="fa fa-chevron-down" aria-hidden="true" /> {ui('More')}</button></div>}
    </Ibox>}
  </section></FindingSubjectsContext.Provider>;
}
