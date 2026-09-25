import { useState } from 'react';

type Choice = 'UseNow' | 'LearnLater' | 'NotApplicable';
export default function AreaSetupChoice({ areaId, label, choice, reason, disabled, t, save }: {
  areaId: string; label: string; choice: Choice; reason?: string; disabled: boolean; t: (key: string) => string;
  save: (choice: Choice, reason: string | null) => void;
}) {
  const [selected, setSelected] = useState(choice);
  const [selectedReason, setSelectedReason] = useState(reason ?? '');
  return <form onSubmit={event => { event.preventDefault(); save(selected, selected === 'NotApplicable' ? selectedReason : null); }}>
    <label>{t('Ui.SetupChoice')} <select aria-label={`${label}: ${t('Ui.SetupChoice')}`} value={selected} disabled={disabled || areaId === 'security'} onChange={event => setSelected(event.target.value as Choice)}>
      {['UseNow', 'LearnLater', 'NotApplicable'].map(value => <option key={value} value={value}>{t(`Ui.${value}`)}</option>)}
    </select></label>
    {selected === 'NotApplicable' && <label>{t('Ui.ScopeReason')} <select required value={selectedReason} disabled={disabled} onChange={event => setSelectedReason(event.target.value)}>
      <option value="">{t('Ui.SelectScopeReason')}</option>{['OutsideMission','OtherSystem','PartnerManaged','NoCurrentNeed'].map(value => <option key={value} value={value}>{t(`Ui.ScopeReason.${value}`)}</option>)}
    </select></label>}
    {areaId !== 'security' && <button type="submit" disabled={disabled || selected === choice && (selected !== 'NotApplicable' || selectedReason === reason)}>{t('Ui.SaveScope')}</button>}
  </form>;
}
