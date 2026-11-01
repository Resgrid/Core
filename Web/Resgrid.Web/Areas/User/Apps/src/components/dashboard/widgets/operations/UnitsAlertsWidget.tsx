import { useState } from 'react';
import { ApiError } from '../../../../runtime/api';
import { EmptyState, ItemList } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { Chip, bffPath, minutesSince, useNow, type EndpointData, type StatusChipData } from './opsShared';
import './opsWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Operations/UnitsAlertsWidgetData.cs */
export interface UnitsAlertAcknowledgement {
  AcknowledgementId: string;
  /** 0 acknowledged, 1 muted. */
  Mode: number;
  Level: number;
  Note?: string | null;
  AcknowledgedByName?: string | null;
  AcknowledgedOnUtc: string;
  MutedUntilUtc?: string | null;
}

export interface UnitsAlertRow {
  UnitId: number;
  UnitName?: string | null;
  Type?: string | null;
  StationName?: string | null;
  UnitStateId: number;
  Status: StatusChipData;
  StatusOnUtc: string;
  /** 1 warning, 2 alert. */
  Level: number;
  WarnSeconds: number;
  AlertSeconds: number;
  NoteRequired: boolean;
  Acknowledgement?: UnitsAlertAcknowledgement | null;
  Url?: string | null;
}

export interface UnitsAlertsWidgetData {
  TimersConfigured: boolean;
  NoteRequirement: number;
  CanAcknowledge: boolean;
  Alerts: UnitsAlertRow[];
  AlertCount: number;
  WarningCount: number;
  MutedCount: number;
  Acknowledge?: EndpointData | null;
  Clear?: EndpointData | null;
}

const LEVEL_ALERT = 2;
const MODE_ACKNOWLEDGE = 0;
const MODE_MUTE = 1;
const MUTE_CHOICES = [15, 30, 60, 0];

interface EditorState {
  unitId: number;
  mode: number;
}

/** units.alerts (plan §7.2): units past their status timer; acknowledge, mute and annotate as on the Big Board. */
export default function UnitsAlertsWidget({ data, t, ctx }: WidgetProps<UnitsAlertsWidgetData>) {
  const k = 'W.units.alerts.';
  const now = useNow(30000);
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [note, setNote] = useState('');
  const [muteMinutes, setMuteMinutes] = useState(30);
  const [busy, setBusy] = useState(false);

  const alerts = data.Alerts ?? [];
  const unacknowledged = alerts.filter((a) => !a.Acknowledgement).length;
  useWidgetBadge(ctx, unacknowledged > 0 ? unacknowledged : null);

  if (!data.TimersConfigured) {
    return <EmptyState icon="fa-bell-slash-o" text={t(`${k}NoTimers`)} linkText={t(`${k}SetUpTimers`)} href="/User/Department/MappingSettings" />;
  }
  if (alerts.length === 0) {
    return (
      <EmptyState
        icon="fa-check-circle-o"
        text={data.MutedCount > 0 ? t(`${k}OnlyMuted`, data.MutedCount) : t(`${k}Empty`)}
        onAction={data.MutedCount > 0 ? ctx.openSettings : undefined}
        linkText={data.MutedCount > 0 ? t(`${k}ShowMuted`) : undefined}
      />
    );
  }

  const canAct = data.CanAcknowledge && !!data.Acknowledge?.Available;
  const editing = editor ? alerts.find((a) => a.UnitId === editor.unitId) ?? null : null;

  const open = (alert: UnitsAlertRow, mode: number) => {
    setEditor({ unitId: alert.UnitId, mode });
    setNote('');
  };

  const save = async () => {
    if (!editor || !editing || !data.Acknowledge) return;
    if (editing.NoteRequired && !note.trim()) {
      ctx.toast(t(`${k}NoteRequired`));
      return;
    }
    setBusy(true);
    try {
      await ctx.api(bffPath(data.Acknowledge.Url), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          UnitId: editing.UnitId,
          UnitStateId: editing.UnitStateId,
          Level: editing.Level,
          Mode: editor.mode,
          MuteMinutes: editor.mode === MODE_MUTE ? muteMinutes : 0,
          Note: note.trim() || null,
        }),
      });
      ctx.toast(editor.mode === MODE_MUTE ? t(`${k}Muted`, editing.UnitName || '') : t(`${k}Acknowledged`, editing.UnitName || ''));
      setEditor(null);
      setNote('');
      ctx.refresh();
    } catch (error) {
      const status = error instanceof ApiError ? error.status : 0;
      ctx.toast(status === 409 ? t(`${k}StatusChanged`) : status === 400 ? t(`${k}NoteRequired`) : t(`${k}SaveFailed`));
      if (status === 409) ctx.refresh();
    } finally {
      setBusy(false);
    }
  };

  const clear = async (alert: UnitsAlertRow) => {
    if (!alert.Acknowledgement || !data.Clear?.Available || busy) return;
    setBusy(true);
    try {
      await ctx.api(bffPath(data.Clear.Url, alert.Acknowledgement.AcknowledgementId), { method: 'DELETE' });
      ctx.toast(t(`${k}Cleared`, alert.UnitName || ''));
      ctx.refresh();
    } catch {
      ctx.toast(t(`${k}SaveFailed`));
    } finally {
      setBusy(false);
    }
  };

  const describe = (alert: UnitsAlertRow) => {
    const ack = alert.Acknowledgement;
    if (ack) {
      const who = ack.AcknowledgedByName || t(`${k}Someone`);
      const base =
        ack.Mode === MODE_MUTE
          ? ack.MutedUntilUtc
            ? t(`${k}MutedUntilBy`, who, ctx.format.time(ack.MutedUntilUtc))
            : t(`${k}MutedByUntilChange`, who)
          : t(`${k}AcknowledgedBy`, who, ctx.format.ago(ack.AcknowledgedOnUtc));
      return ack.Note ? `${base} · ${ack.Note}` : base;
    }
    const threshold = alert.Level >= LEVEL_ALERT ? alert.AlertSeconds : alert.WarnSeconds;
    return [t(`${k}OverTimer`, ctx.format.duration(Math.round(threshold / 60))), alert.StationName].filter(Boolean).join(' · ');
  };

  return (
    <>
      <ItemList
        items={alerts.map((alert) => {
          const muted = alert.Acknowledgement?.Mode === MODE_MUTE;
          return {
            key: String(alert.UnitId),
            icon: muted ? 'fa-bell-slash-o' : alert.Acknowledgement ? 'fa-bell-o' : 'fa-bell',
            tone: muted ? undefined : alert.Level >= LEVEL_ALERT ? ('bad' as const) : ('warn' as const),
            title: (
              <span className={muted ? 'rg-ualerts-muted' : undefined}>
                {alert.UnitName} · <Chip chip={alert.Status} /> {ctx.format.duration(minutesSince(alert.StatusOnUtc, now))}
              </span>
            ),
            href: alert.Url ?? undefined,
            meta: describe(alert),
            right: canAct ? (
              <span className="rg-ops-actions">
                {!alert.Acknowledgement || alert.Acknowledgement.Mode === MODE_MUTE ? (
                  <button type="button" className="rg-btn-xs" disabled={busy} onClick={() => open(alert, MODE_ACKNOWLEDGE)}>
                    {t(`${k}Acknowledge`)}
                  </button>
                ) : null}
                {!muted ? (
                  <button type="button" className="rg-btn-xs" disabled={busy} onClick={() => open(alert, MODE_MUTE)}>
                    {t(`${k}Mute`)}
                  </button>
                ) : null}
                {alert.Acknowledgement && data.Clear?.Available ? (
                  <button type="button" className="rg-btn-xs" disabled={busy} title={t(`${k}ClearTitle`)} onClick={() => void clear(alert)}>
                    {t(`${k}Clear`)}
                  </button>
                ) : null}
              </span>
            ) : undefined,
          };
        })}
      />
      {editor && editing ? (
        <form
          className="rg-ualerts-editor"
          onSubmit={(event) => {
            event.preventDefault();
            void save();
          }}
        >
          <div className="rg-ualerts-editor-title">
            {editor.mode === MODE_MUTE ? t(`${k}MuteTitle`, editing.UnitName || '') : t(`${k}AcknowledgeTitle`, editing.UnitName || '')}
          </div>
          <div className="rg-ualerts-editor-row">
            <input
              className="rg-input"
              value={note}
              maxLength={500}
              onChange={(event) => setNote(event.target.value)}
              placeholder={editing.NoteRequired ? t(`${k}NotePlaceholderRequired`) : t(`${k}NotePlaceholder`)}
              aria-label={t(`${k}NoteLabel`)}
              required={editing.NoteRequired}
              autoFocus
            />
            {editor.mode === MODE_MUTE ? (
              <select className="rg-input" value={muteMinutes} onChange={(event) => setMuteMinutes(Number(event.target.value))} aria-label={t(`${k}MuteFor`)}>
                {MUTE_CHOICES.map((minutes) => (
                  <option key={minutes} value={minutes}>
                    {minutes === 0 ? t(`${k}UntilStatusChanges`) : t(`${k}ForMinutes`, ctx.format.duration(minutes))}
                  </option>
                ))}
              </select>
            ) : null}
            <button type="submit" className="rg-btn-xs rg-btn-primary" disabled={busy}>
              {busy ? <i className="fa fa-spinner fa-spin" aria-hidden="true" /> : null} {editor.mode === MODE_MUTE ? t(`${k}Mute`) : t(`${k}Acknowledge`)}
            </button>
            <button type="button" className="rg-btn-xs" disabled={busy} onClick={() => setEditor(null)}>
              {t(`${k}Cancel`)}
            </button>
          </div>
        </form>
      ) : null}
    </>
  );
}
