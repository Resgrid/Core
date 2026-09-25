import type { ReactNode } from 'react';
import { Chip, ProgressBar, StatusBar, countFindings, countsLabel, format, type Area, type Finding, type Translate } from './SetupVisuals';

/** A product module (catalog "area"): the unit Setup Wizard, Setup Report and Explore teach. */
export type Module = Area & { tier: string; addon: string | null; global: boolean; valueKey: string; exampleKey: string; adoptionKey: string; minimumMinutes: number; maximumMinutes: number; docsPath?: string | null };
export type Feature = { id: string; areaId: string; labelKey: string; purposeKey: string; adoptionKey: string; prominence?: string; releaseStatus: string; requirements: { kind: string; id: string }[]; docsPath?: string | null };
export type Access = { capabilityId: string; state: string; reasonCodes: string[]; canConfigure: boolean; subscriptionDestination: string | null; destination: string | null };
export type Setup = { capabilityId: string; state: string };
export type Choice = 'UseNow' | 'LearnLater' | 'NotApplicable';
export type ModuleState = 'Done' | 'Attention' | 'Progress' | 'NotStarted' | 'Unknown';

export const tiers = ['Core', 'Recommended', 'Optional', 'AddOn'] as const;
export const isKey = (feature: Feature) => feature.prominence === 'Key';
export const keyFeatures = (features: Feature[], moduleId: string) => features.filter(f => f.areaId === moduleId && isKey(f));
export const inScope = (module: Module, choices: Record<string, Choice>) => module.id === 'security' || choices[module.id] === 'UseNow';
const recorded = new Set(['ConfigurationPresent', 'ChecksPassed', 'NeedsAttention']);

// Catalog entries carry only a path; the origin is fixed so catalog data can never point somewhere else.
const docsOrigin = 'https://docs.resgrid.com';
export const docsUrl = (path: string | null | undefined) => path && /^\/[a-z0-9-]+(?:\/[a-z0-9-]+)*\/(?:#[a-z0-9-]+)?$/.test(path) ? docsOrigin + path : undefined;

/** Opens the public documentation in a new tab; icon-only form for dense rows keeps a full accessible name. */
export function DocsLink({ path, ui, subject, compact }: { path: string | null | undefined; ui: Translate; subject: string; compact?: boolean }) {
  const url = docsUrl(path);
  if (!url) return null;
  const name = `${format(ui('DocumentationFor'), subject)} ${ui('OpensInNewTab')}`;
  return <a className={compact ? 'rgaa-docs-link rgaa-docs-link--compact' : 'rgaa-docs-link'} href={url} target="_blank" rel="noopener noreferrer" title={name} aria-label={name}>
    <i className="fa fa-book" aria-hidden="true" />{!compact && <> {ui('Documentation')}</>}
  </a>;
}

/**
 * Status from evidence only: recorded setup of key features that have a supported count, plus the module's checks.
 * Reading about a module or buying an add-on never changes it.
 */
export function moduleStatus(module: Module, features: Feature[], setup: Setup[], findings: Finding[]) {
  const keys = keyFeatures(features, module.id);
  const states = keys.map(f => setup.find(s => s.capabilityId === f.id)?.state ?? 'NotAssessed');
  const measurable = states.filter(s => s !== 'NotAssessed').length;
  const setUp = states.filter(s => recorded.has(s)).length;
  const counts = countFindings(findings.filter(f => f.areaId === module.id));
  const state: ModuleState = counts.fail > 0 || states.includes('NeedsAttention') ? 'Attention'
    : measurable === 0 ? (counts.total > 0 && counts.unknown === 0 ? 'Done' : 'Unknown')
    : setUp === measurable && counts.unknown === 0 ? 'Done'
    : setUp > 0 ? 'Progress' : 'NotStarted';
  return { state, setUp, measurable, counts };
}

const stateTone: Record<ModuleState, 'ok' | 'fail' | 'unknown' | 'muted' | 'info'> = { Done: 'ok', Attention: 'fail', Progress: 'unknown', NotStarted: 'muted', Unknown: 'muted' };
const stateIcon: Record<ModuleState, string> = { Done: 'fa-check', Attention: 'fa-exclamation-triangle', Progress: 'fa-adjust', NotStarted: 'fa-circle-o', Unknown: 'fa-question' };
export function ModuleStatusChip({ state, ui }: { state: ModuleState; ui: Translate }) {
  return <Chip tone={stateTone[state]} icon={stateIcon[state]}>{ui(`ModuleStatus.${state}`)}</Chip>;
}

/** Included, or the add-on it needs with the department's current entitlement when known. */
export function CommercialChip({ module, features, access, ui, t }: { module: Module; features: Feature[]; access: Access[]; ui: Translate; t: Translate }) {
  if (!module.addon) return <Chip tone="muted">{ui('Included')}</Chip>;
  const card = features.find(f => f.areaId === module.id && f.id.startsWith('addon-'));
  const state = access.find(a => a.capabilityId === card?.id)?.state;
  if (state === 'Known') return <Chip tone="ok" icon="fa-check">{ui('AddonActive')}</Chip>;
  const label = t(`Ui.AddonRequired.${module.addon}`);
  return <Chip tone="info" icon="fa-tag">{label.startsWith('Ui.') ? ui('AddonBadge') : label}</Chip>;
}

const setupTone: Record<string, 'ok' | 'fail' | 'unknown' | 'muted'> = { ChecksPassed: 'ok', ConfigurationPresent: 'ok', NeedsAttention: 'fail', NotConfigured: 'unknown' };

/** One key feature: what it is, the first thing to do, recorded setup and a link to its own screen. */
export function FeatureRow({ feature, t, ui, access, setupState, learned, canLearn, busy, onLearn, link }: {
  feature: Feature; t: Translate; ui: Translate; access?: Access; setupState?: string; learned: boolean; canLearn: boolean; busy: boolean;
  onLearn: (learned: boolean) => void; link: (url: string | null | undefined) => string | undefined;
}) {
  const destination = access?.canConfigure ? link(access.destination) : undefined;
  const blocked = access && access.state !== 'Known';
  return <li className="rgaa-feature-row">
    <div className="rgaa-feature-row__main">
      <span className="rgaa-caps__name">{t(feature.labelKey)}</span> <DocsLink path={feature.docsPath} ui={ui} subject={t(feature.labelKey)} compact />
      {feature.releaseStatus !== 'available' && <> <Chip tone="unknown">{ui(`Feature${feature.releaseStatus}`)}</Chip></>}
      <span className="rgaa-feature-row__text">{t(feature.purposeKey)}</span>
      <span className="rgaa-feature-row__step"><i className="fa fa-hand-o-right" aria-hidden="true" /> {t(feature.adoptionKey)}</span>
    </div>
    <div className="rgaa-caps__side">
      {setupState && setupTone[setupState] && <Chip tone={setupTone[setupState]}>{ui(`SetupState.${setupState}`)}</Chip>}
      {blocked && <Chip tone={access!.state === 'Unavailable' ? 'info' : 'unknown'} icon="fa-lock">{access!.reasonCodes.length > 0 ? access!.reasonCodes.map(r => ui(r)).join(' · ') : ui('AvailabilityUnknown')}</Chip>}
      {canLearn && <label className="rgaa-check rgaa-check--inline"><input type="checkbox" disabled={busy} checked={learned} onChange={e => onLearn(e.target.checked)} />{ui('Learned')}</label>}
      {destination && <a className="btn btn-white btn-xs" href={destination}><i className="fa fa-external-link" aria-hidden="true" /> {ui('Configure')}</a>}
    </div>
  </li>;
}

/** A module with its guidance and key features. Detail features are left to Settings reference and Ask. */
export function ModuleCard({ module, features, t, ui, access, setup, findings, choice, learned, canLearn, busy, onLearn, link, scopeControl, showStatus }: {
  module: Module; features: Feature[]; t: Translate; ui: Translate; access: Access[]; setup: Setup[]; findings: Finding[]; choice: Choice | undefined;
  learned: string[]; canLearn: boolean; busy: boolean; onLearn: (featureId: string, learned: boolean) => void; link: (url: string | null | undefined) => string | undefined;
  scopeControl?: ReactNode; showStatus: boolean;
}) {
  const keys = keyFeatures(features, module.id);
  const details = features.filter(f => f.areaId === module.id && !isKey(f) && !f.id.startsWith('addon-'));
  const status = moduleStatus(module, features, setup, findings);
  const subscription = access.find(a => a.capabilityId === features.find(f => f.areaId === module.id && f.id.startsWith('addon-'))?.id)?.subscriptionDestination;
  return <article className={`rgaa-module rgaa-module--${module.tier}`} aria-labelledby={`rgaa-module-${module.id}`}>
    <header className="rgaa-module__head">
      <h4 id={`rgaa-module-${module.id}`}>{t(module.labelKey)}</h4>
      <span className="rgaa-caps__side">
        <CommercialChip module={module} features={features} access={access} ui={ui} t={t} />
        {module.global && <Chip tone="info" icon="fa-globe">{ui('GlobalModule')}</Chip>}
        {showStatus && choice === 'UseNow' && <ModuleStatusChip state={status.state} ui={ui} />}
        {showStatus && choice !== 'UseNow' && choice && <Chip tone="muted">{ui(choice)}</Chip>}
      </span>
    </header>
    <p>{t(module.purposeKey)} <DocsLink path={module.docsPath} ui={ui} subject={t(module.labelKey)} /></p>
    <dl className="rgaa-module__guide">
      <dt>{ui('WhyItMatters')}</dt><dd>{t(module.valueKey)}</dd>
      <dt>{ui('Example')}</dt><dd className="rgaa-muted">{t(module.exampleKey)}</dd>
      <dt>{ui('BeforeYouStart')}</dt><dd>{t(module.adoptionKey)}</dd>
    </dl>
    {scopeControl}
    <h5 className="rgaa-module__features">{ui('KeyFeatures')}{status.measurable > 0 && <span className="rgaa-muted rgaa-small"> · {format(ui('FeaturesSetUp'), status.setUp, status.measurable)}</span>}</h5>
    {status.measurable > 0 && <ProgressBar value={status.setUp} total={status.measurable} label={format(ui('FeaturesSetUp'), status.setUp, status.measurable)} />}
    <ul className="rgaa-features">{keys.map(feature => <FeatureRow key={feature.id} feature={feature} t={t} ui={ui} access={access.find(a => a.capabilityId === feature.id)}
      setupState={setup.find(s => s.capabilityId === feature.id)?.state} learned={learned.includes(feature.id)} canLearn={canLearn} busy={busy}
      onLearn={value => onLearn(feature.id, value)} link={link} />)}</ul>
    {details.length > 0 && <details className="rgaa-module__more">
      <summary>{format(ui('MoreInModule'), details.length)}</summary>
      <ul className="rgaa-more-list">{details.map(feature => {
        const destination = access.find(a => a.capabilityId === feature.id && a.canConfigure)?.destination;
        return <li key={feature.id}>
          <span className="rgaa-caps__name">{t(feature.labelKey)}</span> <DocsLink path={feature.docsPath} ui={ui} subject={t(feature.labelKey)} compact />
          {link(destination) && <> <a className="rgaa-small" href={link(destination)}>{ui('Open')}</a></>}
          <span className="rgaa-feature-row__text">{t(feature.purposeKey)}</span>
        </li>;
      })}</ul>
      <p className="rgaa-small rgaa-muted"><i className="fa fa-info-circle" aria-hidden="true" /> {ui('AskAboutMore')}</p>
    </details>}
    {subscription && link(subscription) && <footer className="rgaa-module__foot"><a className="btn btn-white btn-xs" href={link(subscription)}>{ui('SubscriptionOptions')}</a></footer>}
  </article>;
}

/** One row per module, grouped by tier: the admin's choice, key-feature setup and check results side by side. */
export function ModuleReadiness({ modules, features, setup, findings, choices, reasons, t, ui, onShowModule }: {
  modules: Module[]; features: Feature[]; setup: Setup[]; findings: Finding[]; choices: Record<string, Choice>; reasons?: Record<string, string>;
  t: Translate; ui: Translate; onShowModule?: (moduleId: string) => void;
}) {
  return <div className="table-responsive"><table className="table table-hover rgaa-areas">
    <thead><tr><th scope="col">{ui('Area')}</th><th scope="col">{ui('SetupChoice')}</th><th scope="col">{ui('KeyFeatures')}</th><th scope="col">{ui('ChecksColumn')}</th><th scope="col"><span className="sr-only">{ui('ViewChecks')}</span></th></tr></thead>
    {tiers.map(tier => {
      const rows = modules.filter(m => m.tier === tier);
      if (rows.length === 0) return null;
      return <tbody key={tier}>
        <tr className="rgaa-tier-row"><th colSpan={5} scope="colgroup">{ui(`Tier.${tier}`)}</th></tr>
        {rows.map(module => {
          const choice: Choice = module.id === 'security' ? 'UseNow' : choices[module.id] ?? 'LearnLater';
          const using = choice === 'UseNow';
          const moduleFindings = findings.filter(f => f.areaId === module.id && (using || f.scopeIndependent));
          const counts = countFindings(moduleFindings);
          const status = moduleStatus(module, features, setup, moduleFindings);
          return <tr key={module.id} className={using ? undefined : 'rgaa-dim'}>
            <th scope="row">{t(module.labelKey)}{module.addon && <> <Chip tone="info" icon="fa-tag">{ui('AddonBadge')}</Chip></>}</th>
            <td><Chip tone={using ? 'ok' : choice === 'NotApplicable' ? 'muted' : 'info'} title={choice === 'NotApplicable' && reasons?.[module.id] ? ui(`ScopeReason.${reasons[module.id]}`) : undefined}>{ui(choice)}</Chip></td>
            <td style={{ minWidth: 130 }}>{using && status.measurable > 0 ? <>
              <ProgressBar value={status.setUp} total={status.measurable} label={format(ui('FeaturesSetUp'), status.setUp, status.measurable)} />
              <span className="rgaa-small">{format(ui('FeaturesSetUp'), status.setUp, status.measurable)}</span>
            </> : using ? <ModuleStatusChip state={status.state} ui={ui} /> : <span className="rgaa-muted rgaa-small">{ui('OutsideScope')}</span>}</td>
            <td style={{ minWidth: 140 }}>{counts.total > 0 ? <>
              <StatusBar counts={counts} label={countsLabel(counts, ui)} />
              <span className="rgaa-small">{countsLabel(counts, ui)}</span>
            </> : <span className="rgaa-muted rgaa-small">{!using ? '' : moduleFindings.length > 0 ? ui('ChecksNotApplicable') : ui('NoAutomatedChecks')}</span>}</td>
            <td className="text-right">{onShowModule && counts.total > 0 && <button type="button" className="btn btn-white btn-xs" onClick={() => onShowModule(module.id)}>{ui('ViewChecks')}</button>}</td>
          </tr>;
        })}
      </tbody>;
    })}
  </table></div>;
}
