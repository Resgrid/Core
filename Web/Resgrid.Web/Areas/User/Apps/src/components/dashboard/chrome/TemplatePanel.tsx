import { useEffect, useState } from 'react';
import { dashboardApi } from '../api';
import { t } from '../i18n';
import type { SettingOption, TemplateInfo } from '../types';

// The department template form (template mode, plan §5.2 DepartmentDashboardTemplates): name, description, audience
// (everyone / personnel role / group) and its target, priority (lower wins) and the department-default switch.

export interface TemplateDraft {
  name: string;
  description: string;
  audience: number;
  targetId: number | null;
  priority: number;
  isDefault: boolean;
}

export const NAME_MAX = 100;
export const DESCRIPTION_MAX = 500;

export function draftFromTemplate(template: TemplateInfo | null | undefined): TemplateDraft {
  return {
    name: template?.Name ?? '',
    description: template?.Description ?? '',
    audience: template?.Audience ?? 0,
    targetId: template?.TargetId ?? null,
    priority: template?.Priority ?? 0,
    isDefault: !!template?.IsDefault,
  };
}

/** A message key when the draft cannot be saved yet, otherwise null. */
export function validateDraft(draft: TemplateDraft): string | null {
  if (!draft.name.trim()) {
    return 'Dash.TemplateNameRequired';
  }
  if (draft.audience !== 0 && !draft.targetId) {
    return draft.audience === 1 ? 'Dash.TemplateRoleRequired' : 'Dash.TemplateGroupRequired';
  }
  return null;
}

export function TemplatePanel(props: { draft: TemplateDraft; onChange: (draft: TemplateDraft) => void; error: string | null; isNew: boolean }) {
  const { draft } = props;
  const [targets, setTargets] = useState<{ Roles: SettingOption[]; Groups: SettingOption[] } | null>(null);
  const [targetsFailed, setTargetsFailed] = useState(false);

  useEffect(() => {
    let disposed = false;
    dashboardApi
      .templateTargets()
      .then((result) => {
        if (!disposed) setTargets({ Roles: result.Roles ?? [], Groups: result.Groups ?? [] });
      })
      .catch(() => {
        if (!disposed) setTargetsFailed(true);
      });
    return () => {
      disposed = true;
    };
  }, []);

  const set = (patch: Partial<TemplateDraft>) => props.onChange({ ...draft, ...patch });
  const targetOptions = draft.audience === 1 ? targets?.Roles ?? [] : draft.audience === 2 ? targets?.Groups ?? [] : [];

  return (
    <section className="rg-tpl" aria-labelledby="rg-tpl-title">
      <header className="rg-tpl-head">
        <h1 id="rg-tpl-title" className="rg-greet">
          {props.isNew ? t('Dash.TemplateNewTitle') : t('Dash.TemplateEditTitle')}
        </h1>
        <p className="rg-head-meta">{t('Dash.TemplateIntro')}</p>
      </header>
      <div className="rg-tpl-grid">
        <label className="rg-tpl-name">
          {t('TemplateName')}
          <input className="rg-input" value={draft.name} maxLength={NAME_MAX} required aria-required="true" onChange={(event) => set({ name: event.target.value })} />
        </label>
        <label className="rg-tpl-desc">
          {t('Dash.TemplateDescription')}
          <textarea className="rg-input" rows={2} value={draft.description} maxLength={DESCRIPTION_MAX} onChange={(event) => set({ description: event.target.value })} />
        </label>
        <label>
          {t('TemplateAudience')}
          <select
            className="rg-input"
            value={draft.audience}
            onChange={(event) => set({ audience: Number(event.target.value), targetId: null })}
          >
            <option value={0}>{t('TemplateAudienceEveryone')}</option>
            <option value={1}>{t('TemplateAudienceRole')}</option>
            <option value={2}>{t('TemplateAudienceGroup')}</option>
          </select>
        </label>
        {draft.audience !== 0 ? (
          <label>
            {draft.audience === 1 ? t('TemplateAudienceRole') : t('TemplateAudienceGroup')}
            <select
              className="rg-input"
              value={draft.targetId ?? ''}
              disabled={!targets}
              onChange={(event) => set({ targetId: event.target.value ? Number(event.target.value) : null })}
            >
              <option value="">{targets ? t('Dash.SelectNone') : t('Dash.Loading')}</option>
              {targetOptions.map((option) => (
                <option key={option.Value} value={option.Value}>
                  {option.Label}
                </option>
              ))}
            </select>
            {targetsFailed ? <span className="rg-field-hint rg-field-error">{t('Dash.TemplateTargetsFailed')}</span> : null}
          </label>
        ) : null}
        <label>
          {t('TemplatePriority')}
          <input
            className="rg-input"
            type="number"
            min={0}
            max={1000}
            value={draft.priority}
            onChange={(event) => set({ priority: Math.max(0, Math.min(1000, Math.round(Number(event.target.value) || 0))) })}
          />
          <span className="rg-field-hint">{t('Dash.TemplatePriorityHint')}</span>
        </label>
        <label className="rg-chk rg-tpl-default">
          <input type="checkbox" checked={draft.isDefault} onChange={(event) => set({ isDefault: event.target.checked })} /> {t('TemplateDefault')}
          <span className="rg-field-hint">{t('Dash.TemplateDefaultHint')}</span>
        </label>
      </div>
      {props.error ? (
        <div className="rg-field-error" role="alert">
          {t(props.error)}
        </div>
      ) : null}
    </section>
  );
}
