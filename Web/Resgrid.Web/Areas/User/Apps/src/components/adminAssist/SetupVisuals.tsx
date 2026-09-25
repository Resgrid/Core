import { useId, type ReactNode } from 'react';

export type Translate = (key: string) => string;
export type Finding = { ruleId: string; areaId: string; severity: string; result: string; titleKey: string; explanationKey: string; nextActionKey: string; destination: string; reasonCode: string | null; scopeIndependent: boolean };
export type Area = { id: string; labelKey: string; purposeKey: string; order?: number };
export type Counts = { ok: number; fail: number; unknown: number; total: number };

/** Replaces {0}, {1}… in a localized template; resource text stays translatable as a whole sentence. */
export const format = (template: string, ...values: (string | number)[]) =>
  values.reduce<string>((text, value, index) => text.split(`{${index}}`).join(String(value)), template);

const severityRank: Record<string, number> = { Critical: 0, Warning: 1, Information: 2 };
const resultRank: Record<string, number> = { Fail: 0, Unknown: 1, Suggestion: 2, Pass: 3, NotApplicable: 4 };
/** Information-level results are review suggestions: listed, but never counted as failed, unknown or required checks. */
export const isSuggestion = (f: Finding) => f.severity === 'Information' && (f.result === 'Fail' || f.result === 'Unknown');
const displayResult = (f: Finding) => isSuggestion(f) ? 'Suggestion' : f.result;
export const byPriority = (a: Finding, b: Finding) =>
  (resultRank[displayResult(a)] ?? 9) - (resultRank[displayResult(b)] ?? 9) || (severityRank[a.severity] ?? 9) - (severityRank[b.severity] ?? 9);

/** Mirrors the server report: not-applicable and information-level findings are excluded from counts. */
export function countFindings(findings: Finding[]): Counts {
  const applicable = findings.filter(f => f.result !== 'NotApplicable' && f.severity !== 'Information');
  return {
    ok: applicable.filter(f => f.result === 'Pass').length,
    fail: applicable.filter(f => f.result === 'Fail').length,
    unknown: applicable.filter(f => f.result === 'Unknown').length,
    total: applicable.length,
  };
}

const percent = (part: number, total: number) => total > 0 ? `${(part / total) * 100}%` : '0%';

/** Proportions of one stated denominator. The accessible name repeats the counts, so colour is never the only signal. */
export function StatusBar({ counts, label, thin }: { counts: Counts; label: string; thin?: boolean }) {
  return <div className={`rgaa-bar${thin ? ' rgaa-bar--thin' : ''}`} role="img" aria-label={label} title={label}>
    {counts.ok > 0 && <span className="rgaa-bar__ok" style={{ width: percent(counts.ok, counts.total) }} />}
    {counts.fail > 0 && <span className="rgaa-bar__fail" style={{ width: percent(counts.fail, counts.total) }} />}
    {counts.unknown > 0 && <span className="rgaa-bar__unknown" style={{ width: percent(counts.unknown, counts.total) }} />}
  </div>;
}

export function ProgressBar({ value, total, label }: { value: number; total: number; label: string }) {
  return <div className="rgaa-bar rgaa-bar--thin" role="img" aria-label={label} title={label}>
    {value > 0 && <span className="rgaa-bar__info" style={{ width: percent(Math.min(value, total), total) }} />}
  </div>;
}

export function Chip({ tone, icon, children, title }: { tone: 'ok' | 'fail' | 'unknown' | 'info' | 'muted' | 'critical'; icon?: string; children: ReactNode; title?: string }) {
  return <span className={`rgaa-chip rgaa-chip--${tone}`} title={title}>{icon && <i className={`fa ${icon}`} aria-hidden="true" />}{children}</span>;
}

const resultVisual: Record<string, { icon: string; tone: 'ok' | 'fail' | 'unknown' | 'muted' }> = {
  Pass: { icon: 'fa-check-circle', tone: 'ok' },
  Fail: { icon: 'fa-times-circle', tone: 'fail' },
  Unknown: { icon: 'fa-question-circle', tone: 'unknown' },
  NotApplicable: { icon: 'fa-minus-circle', tone: 'muted' },
  Suggestion: { icon: 'fa-lightbulb-o', tone: 'muted' },
};

export function ResultChip({ result, ui }: { result: string; ui: Translate }) {
  const visual = resultVisual[result] ?? resultVisual.Unknown;
  return <Chip tone={visual.tone} icon={visual.icon}>{ui(result)}</Chip>;
}

export function SeverityChip({ severity, ui }: { severity: string; ui: Translate }) {
  return severity === 'Critical' ? <Chip tone="critical" icon="fa-exclamation-triangle">{ui(severity)}</Chip>
    : <Chip tone={severity === 'Warning' ? 'unknown' : 'info'}>{ui(severity)}</Chip>;
}

export function StatusLegend({ counts, ui }: { counts: Counts; ui: Translate }) {
  return <ul className="rgaa-legend">
    <li><i className="fa fa-check-circle rgaa-icon--ok" aria-hidden="true" />{format(ui('LegendVerified'), counts.ok)}</li>
    <li><i className="fa fa-times-circle rgaa-icon--fail" aria-hidden="true" />{format(ui('LegendAttention'), counts.fail)}</li>
    <li><i className="fa fa-question-circle rgaa-icon--unknown" aria-hidden="true" />{format(ui('LegendUnknown'), counts.unknown)}</li>
  </ul>;
}

export const countsLabel = (counts: Counts, ui: Translate) => format(ui('ChecksSummary'), counts.ok, counts.fail, counts.unknown, counts.total);

/** Separate widgets per dimension: configuration checks and personal learning are never blended into one score. */
export function MetricRow({ counts, critical, learned, totalCapabilities, ui }: { counts: Counts; critical: number; learned: number; totalCapabilities: number; ui: Translate }) {
  return <div className="rgaa-metrics">
    <div className="rgaa-metric rgaa-metric--ok">
      <span className="rgaa-metric__label">{ui('Checks')}</span>
      <span className="rgaa-metric__value">{counts.ok} <small>/ {counts.total}</small></span>
      <StatusBar counts={counts} label={countsLabel(counts, ui)} />
      <StatusLegend counts={counts} ui={ui} />
    </div>
    <div className={`rgaa-metric ${counts.fail > 0 ? 'rgaa-metric--fail' : 'rgaa-metric--ok'}`}>
      <span className="rgaa-metric__label">{ui('Failures')}</span>
      <span className="rgaa-metric__value">{counts.fail}</span>
      <span className="rgaa-metric__note">{critical > 0 ? <Chip tone="critical" icon="fa-exclamation-triangle">{format(ui('CriticalCount'), critical)}</Chip> : ui('NoCriticalFailures')}</span>
    </div>
    <div className={`rgaa-metric ${counts.unknown > 0 ? 'rgaa-metric--unknown' : 'rgaa-metric--ok'}`}>
      <span className="rgaa-metric__label">{ui('UnknownChecks')}</span>
      <span className="rgaa-metric__value">{counts.unknown}</span>
      <span className="rgaa-metric__note">{ui('UnknownMeaning')}</span>
    </div>
    <div className="rgaa-metric rgaa-metric--info">
      <span className="rgaa-metric__label">{ui('Learning')}</span>
      <span className="rgaa-metric__value">{learned} <small>/ {totalCapabilities}</small></span>
      <ProgressBar value={learned} total={totalCapabilities} label={format(ui('LearningSummary'), learned, totalCapabilities)} />
      <span className="rgaa-metric__note">{ui('LearningSeparate')}</span>
    </div>
  </div>;
}

export function FindingRow({ finding, t, ui, areaLabel, link, onPlan }: { finding: Finding; t: Translate; ui: Translate; areaLabel?: string; link: (url: string) => string | undefined; onPlan?: (finding: Finding) => void }) {
  const visual = resultVisual[displayResult(finding)] ?? resultVisual.Unknown;
  const destination = link(finding.destination);
  return <li className="rgaa-finding">
    <span className={`rgaa-finding__icon rgaa-icon--${visual.tone}`}><i className={`fa ${visual.icon}`} aria-hidden="true" /></span>
    <div className="rgaa-finding__body">
      <div className="rgaa-finding__meta">{isSuggestion(finding) ? <Chip tone="info" icon="fa-lightbulb-o">{ui('Suggestion')}</Chip> : <><ResultChip result={finding.result} ui={ui} /><SeverityChip severity={finding.severity} ui={ui} /></>}{areaLabel && <span className="rgaa-muted rgaa-small">{areaLabel}</span>}</div>
      <p className="rgaa-finding__title">{t(finding.titleKey)}</p>
      <p>{t(finding.explanationKey)}</p>
      <div className="rgaa-finding__actions">
        {destination && <a className="btn btn-white btn-xs" href={destination}><i className="fa fa-external-link" aria-hidden="true" /> {t(finding.nextActionKey)}</a>}
        {onPlan && <button type="button" className="btn btn-white btn-xs" onClick={() => onPlan(finding)}>{t('Plan.New')}</button>}
      </div>
    </div>
  </li>;
}

/** Findings grouped by result, most urgent first. Verified and not-applicable groups start collapsed. */
export function FindingGroups({ findings, t, ui, areas, link, onPlan, showArea = true }: { findings: Finding[]; t: Translate; ui: Translate; areas: Area[]; link: (url: string) => string | undefined; onPlan?: (finding: Finding) => void; showArea?: boolean }) {
  const areaLabel = (id: string) => showArea ? t(areas.find(a => a.id === id)?.labelKey ?? id) : undefined;
  const groups: { result: string; titleKey: string; open: boolean }[] = [
    { result: 'Fail', titleKey: 'Fail', open: true },
    { result: 'Unknown', titleKey: 'GroupUnknown', open: true },
    { result: 'Suggestion', titleKey: 'GroupSuggestions', open: true },
    { result: 'Pass', titleKey: 'Pass', open: false },
    { result: 'NotApplicable', titleKey: 'GroupNotApplicable', open: false },
  ];
  const sorted = [...findings].sort(byPriority);
  if (sorted.length === 0) return <p className="rgaa-muted">{ui('None')}</p>;
  return <>{groups.map(group => {
    const items = sorted.filter(f => displayResult(f) === group.result);
    if (items.length === 0) return null;
    const visual = resultVisual[group.result];
    return <details className="rgaa-group" key={group.result} open={group.open}>
      <summary><i className={`fa ${visual.icon} rgaa-icon--${visual.tone}`} aria-hidden="true" /> {ui(group.titleKey)} <span className="rgaa-group__count">({items.length})</span></summary>
      <ul className="rgaa-findings">{items.map(f => <FindingRow key={f.ruleId} finding={f} t={t} ui={ui} areaLabel={areaLabel(f.areaId)} link={link} onPlan={onPlan} />)}</ul>
    </details>;
  })}</>;
}

export function Ibox({ title, tools, children }: { title: ReactNode; tools?: ReactNode; children: ReactNode }) {
  const headingId = useId();
  return <section className="ibox" aria-labelledby={headingId}>
    <div className="ibox-title"><h5 id={headingId}>{title}</h5>{tools && <div className="rgaa-ibox-tools">{tools}</div>}</div>
    <div className="ibox-content">{children}</div>
  </section>;
}
