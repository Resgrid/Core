import { useId, useState } from 'react';

type Choice = 'UseNow' | 'LearnLater' | 'NotApplicable';
const choices: { value: Choice; icon: string }[] = [{ value: 'UseNow', icon: 'fa-check' }, { value: 'LearnLater', icon: 'fa-clock-o' }, { value: 'NotApplicable', icon: 'fa-ban' }];

/**
 * A module's setup choice as three toggle buttons. Use now and Learn later save on click; Not applicable asks for its
 * reason first. Security always applies, so its choice is shown but fixed.
 */
export default function AreaSetupChoice({ areaId, label, choice, reason, disabled, t, save, compact }: {
  areaId: string; label: string; choice?: Choice; reason?: string; disabled: boolean; t: (key: string) => string;
  save: (choice: Choice, reason: string | null) => void; compact?: boolean;
}) {
  const [selected, setSelected] = useState<Choice | ''>(choice ?? '');
  const [selectedReason, setSelectedReason] = useState(reason ?? '');
  const id = useId();
  const fixed = areaId === 'security';
  const pick = (value: Choice) => {
    setSelected(value);
    if (value !== 'NotApplicable' && value !== choice) save(value, null);
  };
  const reasonChanged = selected === 'NotApplicable' && (choice !== 'NotApplicable' || selectedReason !== (reason ?? ''));
  return <div className={`rgaa-scope${compact ? ' rgaa-scope--compact' : ''}`}>
    <span className={compact ? 'sr-only' : 'rgaa-field-label'} id={`${id}-label`}>{t('Ui.SetupChoice')}</span>
    <div className="rgaa-segmented" role="group" aria-labelledby={`${id}-label`} aria-describedby={fixed ? `${id}-fixed` : undefined}>
      {choices.map(option => <button type="button" key={option.value} className="rgaa-plain rgaa-segment" aria-pressed={selected === option.value}
        aria-label={`${label}: ${t(`Ui.${option.value}`)}`} disabled={disabled || fixed} onClick={() => pick(option.value)}>
        <i className={`fa ${option.icon}`} aria-hidden="true" /><span>{t(`Ui.${option.value}`)}</span>
      </button>)}
    </div>
    {fixed && <span id={`${id}-fixed`} className="help-block rgaa-small">{t('Ui.SecurityAlwaysApplies')}</span>}
    {selected === 'NotApplicable' && !fixed && <form className="rgaa-inline-form rgaa-scope__reason" onSubmit={event => { event.preventDefault(); save('NotApplicable', selectedReason); }}>
      <label htmlFor={`${id}-reason`}>{t('Ui.ScopeReason')}</label>
      <select id={`${id}-reason`} className="form-control input-sm" required value={selectedReason} disabled={disabled} onChange={event => setSelectedReason(event.target.value)}>
        <option value="">{t('Ui.SelectScopeReason')}</option>{['OutsideMission', 'OtherSystem', 'PartnerManaged', 'NoCurrentNeed'].map(value => <option key={value} value={value}>{t(`Ui.ScopeReason.${value}`)}</option>)}
      </select>
      {reasonChanged && <button type="submit" className="btn btn-primary btn-xs" disabled={disabled || !selectedReason}><i className="fa fa-check" aria-hidden="true" /> {t('Ui.SaveScope')}</button>}
    </form>}
  </div>;
}
