import { useId, useState } from 'react';

type Choice = 'UseNow' | 'LearnLater' | 'NotApplicable';
export default function AreaSetupChoice({ areaId, label, choice, reason, disabled, t, save }: {
  areaId: string; label: string; choice?: Choice; reason?: string; disabled: boolean; t: (key: string) => string;
  save: (choice: Choice, reason: string | null) => void;
}) {
  const [selected, setSelected] = useState<Choice | ''>(choice ?? '');
  const [selectedReason, setSelectedReason] = useState(reason ?? '');
  const id = useId();
  const unchanged = selected === (choice ?? '') && (selected !== 'NotApplicable' || selectedReason === (reason ?? ''));
  return <form onSubmit={event => { event.preventDefault(); if (selected) save(selected, selected === 'NotApplicable' ? selectedReason : null); }}>
    <div className="form-group">
      <label className="control-label" htmlFor={`${id}-choice`}>{t('Ui.SetupChoice')}</label>
      <select id={`${id}-choice`} className="form-control input-sm" aria-label={`${label}: ${t('Ui.SetupChoice')}`} value={selected} disabled={disabled || areaId === 'security'} onChange={event => setSelected(event.target.value as Choice)}>
        {!choice && <option value="" disabled>{t('Ui.ChooseScope')}</option>}
        {['UseNow', 'LearnLater', 'NotApplicable'].map(value => <option key={value} value={value}>{t(`Ui.${value}`)}</option>)}
      </select>
      {areaId === 'security' && <span className="help-block rgaa-small">{t('Ui.SecurityAlwaysApplies')}</span>}
    </div>
    {selected === 'NotApplicable' && <div className="form-group"><label className="control-label" htmlFor={`${id}-reason`}>{t('Ui.ScopeReason')}</label>
      <select id={`${id}-reason`} className="form-control input-sm" required value={selectedReason} disabled={disabled} onChange={event => setSelectedReason(event.target.value)}>
        <option value="">{t('Ui.SelectScopeReason')}</option>{['OutsideMission', 'OtherSystem', 'PartnerManaged', 'NoCurrentNeed'].map(value => <option key={value} value={value}>{t(`Ui.ScopeReason.${value}`)}</option>)}
      </select></div>}
    {areaId !== 'security' && !unchanged && selected && <button type="submit" className="btn btn-primary btn-xs" disabled={disabled}>{t('Ui.SaveScope')}</button>}
  </form>;
}
