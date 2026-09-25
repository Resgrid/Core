import { Chip, format, type Area, type Translate } from './SetupVisuals';

export type SetupPlan = {
  tasks: { id: string; capabilityId: string; labelKey: string; guidanceKey: string; state: string; destination: string | null; ruleIds: string[] }[];
  effort: { areaId: string; minimumMinutes: number; maximumMinutes: number; assumptionKey: string }[];
  revisitDue: boolean; revisitOnUtc: string | null;
};
type Task = SetupPlan['tasks'][number];

const done = new Set(['Learned', 'AccessAvailable', 'ConfigurationPresent', 'ChecksPassed']);
const stepIcon: Record<string, string> = { Learn: 'fa-book', Prerequisites: 'fa-key', Configure: 'fa-cog', Verify: 'fa-check' };
const pipTone = (state: string) => done.has(state) ? 'done' : state === 'Blocked' ? 'blocked' : state === 'Unknown' ? 'unknown' : 'pending';

/** Learn → prerequisites → configure → verify per selected feature, grouped by area. Status is visible without expanding. */
export default function SetupChecklist({ plan, t, capabilities, areas, localLink }: {
  plan: SetupPlan; t: Translate;
  capabilities: { id: string; areaId: string; labelKey: string }[]; areas: Area[];
  localLink: (url: string | null | undefined) => string | undefined;
}) {
  const ui = (key: string) => t(`Ui.${key}`);
  const byCapability = new Map<string, Task[]>();
  for (const task of plan.tasks) byCapability.set(task.capabilityId, [...(byCapability.get(task.capabilityId) ?? []), task]);
  const orderedAreas = [...areas].sort((a, b) => (a.order ?? 0) - (b.order ?? 0))
    .filter(area => capabilities.some(c => c.areaId === area.id && byCapability.has(c.id)));
  if (orderedAreas.length === 0) return null;
  return <section aria-label={ui('SetupChecklist')}>
    <p className="rgaa-lead">{ui('ChecklistHelp')}</p>
    {plan.revisitDue && <div className="alert alert-warning" role="status"><i className="fa fa-calendar" aria-hidden="true" /> {ui('RevisitDue')}</div>}
    <ul className="rgaa-legend" aria-label={ui('ChecklistLegend')}>
      {['Learn', 'Prerequisites', 'Configure', 'Verify'].map(step => <li key={step}><i className={`fa ${stepIcon[step]}`} aria-hidden="true" />{t(`Ui.SetupTask.${step}`)}</li>)}
    </ul>
    {orderedAreas.map((area, index) => {
      const areaCapabilities = capabilities.filter(c => c.areaId === area.id && byCapability.has(c.id));
      const tasks = areaCapabilities.flatMap(c => byCapability.get(c.id) ?? []);
      const complete = tasks.filter(task => done.has(task.state)).length;
      const blocked = tasks.some(task => task.state === 'Blocked');
      const effort = plan.effort.find(e => e.areaId === area.id);
      return <details className="rgaa-area-group" key={area.id} open={index === 0}>
        <summary>
          <strong>{t(area.labelKey)}</strong>
          <span className="rgaa-bar" role="img" aria-label={format(ui('StepsDone'), complete, tasks.length)}><span className="rgaa-bar__ok" style={{ width: `${tasks.length ? complete / tasks.length * 100 : 0}%` }} /></span>
          <span className="rgaa-small rgaa-muted">{format(ui('StepsDone'), complete, tasks.length)}</span>
          {blocked && <Chip tone="fail" icon="fa-lock">{ui('TaskState.Blocked')}</Chip>}
          {effort && <span className="rgaa-small rgaa-muted"><i className="fa fa-clock-o" aria-hidden="true" /> {effort.minimumMinutes}–{effort.maximumMinutes} {ui('PlanningMinutes')}</span>}
        </summary>
        <ul className="rgaa-caps">{areaCapabilities.map(capability => {
          const steps = byCapability.get(capability.id) ?? [];
          const destination = localLink(steps.find(s => s.destination)?.destination);
          return <li key={capability.id}>
            <span className="rgaa-caps__name">{t(capability.labelKey)}</span>
            <span className="rgaa-caps__side">
              <span className="rgaa-pips">{steps.map(step => {
                const name = step.id.slice(step.id.lastIndexOf('.') + 1);
                const label = `${t(step.labelKey)}: ${t(`Ui.TaskState.${step.state}`)}`;
                return <span key={step.id} className={`rgaa-pip rgaa-pip--${pipTone(step.state)}`} role="img" aria-label={label} title={label}><i className={`fa ${stepIcon[name] ?? 'fa-circle'}`} aria-hidden="true" /></span>;
              })}</span>
              {destination && <a className="btn btn-white btn-xs" href={destination}>{ui('Configure')}</a>}
            </span>
            <details className="rgaa-cap-detail"><summary className="rgaa-muted">{ui('StepDetails')}</summary>
              <ol>{steps.map(step => <li key={step.id}><strong>{t(step.labelKey)}</strong> · {t(`Ui.TaskState.${step.state}`)}<br /><span className="rgaa-muted">{t(step.guidanceKey)}</span></li>)}</ol>
            </details>
          </li>;
        })}</ul>
      </details>;
    })}
    <p className="rgaa-small rgaa-muted">{ui('EffortAssumptions')}</p>
  </section>;
}
