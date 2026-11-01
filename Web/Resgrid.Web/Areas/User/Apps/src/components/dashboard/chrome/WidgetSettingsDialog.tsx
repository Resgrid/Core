import { useMemo, useState } from 'react';
import { liveTopics } from '../data/dashboardBus';
import { t } from '../i18n';
import type { LayoutWidget, SettingOption, WidgetDefinition, WidgetSetting } from '../types';
import { Dialog } from './overlays';
import { widgetTitle } from './WidgetFrame';

// Renders a widget's settings from its schema (plan §6.5): number, toggle, select, multiSelect and text. Options come
// from the setting itself or from the bootstrap's department option lists (setting.OptionsSource). The server
// re-validates every value (allow-list, types, bounds) on save and on preview.

function optionsFor(setting: WidgetSetting, sources: Record<string, SettingOption[]>): SettingOption[] {
  if (setting.Options && setting.Options.length > 0) {
    return setting.Options;
  }
  return (setting.OptionsSource && sources[setting.OptionsSource]) || [];
}

function settingLabel(definition: WidgetDefinition, setting: WidgetSetting): string {
  const key = `W.${definition.Key}.Setting.${setting.Name}`;
  return setting.Label && setting.Label !== key ? setting.Label : t(key);
}

function initialValue(setting: WidgetSetting, current: unknown): unknown {
  const value = current !== undefined ? current : setting.Default;
  switch (setting.Type) {
    case 'toggle':
      return value === true || value === 'true';
    case 'number': {
      const number = typeof value === 'number' ? value : Number(value);
      return Number.isFinite(number) ? number : setting.Min ?? 0;
    }
    case 'multiSelect':
      return Array.isArray(value) ? value.map(String) : value === null || value === undefined || value === '' ? [] : [String(value)];
    default:
      return value === null || value === undefined ? '' : String(value);
  }
}

function clampNumber(setting: WidgetSetting, raw: unknown): number {
  let value = typeof raw === 'number' ? raw : Number(raw);
  if (!Number.isFinite(value)) {
    value = typeof setting.Default === 'number' ? setting.Default : setting.Min ?? 0;
  }
  if (setting.Min !== null && setting.Min !== undefined) value = Math.max(setting.Min, value);
  if (setting.Max !== null && setting.Max !== undefined) value = Math.min(setting.Max, value);
  return Math.round(value);
}

function refreshNote(definition: WidgetDefinition): string {
  if (liveTopics(definition.RealtimeTopics).length > 0) {
    return t('Dash.SettingsLive');
  }
  const seconds = definition.DefaultRefreshSeconds || 300;
  return seconds >= 3600 ? t('Dash.SettingsRefreshHours', Math.round(seconds / 3600)) : t('Dash.SettingsRefreshMinutes', Math.max(1, Math.round(seconds / 60)));
}

export function WidgetSettingsDialog(props: {
  instance: LayoutWidget;
  definition: WidgetDefinition;
  sources: Record<string, SettingOption[]>;
  onApply: (settings: Record<string, unknown>) => void;
  onClose: () => void;
}) {
  const { definition, instance } = props;
  const [values, setValues] = useState<Record<string, unknown>>(() => {
    const result: Record<string, unknown> = {};
    for (const setting of definition.Settings) {
      result[setting.Name] = initialValue(setting, instance.Settings?.[setting.Name]);
    }
    return result;
  });

  const set = (name: string, value: unknown) => setValues((current) => ({ ...current, [name]: value }));
  const ids = useMemo(() => definition.Settings.map((setting) => `rg-set-${instance.Id}-${setting.Name}`), [definition.Settings, instance.Id]);

  const apply = () => {
    const result: Record<string, unknown> = { ...(instance.Settings ?? {}) };
    for (const setting of definition.Settings) {
      const value = values[setting.Name];
      switch (setting.Type) {
        case 'number':
          result[setting.Name] = clampNumber(setting, value);
          break;
        case 'toggle':
          result[setting.Name] = value === true;
          break;
        case 'multiSelect':
          result[setting.Name] = Array.isArray(value) ? value : [];
          break;
        case 'text': {
          const text = String(value ?? '');
          result[setting.Name] = setting.Max ? text.slice(0, setting.Max) : text;
          break;
        }
        default:
          result[setting.Name] = String(value ?? '');
      }
    }
    props.onApply(result);
  };

  return (
    <Dialog
      title={t('Dash.SettingsTitle', widgetTitle(definition))}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="btn btn-default btn-sm" onClick={props.onClose}>
            {t('Dash.Cancel')}
          </button>
          <button type="button" className="btn btn-primary btn-sm" onClick={apply}>
            {t('Dash.Apply')}
          </button>
        </>
      }
    >
      {definition.Settings.map((setting, index) => {
        const label = settingLabel(definition, setting);
        const id = ids[index];
        const value = values[setting.Name];
        switch (setting.Type) {
          case 'toggle':
            return (
              <label key={setting.Name} className="rg-chk" htmlFor={id}>
                <input id={id} type="checkbox" checked={value === true} onChange={(event) => set(setting.Name, event.target.checked)} /> {label}
              </label>
            );
          case 'number':
            return (
              <label key={setting.Name} htmlFor={id}>
                {label}
                <input
                  id={id}
                  className="rg-input"
                  type="number"
                  inputMode="numeric"
                  min={setting.Min ?? undefined}
                  max={setting.Max ?? undefined}
                  value={value === '' ? '' : String(value)}
                  onChange={(event) => set(setting.Name, event.target.value === '' ? '' : Number(event.target.value))}
                />
                {setting.Min !== null && setting.Min !== undefined && setting.Max !== null && setting.Max !== undefined ? (
                  <span className="rg-field-hint">{t('Dash.SettingRange', setting.Min, setting.Max)}</span>
                ) : null}
              </label>
            );
          case 'select': {
            const options = optionsFor(setting, props.sources);
            const current = String(value ?? '');
            return (
              <label key={setting.Name} htmlFor={id}>
                {label}
                <select id={id} className="rg-input" value={current} onChange={(event) => set(setting.Name, event.target.value)}>
                  {/* A stored value the department no longer offers stays selectable until changed. */}
                  {current && !options.some((option) => option.Value === current) ? <option value={current}>{current}</option> : null}
                  {!current ? <option value="">{t('Dash.SelectNone')}</option> : null}
                  {options.map((option) => (
                    <option key={option.Value} value={option.Value}>
                      {option.Label}
                    </option>
                  ))}
                </select>
              </label>
            );
          }
          case 'multiSelect': {
            const options = optionsFor(setting, props.sources);
            const selected = new Set(Array.isArray(value) ? (value as string[]) : []);
            return (
              <fieldset key={setting.Name} className="rg-multi">
                <legend>{label}</legend>
                {options.length === 0 ? <span className="rg-field-hint">{t('Dash.NoOptions')}</span> : null}
                <div className="rg-multi-list">
                  {options.map((option) => (
                    <label key={option.Value} className="rg-chk">
                      <input
                        type="checkbox"
                        checked={selected.has(option.Value)}
                        onChange={(event) => {
                          const next = new Set(selected);
                          if (event.target.checked) next.add(option.Value);
                          else next.delete(option.Value);
                          set(setting.Name, options.map((item) => item.Value).filter((item) => next.has(item)));
                        }}
                      />{' '}
                      {option.Label}
                    </label>
                  ))}
                </div>
                <span className="rg-field-hint">{t('Dash.MultiSelectHint')}</span>
              </fieldset>
            );
          }
          default:
            return (
              <label key={setting.Name} htmlFor={id}>
                {label}
                <input
                  id={id}
                  className="rg-input"
                  type="text"
                  maxLength={setting.Max ?? undefined}
                  value={String(value ?? '')}
                  onChange={(event) => set(setting.Name, event.target.value)}
                />
              </label>
            );
        }
      })}
      <div className="rg-chart-note">{refreshNote(definition)}</div>
    </Dialog>
  );
}
