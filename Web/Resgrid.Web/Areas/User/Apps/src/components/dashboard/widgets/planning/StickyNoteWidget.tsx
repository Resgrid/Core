import { useEffect, useRef, useState } from 'react';
import type { WidgetProps } from '../types';
import './planningWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/StickyNoteWidgetData.cs */
export interface StickyNoteWidgetData {
  Text?: string | null;
}

const MAX_LENGTH = 2000;
const SAVE_DELAY_MS = 800;

/**
 * general.stickyNote (plan §7.6): a private plain-text note kept in this instance's settings. Typing saves through
 * ctx.updateSettings after a short pause, without refetching (the server only echoes the text back).
 */
export default function StickyNoteWidget({ data, instance, t, ctx }: WidgetProps<StickyNoteWidgetData>) {
  const stored = typeof instance.Settings?.text === 'string' ? (instance.Settings.text as string) : data.Text ?? '';
  const [text, setText] = useState(stored);
  const timer = useRef<number | null>(null);
  const pending = useRef<string | null>(null);

  // The context object can change between renders; the unmount flush must use the latest one, once.
  const ctxRef = useRef(ctx);
  ctxRef.current = ctx;

  // Flush an unsaved edit when the widget goes away (board switch, removal).
  useEffect(
    () => () => {
      if (timer.current !== null) window.clearTimeout(timer.current);
      if (pending.current !== null) ctxRef.current.updateSettings({ text: pending.current }, { refetch: false });
    },
    [],
  );

  const onChange = (value: string) => {
    const next = value.length > MAX_LENGTH ? value.slice(0, MAX_LENGTH) : value;
    setText(next);
    pending.current = next;
    if (timer.current !== null) window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => {
      timer.current = null;
      if (pending.current !== null) {
        ctxRef.current.updateSettings({ text: pending.current }, { refetch: false });
        pending.current = null;
      }
    }, SAVE_DELAY_MS);
  };

  return (
    <div className="rg-sticky">
      <textarea
        className="rg-sticky-text"
        value={text}
        maxLength={MAX_LENGTH}
        aria-label={t('W.general.stickyNote.Title')}
        placeholder={t('W.general.stickyNote.Placeholder')}
        disabled={ctx.editing}
        onChange={(event) => onChange(event.target.value)}
      />
      {text.length > MAX_LENGTH * 0.9 ? <div className="rg-sticky-count rg-mu">{t('W.general.stickyNote.Remaining', MAX_LENGTH - text.length)}</div> : null}
    </div>
  );
}
