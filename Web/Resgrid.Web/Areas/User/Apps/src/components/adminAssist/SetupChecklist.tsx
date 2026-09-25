export type SetupPlan = {
  tasks: { id: string; capabilityId: string; labelKey: string; guidanceKey: string; state: string; destination: string | null; ruleIds: string[] }[];
  effort: { areaId: string; minimumMinutes: number; maximumMinutes: number; assumptionKey: string }[];
  revisitDue: boolean; revisitOnUtc: string | null;
};

export default function SetupChecklist({ plan, t, capabilities, areas, localLink }: {
  plan: SetupPlan; t: (key: string) => string;
  capabilities: { id: string; labelKey: string }[]; areas: { id: string; labelKey: string }[];
  localLink: (url: string | null | undefined) => string | undefined;
}) {
  const ids = [...new Set(plan.tasks.map(task => task.capabilityId))];
  return <section aria-label={t('Ui.SetupChecklist')}>
    <h3>{t('Ui.SetupChecklist')}</h3><p>{t('Ui.EffortAssumptions')}</p>
    {plan.revisitDue && <p role="status"><strong>{t('Ui.RevisitDue')}</strong></p>}
    <ul>{plan.effort.map(e => <li key={e.areaId}>{t(areas.find(a => a.id === e.areaId)?.labelKey ?? 'Ui.Unknown')}: {e.minimumMinutes}–{e.maximumMinutes} {t('Ui.PlanningMinutes')}</li>)}</ul>
    {ids.map(id => <details key={id}><summary>{t(capabilities.find(c => c.id === id)?.labelKey ?? 'Ui.Unknown')}</summary>
      <ol>{plan.tasks.filter(task => task.capabilityId === id).map(task => <li key={task.id}>
        <strong>{t(task.labelKey)}</strong> · {t(`Ui.TaskState.${task.state}`)}
        <p>{t(task.guidanceKey)}</p>
        {localLink(task.destination) && <a href={localLink(task.destination)}>{t('Ui.Configure')}</a>}
      </li>)}</ol>
    </details>)}
  </section>;
}
