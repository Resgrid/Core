import { useEffect, useRef, useState, type ReactNode } from 'react';
import AreaSetupChoice from './AreaSetupChoice';
import { Chip, FindingRow, ProgressBar, byPriority, format, type Finding, type Translate } from './SetupVisuals';
import { CommercialChip, DocsLink, FeatureRow, ModuleCard, ModuleStatusChip, inScope, keyFeatures, moduleStatus, tiers,
  type Access, type Choice, type Feature, type Module, type Setup } from './ModuleViews';

type Pack = { id: string; labelKey: string; purposeKey: string; prerequisiteKeys: string[] };
type StepStatus = 'done' | 'attention' | 'progress' | 'todo' | 'info';
type Step = { id: number; modules: (all: Module[], choices: Record<string, Choice>) => Module[] };

// Steps 4-7 walk the modules in setup priority. Step 6 only includes the recommended and optional modules the admin chose.
const steps: Step[] = [
  { id: 1, modules: () => [] },
  { id: 2, modules: () => [] },
  { id: 3, modules: () => [] },
  { id: 4, modules: all => all.filter(m => m.id === 'people' || m.id === 'security') },
  { id: 5, modules: all => all.filter(m => m.tier === 'Core' && !['people', 'security', 'plans'].includes(m.id)) },
  { id: 6, modules: (all, choices) => all.filter(m => (m.tier === 'Recommended' || m.tier === 'Optional') && inScope(m, choices)) },
  { id: 7, modules: all => all.filter(m => m.tier === 'AddOn' || m.id === 'plans') },
  { id: 8, modules: () => [] },
  { id: 9, modules: () => [] },
];
const statusTone: Record<StepStatus, 'ok' | 'fail' | 'unknown' | 'muted' | 'info'> = { done: 'ok', attention: 'fail', progress: 'unknown', todo: 'muted', info: 'info' };
const statusIcon: Record<StepStatus, string> = { done: 'fa-check', attention: 'fa-exclamation', progress: 'fa-ellipsis-h', todo: '', info: '' };
const modes: { value: string; icon: string }[] = [{ value: 'Fresh', icon: 'fa-flag-o' }, { value: 'Review', icon: 'fa-search' }, { value: 'Import', icon: 'fa-cloud-upload' }];

export default function SetupWizard(props: {
  t: Translate; mode: string; canSetup: boolean; busy: boolean;
  modules: Module[]; features: Feature[]; access: Access[]; capabilitySetup: Setup[]; packs: Pack[];
  choices: Record<string, Choice>; reasons?: Record<string, string>; learned: string[];
  findings: Finding[]; selectedAreas: string[]; profileKnown: boolean; selectedPackIds: string[]; suggestedAreas: Set<string>;
  reportCounts: { failed: number; unknown: number; required: number; verified: number; criticalUnknown: boolean };
  reviewed: 'current' | 'changed' | 'none';
  localLink: (url: string | null | undefined) => string | undefined;
  save: (operation: string, targetId?: string | null, choice?: string | null, reasonCode?: string | null) => void;
  show: (page: string, query?: string, addonsOnly?: boolean) => void;
  reviewPanel: ReactNode; summary: ReactNode;
}) {
  const { t, modules, features, access, findings, choices } = props;
  const ui = (key: string) => t(`Ui.${key}`);
  const inReport = (f: Finding) => f.scopeIndependent || f.areaId === 'security' || props.selectedAreas.includes(f.areaId);
  const moduleFindings = (module: Module) => findings.filter(f => f.areaId === module.id && inReport(f));
  const statusOf = (module: Module) => moduleStatus(module, features, props.capabilitySetup, moduleFindings(module));
  const chosen = modules.filter(m => m.id === 'security' || choices[m.id] !== undefined).length;
  const addons = modules.filter(m => m.tier === 'AddOn');
  const addonsChosen = addons.filter(m => choices[m.id] !== undefined).length;
  const featureRow = (feature: Feature) => <FeatureRow key={feature.id} feature={feature} t={t} ui={ui} access={access.find(a => a.capabilityId === feature.id)}
    setupState={props.capabilitySetup.find(s => s.capabilityId === feature.id)?.state} learned={props.learned.includes(feature.id)} canLearn={props.canSetup}
    busy={props.busy} onLearn={value => props.save('learn', feature.id, String(value))} link={props.localLink} />;
  const importLink = props.localLink(access.find(a => a.capabilityId === 'import-migration' && a.canConfigure)?.destination);

  function status(step: Step): { status: StepStatus; text: string } {
    const stepModules = step.modules(modules, choices).filter(m => step.id === 7 || inScope(m, choices));
    if (step.id >= 4 && step.id <= 6) {
      if (stepModules.length === 0) return { status: 'info', text: ui('NoModulesChosen') };
      const states = stepModules.map(m => statusOf(m).state);
      const attention = stepModules.reduce((sum, m) => sum + statusOf(m).counts.fail, 0);
      if (states.includes('Attention')) return { status: 'attention', text: attention > 0 ? format(ui('StepNeedsAttention'), attention) : ui('ModuleStatus.Attention') };
      if (states.every(s => s === 'Done')) return { status: 'done', text: ui('StepStatus.Done') };
      return states.some(s => s === 'Done' || s === 'Progress') ? { status: 'progress', text: ui('ModuleStatus.Progress') } : { status: 'todo', text: ui('ModuleStatus.NotStarted') };
    }
    switch (step.id) {
      case 1: return { status: 'info', text: ui(props.mode) };
      case 2: return props.profileKnown && props.selectedPackIds.length > 0 ? { status: 'done', text: ui('ProfileRecorded') } : { status: 'todo', text: ui('ProfileMissing') };
      case 3: return { status: chosen === modules.length ? 'done' : chosen > 1 ? 'progress' : 'todo', text: format(ui('AreasChosen'), chosen, modules.length) };
      case 7: return { status: addonsChosen === addons.length ? 'done' : addonsChosen > 0 ? 'progress' : 'info', text: format(ui('AddonsReviewed'), addonsChosen, addons.length) };
      case 8: return props.reportCounts.failed > 0 ? { status: 'attention', text: format(ui('StepNeedsAttention'), props.reportCounts.failed) }
        : props.reportCounts.unknown > 0 || props.reportCounts.criticalUnknown ? { status: 'progress', text: format(ui('StepUnknown'), props.reportCounts.unknown) }
        : { status: 'done', text: ui('StepStatus.Done') };
      case 9: return props.reviewed === 'current' ? { status: 'done', text: ui('StepReviewed') } : props.reviewed === 'changed' ? { status: 'attention', text: ui('StepReviewChanged') } : { status: 'todo', text: ui('NoReview') };
      default: return { status: 'info', text: '' };
    }
  }
  const statuses = steps.map(status);
  const [active, setActive] = useState(() => Math.max(0, statuses.findIndex(s => s.status === 'todo' || s.status === 'attention')));
  const step = steps[active];
  const current = statuses[active];
  const done = statuses.filter(s => s.status === 'done').length;
  // Moving to another step brings its heading into view and focus, so keyboard and screen-reader users land on the new content.
  const heading = useRef<HTMLHeadingElement | null>(null);
  const rail = useRef<HTMLOListElement | null>(null);
  const moved = useRef(false);
  useEffect(() => {
    // When the rail is a scrolling strip (narrow screens), keep the current step centred in it; this never scrolls the page.
    const strip = rail.current, item = strip?.querySelector<HTMLElement>('[aria-current=step]');
    if (strip && item && strip.scrollWidth > strip.clientWidth) {
      const box = strip.getBoundingClientRect(), spot = item.getBoundingClientRect();
      strip.scrollLeft += spot.left - box.left - (box.width - spot.width) / 2;
    }
    if (!moved.current) return;
    heading.current?.focus();
    heading.current?.scrollIntoView({ block: 'nearest' });
  }, [active]);
  const go = (index: number) => { moved.current = true; setActive(index); };

  // A module inside a step: its status, what to have ready, its key features and anything its checks found.
  const moduleSection = (module: Module) => {
    const state = statusOf(module);
    const issues = moduleFindings(module).filter(f => f.result === 'Fail' || f.result === 'Unknown').sort(byPriority);
    return <section className={`rgaa-step-module rgaa-step-module--${state.state}`} key={module.id} aria-labelledby={`rgaa-step-module-${module.id}`}>
      <header className="rgaa-module__head">
        <h4 id={`rgaa-step-module-${module.id}`}>{t(module.labelKey)} <DocsLink path={module.docsPath} ui={ui} subject={t(module.labelKey)} compact /></h4>
        <span className="rgaa-caps__side"><CommercialChip module={module} features={features} access={access} ui={ui} t={t} /><ModuleStatusChip state={state.state} ui={ui} /></span>
      </header>
      <p className="rgaa-step-module__ready"><i className="fa fa-flag-checkered" aria-hidden="true" /> <strong>{ui('BeforeYouStart')}:</strong> {t(module.adoptionKey)}</p>
      {state.measurable > 0 && <div className="rgaa-progress-line"><ProgressBar value={state.setUp} total={state.measurable} label={format(ui('FeaturesSetUp'), state.setUp, state.measurable)} />
        <span className="rgaa-small">{format(ui('FeaturesSetUp'), state.setUp, state.measurable)}</span></div>}
      <ul className="rgaa-features">{keyFeatures(features, module.id).map(featureRow)}</ul>
      {issues.length > 0 && <div className="rgaa-step-module__checks">
        <h5 className="rgaa-section-title">{ui('StepChecks')}</h5>
        <ul className="rgaa-findings">{issues.map(f => <FindingRow key={f.ruleId} finding={f} t={t} ui={ui} link={url => props.localLink(url)} />)}</ul>
      </div>}
    </section>;
  };

  return <div className="rgaa-wizard">
    <nav className="rgaa-journey" aria-label={ui('JourneyTitle')}>
      <div className="rgaa-journey__head">
        <span className="rgaa-journey__title">{ui('JourneyTitle')}</span>
        <span className="rgaa-small rgaa-muted">{format(ui('StepsDone'), done, steps.length)}</span>
        <span className="rgaa-journey__track" aria-hidden="true">{statuses.map((s, index) => <span key={index} className={`rgaa-journey__seg rgaa-journey__seg--${s.status}${index === active ? ' rgaa-journey__seg--active' : ''}`} />)}</span>
      </div>
      <ol className="rgaa-steps" ref={rail}>{steps.map((s, index) => {
        const st = statuses[index];
        return <li key={s.id} className={`rgaa-steps__item rgaa-steps__item--${st.status}`}><button type="button" className="rgaa-plain" aria-current={index === active ? 'step' : undefined} onClick={() => go(index)}>
          <span className={`rgaa-step__num rgaa-step__num--${st.status}`} aria-hidden="true">{statusIcon[st.status] ? <i className={`fa ${statusIcon[st.status]}`} /> : s.id}</span>
          <span className="rgaa-step__text"><span className="rgaa-step__title">{ui(`Journey${s.id}Title`)}</span><span className="rgaa-step__status">{st.text}</span></span>
        </button></li>;
      })}</ol>
    </nav>
    <section className="ibox rgaa-step-panel" aria-labelledby="rgaa-step-title">
      <div className="ibox-title rgaa-step-panel__head">
        <div className="rgaa-step-panel__heading">
          <span className="rgaa-step-panel__eyebrow">{format(ui('StepOf'), step.id, steps.length)}</span>
          <h3 id="rgaa-step-title" ref={heading} tabIndex={-1}>{ui(`Journey${step.id}Title`)}</h3>
        </div>
        <Chip tone={statusTone[current.status]} icon={statusIcon[current.status] ? statusIcon[current.status] : undefined}>{current.text}</Chip>
      </div>
      <div className="ibox-content">
        <p className="rgaa-lead">{ui(`Journey${step.id}Help`)}</p>
        {step.id === 1 && <>
          <span className="rgaa-field-label" id="rgaa-mode-label">{ui('Mode')}</span>
          <div className="rgaa-choice-cards" role="group" aria-labelledby="rgaa-mode-label">
            {modes.map(mode => <button type="button" key={mode.value} className="rgaa-plain rgaa-choice-card" aria-pressed={props.mode === mode.value}
              disabled={props.busy || !props.canSetup} onClick={() => { if (props.mode !== mode.value) props.save('mode', null, mode.value); }}>
              <i className={`fa ${mode.icon}`} aria-hidden="true" /><span>{ui(mode.value)}</span>
              {props.mode === mode.value && <i className="fa fa-check-circle rgaa-choice-card__tick" aria-hidden="true" />}
            </button>)}
          </div>
          {props.mode === 'Import' && <div className="alert alert-info rgaa-import-note">
            <p>{ui('ImportHelp')}</p>
            {importLink && <a className="btn btn-primary btn-sm" href={importLink}><i className="fa fa-cloud-upload" aria-hidden="true" /> {ui('OpenImport')}</a>}
          </div>}
          <p className="help-block rgaa-journey-help">{ui('JourneyHelp')}</p>
          <div className="rgaa-tier-overview">{tiers.map(tier => <div key={tier} className={`rgaa-tier-card rgaa-tier-card--${tier}`}>
            <h4>{ui(`Tier.${tier}`)}</h4><p className="rgaa-small rgaa-muted">{ui(`TierHelp.${tier}`)}</p>
            <ul className="rgaa-tier-card__modules">{modules.filter(m => m.tier === tier).map(m => <li key={m.id}>{t(m.labelKey)}</li>)}</ul>
          </div>)}</div>
          <p className="rgaa-muted rgaa-small"><i className="fa fa-clock-o" aria-hidden="true" /> {ui('JourneyEffort')}</p>
          <p className="rgaa-muted rgaa-small"><i className="fa fa-info-circle" aria-hidden="true" /> {ui('NoAutomaticActions')}</p>
        </>}
        {step.id === 2 && <>
          <ul className="rgaa-features">{features.filter(f => f.id === 'department-settings').map(featureRow)}</ul>
          <div className={`alert ${props.profileKnown && props.selectedPackIds.length > 0 ? 'alert-success' : 'alert-warning'}`}>
            <i className={`fa ${props.profileKnown && props.selectedPackIds.length > 0 ? 'fa-check-circle' : 'fa-exclamation-triangle'}`} aria-hidden="true" />{' '}
            {props.profileKnown ? (props.selectedPackIds.length > 0 ? <>{ui('ProfileRecorded')}: {props.packs.filter(p => props.selectedPackIds.includes(p.id)).map(p => t(p.labelKey)).join(', ')}</> : ui('ProfileMissing')) : ui('ProfileUnavailable')}
          </div>
          <p>{ui('ProfileHelp')}</p>
          {props.localLink('/User/Department/OperatingProfile') && <p><a className="btn btn-primary btn-sm" href={props.localLink('/User/Department/OperatingProfile')}><i className="fa fa-pencil" aria-hidden="true" /> {ui('OperatingProfile')}</a></p>}
        </>}
        {step.id === 3 && <>
          <div className="rgaa-progress-line rgaa-progress-line--lead"><ProgressBar value={chosen} total={modules.length} label={format(ui('AreasChosen'), chosen, modules.length)} /><span className="rgaa-small">{format(ui('AreasChosen'), chosen, modules.length)}</span></div>
          <p className="help-block">{ui('ScopeHelp')}</p>
          {tiers.map(tier => <section key={tier} aria-labelledby={`rgaa-choose-${tier}`}>
            <div className="rgaa-area-heading"><h4 id={`rgaa-choose-${tier}`}>{ui(`Tier.${tier}`)}</h4><span className="rgaa-muted rgaa-small">{ui(`TierHelp.${tier}`)}</span></div>
            <ul className="rgaa-scope-list">{modules.filter(m => m.tier === tier).map(module => {
              const choice = module.id === 'security' ? 'UseNow' : choices[module.id];
              return <li key={module.id} className={`rgaa-scope-row${choice ? ` rgaa-scope-row--${choice}` : ''}`}>
                <div className="rgaa-scope-row__main">
                  <span className="rgaa-scope-row__title">
                    <span className="rgaa-scope-row__name">{t(module.labelKey)}
                      {props.suggestedAreas.has(module.id) && <span className="rgaa-module-tile__star" title={ui('SuggestedArea')}> <i className="fa fa-star" aria-hidden="true" /><span className="sr-only">{ui('SuggestedArea')}</span></span>}</span>
                    <CommercialChip module={module} features={features} access={access} ui={ui} t={t} />{module.global && <Chip tone="info" icon="fa-globe">{ui('GlobalModule')}</Chip>}
                  </span>
                  <span className="rgaa-scope-row__purpose">{t(module.purposeKey)}</span>
                </div>
                <div className="rgaa-scope-row__side">
                  <AreaSetupChoice key={`${module.id}:${choices[module.id] ?? ''}:${props.reasons?.[module.id] ?? ''}`} areaId={module.id} label={t(module.labelKey)} compact
                    choice={choice} reason={props.reasons?.[module.id]} disabled={props.busy || !props.canSetup} t={t} save={(value, reason) => props.save('area', module.id, value, reason)} />
                  <button type="button" className="btn btn-link btn-xs" onClick={() => props.show('explore', t(module.labelKey))}><i className="fa fa-compass" aria-hidden="true" /> {ui('ExploreThisArea')}</button>
                </div>
              </li>;
            })}</ul>
          </section>)}
        </>}
        {(step.id === 4 || step.id === 5 || step.id === 6) && <>
          {step.modules(modules, choices).filter(m => inScope(m, choices)).map(moduleSection)}
          {step.modules(modules, choices).filter(m => inScope(m, choices)).length === 0 && <div className="rgaa-empty">
            <span className="rgaa-ask__empty-icon" aria-hidden="true"><i className="fa fa-th-large" /></span>
            <p>{ui('NoModulesChosen')}</p>
            <button type="button" className="btn btn-white btn-sm" onClick={() => go(2)}><i className="fa fa-sliders" aria-hidden="true" /> {ui('Journey3Title')}</button>
          </div>}
        </>}
        {step.id === 7 && <>
          <div className="rgaa-module-grid">{step.modules(modules, choices).map(module => <ModuleCard key={module.id} module={module} features={features} t={t} ui={ui}
            access={access} setup={props.capabilitySetup} findings={moduleFindings(module)} choice={choices[module.id]} learned={props.learned} canLearn={props.canSetup}
            busy={props.busy} onLearn={(id, value) => props.save('learn', id, String(value))} link={props.localLink} showStatus={false}
            scopeControl={props.canSetup && module.tier === 'AddOn' ? <div className="rgaa-module__scope"><AreaSetupChoice key={`${module.id}:${choices[module.id] ?? ''}:${props.reasons?.[module.id] ?? ''}`}
              areaId={module.id} label={t(module.labelKey)} choice={choices[module.id]} reason={props.reasons?.[module.id]} disabled={props.busy} t={t}
              save={(choice, reason) => props.save('area', module.id, choice, reason)} /></div> : undefined} />)}</div>
          <p className="rgaa-small rgaa-muted"><i className="fa fa-info-circle" aria-hidden="true" /> {ui('Optional')}</p>
        </>}
        {step.id === 8 && <>
          {props.summary}
          <ul className="rgaa-features">{features.filter(f => f.id === 'communication-tests').map(featureRow)}</ul>
          <p><button type="button" className="btn btn-primary btn-sm" onClick={() => props.show('report')}><i className="fa fa-clipboard" aria-hidden="true" /> {ui('report')}</button></p>
        </>}
        {step.id === 9 && props.reviewPanel}
        <div className="rgaa-step-nav">
          <button type="button" className="btn btn-white btn-sm" disabled={active === 0} onClick={() => go(active - 1)}><i className="fa fa-chevron-left" aria-hidden="true" /> {ui('PreviousStep')}</button>
          {active < steps.length - 1 && <button type="button" className="btn btn-primary btn-sm rgaa-step-nav__next" onClick={() => go(active + 1)}>
            <span className="rgaa-step-nav__label">{ui('NextStep')}</span><span className="rgaa-step-nav__title">{ui(`Journey${steps[active + 1].id}Title`)}</span> <i className="fa fa-chevron-right" aria-hidden="true" /></button>}
        </div>
      </div>
    </section>
  </div>;
}
