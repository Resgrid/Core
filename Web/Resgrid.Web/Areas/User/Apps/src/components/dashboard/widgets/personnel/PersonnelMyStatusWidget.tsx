import { useState } from 'react';
import { SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';
import { Chip, type EndpointData, type StatusChipData } from '../operations/opsShared';
import './personnelWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Personnel/PersonnelMyStatusWidgetData.cs */
export interface PersonnelMyStatusOption {
  Id: number;
  Chip: StatusChipData;
  /** CustomStateDetailTypes: 0 none, 1 stations, 2 calls, 3 calls and stations. */
  DetailType: number;
  /** CustomStateNoteTypes: 0 none, 1 optional, 2 required. */
  NoteType: number;
}

export interface PersonnelMyStatusDestination {
  Id: number;
  Name?: string | null;
  Number?: string | null;
  Color?: string | null;
}

export interface PersonnelMyStatusWidgetData {
  CustomStatuses: boolean;
  CustomStaffing: boolean;
  Statuses: PersonnelMyStatusOption[];
  StaffingLevels: PersonnelMyStatusOption[];
  CurrentStatus?: StatusChipData | null;
  CurrentStatusOnUtc?: string | null;
  CurrentStatusDestination?: string | null;
  CurrentStaffing?: StatusChipData | null;
  CurrentStaffingOnUtc?: string | null;
  Stations: PersonnelMyStatusDestination[];
  Calls: PersonnelMyStatusDestination[];
  SetStatus: EndpointData;
  RespondToCall: EndpointData;
  RespondToStation: EndpointData;
  SetStaffing: EndpointData;
  SetStaffingWithNote: EndpointData;
  UserId: string;
}

const NOTE_REQUIRED = 2;

/**
 * personnel.myStatus (plan §7.3): today's Actions and Staffing iboxes — the department's status buttons, respond to a
 * call or station, and the staffing level with an optional note. Every write goes to the classic HomeController action.
 */
export default function PersonnelMyStatusWidget({ data, size, t, ctx }: WidgetProps<PersonnelMyStatusWidgetData>) {
  const k = 'W.personnel.myStatus.';
  const [busy, setBusy] = useState(false);
  const [statusNote, setStatusNote] = useState('');
  const [respondTarget, setRespondTarget] = useState('');
  const [showRespond, setShowRespond] = useState(false);
  // On a small card the status note opens when a status that needs one is pressed.
  const [noteOpen, setNoteOpen] = useState(false);
  const [staffing, setStaffing] = useState<string>('');
  const [staffingNote, setStaffingNote] = useState('');

  const statuses = data.Statuses ?? [];
  const levels = data.StaffingLevels ?? [];
  const calls = data.Calls ?? [];
  const stations = data.Stations ?? [];
  // A small card shows the note field only when a status needs one; the pressed button already says the current status.
  const takesNote = size === 'm' ? statuses.some((s) => s.NoteType > 0) : noteOpen;
  const canRespond = calls.length > 0 || stations.length > 0;
  // The pressed button is the current status; its tooltip says where to and since when.
  const currentTitle = [
    data.CurrentStatus?.Text,
    data.CurrentStatusDestination,
    data.CurrentStatusOnUtc ? t(`${k}Since`, ctx.format.dateTime(data.CurrentStatusOnUtc)) : null,
  ]
    .filter(Boolean)
    .join(' · ');
  const statusShown = !!data.CurrentStatus && statuses.some((s) => s.Id === data.CurrentStatus?.Id);
  const selectedStaffing = staffing || (data.CurrentStaffing ? String(data.CurrentStaffing.Id) : levels[0] ? String(levels[0].Id) : '');

  const run = async (action: () => Promise<boolean>, success: string) => {
    if (busy) return;
    setBusy(true);
    try {
      if (await action()) {
        ctx.toast(success);
        ctx.refresh();
      } else {
        ctx.toast(t(`${k}Failed`));
      }
    } finally {
      setBusy(false);
    }
  };

  const setStatus = (option: PersonnelMyStatusOption) => {
    const note = statusNote.trim();
    if (option.NoteType === NOTE_REQUIRED && !note) {
      setNoteOpen(true);
      ctx.toast(t(`${k}NoteRequired`, option.Chip.Text || ''));
      return;
    }
    void run(async () => {
      const result = await ctx.submit(data.SetStatus.Url, { query: { actionType: option.Id }, form: note ? { note } : undefined });
      if (result.ok) {
        setStatusNote('');
        setNoteOpen(false);
      }
      return result.ok;
    }, t(`${k}StatusSet`, option.Chip.Text || ''));
  };

  const respond = () => {
    const [kind, id] = respondTarget.split(':');
    if (!kind || !id) return;
    const isCall = kind === 'call';
    const target = (isCall ? calls : stations).find((x) => String(x.Id) === id);
    void run(async () => {
      const result = isCall
        ? await ctx.submit(data.RespondToCall.Url, { query: { callId: Number(id) } })
        : await ctx.submit(data.RespondToStation.Url, { query: { stationId: Number(id) } });
      if (result.ok) {
        setRespondTarget('');
        setShowRespond(false);
      }
      return result.ok;
    }, t(`${k}Responding`, (isCall ? target?.Number || target?.Name : target?.Name) || ''));
  };

  const saveStaffing = () => {
    const level = levels.find((x) => String(x.Id) === selectedStaffing);
    if (!level) return;
    const note = staffingNote.trim();
    if (level.NoteType === NOTE_REQUIRED && !note) {
      ctx.toast(t(`${k}NoteRequired`, level.Chip.Text || ''));
      return;
    }
    void run(async () => {
      // A note only travels in a form body (SetUserState); without one the plain staffing action is enough.
      const result = note
        ? await ctx.submit(data.SetStaffingWithNote.Url, {
            // DashboardModel binding: a custom level posts UserState, a built-in one UserStateEnum.
            form: data.CustomStaffing
              ? { CustomStaffingActive: true, UserState: level.Id, StateNote: note }
              : { CustomStaffingActive: false, UserStateEnum: level.Id, StateNote: note },
          })
        : await ctx.submit(data.SetStaffing.Url, { query: { userId: data.UserId, staffingLevel: level.Id } });
      if (result.ok) {
        setStaffingNote('');
        setStaffing('');
      }
      return result.ok;
    }, t(`${k}StaffingSet`, level.Chip.Text || ''));
  };

  // The stored note is never echoed back: the field always starts empty.
  const noteInput = (
    <input
      className="rg-input"
      value={staffingNote}
      maxLength={500}
      onChange={(event) => setStaffingNote(event.target.value)}
      placeholder={ctx.adp.enforced ? t(`${k}StaffingNoteEncrypted`) : t(`${k}StaffingNotePlaceholder`)}
      aria-label={t(`${k}StaffingNoteLabel`)}
      autoComplete="off"
    />
  );

  return (
    <>
      {data.CurrentStatus && !statusShown ? (
        <div className="rg-mystatus-now" aria-live="polite">
          {data.CurrentStatus ? (
            <span title={data.CurrentStatusOnUtc ? ctx.format.dateTime(data.CurrentStatusOnUtc) : undefined}>
              <Chip chip={data.CurrentStatus} />
              {data.CurrentStatusDestination ? ` · ${data.CurrentStatusDestination}` : ''}
              {data.CurrentStatusOnUtc ? ` · ${ctx.format.ago(data.CurrentStatusOnUtc)}` : ''}
            </span>
          ) : null}
        </div>
      ) : null}

      <div className={`rg-mystatus-btns${size === 'm' ? ' rg-mystatus-wide' : ''}`} role="group" aria-label={t(`${k}StatusButtons`)}>
        {statuses.map((option) => (
          <button
            key={option.Id}
            type="button"
            disabled={busy}
            aria-pressed={data.CurrentStatus?.Id === option.Id}
            style={{ background: option.Chip.Color || 'var(--rg-text)', color: option.Chip.TextColor || '#fff' }}
            title={data.CurrentStatus?.Id === option.Id ? currentTitle : option.Chip.Text || undefined}
            onClick={() => setStatus(option)}
          >
            {option.Chip.Text}
          </button>
        ))}
        {canRespond ? (
          <button
            type="button"
            className="rg-mystatus-respond"
            disabled={busy}
            aria-expanded={showRespond}
            aria-controls={`${ctx.instanceId}-respond`}
            onClick={() => setShowRespond((open) => !open)}
          >
            <i className="fa fa-bullhorn" aria-hidden="true" /> {t(`${k}RespondTo`)}
          </button>
        ) : null}
      </div>

      {takesNote ? (
        <div className="rg-mystatus-row">
          <input
            className="rg-input"
            value={statusNote}
            maxLength={500}
            onChange={(event) => setStatusNote(event.target.value)}
            placeholder={t(`${k}StatusNotePlaceholder`)}
            aria-label={t(`${k}StatusNoteLabel`)}
            autoFocus={size !== 'm'}
          />
        </div>
      ) : null}

      {canRespond && showRespond ? (
        <div className="rg-mystatus-row" id={`${ctx.instanceId}-respond`}>
          <select className="rg-input" value={respondTarget} onChange={(event) => setRespondTarget(event.target.value)} aria-label={t(`${k}RespondLabel`)}>
            <option value="">{t(`${k}RespondPlaceholder`)}</option>
            {calls.length > 0 ? (
              <optgroup label={t(`${k}Calls`)}>
                {calls.map((call) => (
                  <option key={`call-${call.Id}`} value={`call:${call.Id}`}>
                    {[call.Number, call.Name].filter(Boolean).join(' · ')}
                  </option>
                ))}
              </optgroup>
            ) : null}
            {stations.length > 0 ? (
              <optgroup label={t(`${k}Stations`)}>
                {stations.map((station) => (
                  <option key={`station-${station.Id}`} value={`station:${station.Id}`}>
                    {station.Name}
                  </option>
                ))}
              </optgroup>
            ) : null}
          </select>
          <button type="button" className="rg-btn-xs rg-btn-primary" disabled={busy || !respondTarget} onClick={respond}>
            <i className="fa fa-bullhorn" aria-hidden="true" /> {t(`${k}Respond`)}
          </button>
        </div>
      ) : null}

      <SubHeading>{t(`${k}Staffing`)}</SubHeading>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          saveStaffing();
        }}
      >
        <div className="rg-mystatus-row rg-mystatus-staffing">
          <select className="rg-input" value={selectedStaffing} onChange={(event) => setStaffing(event.target.value)} aria-label={t(`${k}StaffingLabel`)}>
            {levels.map((level) => (
              <option key={level.Id} value={String(level.Id)}>
                {level.Chip.Text}
              </option>
            ))}
          </select>
          {noteInput}
          <button type="submit" className="rg-btn-xs" disabled={busy || !selectedStaffing}>
            {busy ? <i className="fa fa-spinner fa-spin" aria-hidden="true" /> : null} {t(`${k}Set`)}
          </button>
        </div>
      </form>
    </>
  );
}
