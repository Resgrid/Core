const steps = [
  { id: 1, capabilities: ['dashboard','dispatch-app','responder-app','unit-app','incident-command','big-board'] },
  { id: 2, capabilities: ['department-settings'] },
  { id: 3, capabilities: [] },
  { id: 4, capabilities: ['personnel','groups','personnel-roles','permissions','two-factor','certification-dashboard'] },
  { id: 5, capabilities: ['units','custom-statuses','call-types-priorities','dispatch-settings','email-intake','text-intake','notifications','mapping'] },
  { id: 6, capabilities: ['shifts','trainings','checklists','inventory','records','contacts','workflows'] },
  { id: 7, capabilities: [] },
  { id: 8, capabilities: ['communication-tests'] },
  { id: 9, capabilities: ['documents','trainings'] },
];

export default function SetupJourney({ t, capabilities, access, localLink, show, showAddons, learned, total }: {
  t: (key: string) => string; capabilities: { id: string; labelKey: string; adoptionKey: string }[];
  access: { capabilityId: string; canConfigure: boolean; destination: string | null }[];
  localLink: (url: string | null | undefined) => string | undefined;
  show: (page: string) => void; showAddons: () => void; learned: number; total: number;
}) {
  return <section aria-label={t('Ui.JourneyTitle')}><h3>{t('Ui.JourneyTitle')}</h3><p>{t('Ui.JourneyHelp')}</p><p>{t('Ui.EffortAssumptions')}</p>
    <ol className="rgaa-journey">{steps.map(step => <li key={step.id}><details>
      <summary>{t(`Ui.Journey${step.id}Title`)}</summary><p>{t(`Ui.Journey${step.id}Help`)}</p>
      {step.id === 2 && <p><a href={localLink("/User/Department/OperatingProfile")}>{t('Ui.OperatingProfile')}</a></p>}
      {step.id === 3 && <><p>{learned} / {total} {t('Ui.Learning')}</p><button type="button" onClick={() => show('explore')}>{t('Ui.explore')}</button></>}
      {step.id === 6 && <button type="button" onClick={() => show('report')}>{t('Ui.SelectedWorkflows')}</button>}
      {step.id === 7 && <button type="button" onClick={showAddons}>{t('Ui.OptionalCapabilities')}</button>}
      {step.id >= 8 && <button type="button" onClick={() => show('report')}>{t('Ui.report')}</button>}
      {step.capabilities.length > 0 && <ul>{step.capabilities.map(id => {
        const capability = capabilities.find(c => c.id === id); const available = access.find(a => a.capabilityId === id);
        if (!capability) return null;
        return <li key={id}>{available?.canConfigure && localLink(available.destination) ? <a href={localLink(available.destination)}>{t(capability.labelKey)}</a> : <span>{t(capability.labelKey)} · {t('Ui.SourceAccessUnavailable')}</span>}
          <p>{t(capability.adoptionKey)}</p></li>;
      })}</ul>}
    </details></li>)}</ol>
  </section>;
}
