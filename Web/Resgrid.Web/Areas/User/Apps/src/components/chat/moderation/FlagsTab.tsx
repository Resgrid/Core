import { useCallback, useEffect, useState } from 'react';
import { getFlags, resolveFlag, moderatorDeleteMessage, muteUser, banUser } from '../chatModerationApi';
import { getMessages } from '../chatApi';
import { formatRelativeDay } from '../chatFormat';
import type { ChatFlagDto, ChatMessageDto } from '../types';

const REASON_LABELS: Record<number, string> = {
  0: 'Other',
  1: 'Inappropriate',
  2: 'Harassment',
  3: 'Spam',
  4: 'Sensitive info',
  5: 'Policy violation',
};

const STATUS_OPTIONS = [
  { value: 0, label: 'Open' },
  { value: 1, label: 'Reviewed' },
  { value: 2, label: 'Dismissed' },
  { value: 3, label: 'Action taken' },
];

const MUTE_MS = 24 * 60 * 60 * 1000;

export default function FlagsTab() {
  const [status, setStatus] = useState(0);
  const [flags, setFlags] = useState<ChatFlagDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [context, setContext] = useState<Record<string, ChatMessageDto | null>>({});
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    getFlags(status)
      .then(setFlags)
      .catch(() => setFlags([]))
      .finally(() => setLoading(false));
  }, [status]);

  useEffect(load, [load]);

  const loadContext = async (flag: ChatFlagDto) => {
    try {
      const page = await getMessages(flag.ChatChannelId);
      const found = page.messages.find((message) => message.ChatMessageId === flag.ChatMessageId) ?? null;
      setContext((current) => ({ ...current, [flag.ChatMessageFlagId]: found }));
    } catch {
      setContext((current) => ({ ...current, [flag.ChatMessageFlagId]: null }));
    }
  };

  const runAction = async (key: string, action: () => Promise<void>) => {
    setBusy(key);
    try {
      await action();
      load();
    } catch (error) {
      console.error('Flag action failed.', error);
    } finally {
      setBusy(null);
    }
  };

  return (
    <div>
      <div className="rgchat-form-row">
        <label htmlFor="rgchat-flag-status">Status</label>
        <select id="rgchat-flag-status" className="rgchat-input" style={{ width: 180 }} value={status} onChange={(event) => setStatus(Number(event.target.value))}>
          {STATUS_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>{option.label}</option>
          ))}
        </select>
        <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={load}>Refresh</button>
      </div>

      {loading && <div className="rgchat-convo__sub">Loading…</div>}
      {!loading && flags.length === 0 && <div className="rgchat-convo__sub">No flags for this status.</div>}

      <table className="rgchat-table">
        <thead>
          <tr>
            <th>Reason</th>
            <th>Note</th>
            <th>Flagged</th>
            <th>Message</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {flags.map((flag) => {
            const message = context[flag.ChatMessageFlagId];
            const loaded = flag.ChatMessageFlagId in context;
            return (
              <tr key={flag.ChatMessageFlagId}>
                <td><span className="rgchat-pill rgchat-pill--open">{REASON_LABELS[flag.Reason] ?? 'Other'}</span></td>
                <td>{flag.Note || '—'}</td>
                <td>{formatRelativeDay(flag.FlaggedOn)}</td>
                <td>
                  {loaded ? (
                    message ? (
                      <div>
                        <div style={{ fontWeight: 600 }}>{message.SenderDisplayName ?? 'Unknown'}</div>
                        <div className="rgchat-chan__preview" style={{ whiteSpace: 'normal' }}>{message.Body ?? '(no text)'}</div>
                      </div>
                    ) : (
                      <span className="rgchat-convo__sub">Not in recent history</span>
                    )
                  ) : (
                    <button type="button" className="rgchat-thread-link" onClick={() => void loadContext(flag)}>Load context</button>
                  )}
                </td>
                <td>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                    <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={busy === flag.ChatMessageFlagId} onClick={() => void runAction(flag.ChatMessageFlagId, () => resolveFlag(flag.ChatMessageFlagId, 1, 'Reviewed'))}>Review</button>
                    <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={busy === flag.ChatMessageFlagId} onClick={() => void runAction(flag.ChatMessageFlagId, () => resolveFlag(flag.ChatMessageFlagId, 2, 'Dismissed'))}>Dismiss</button>
                    <button type="button" className="rgchat-btn rgchat-btn--danger" disabled={busy === flag.ChatMessageFlagId} onClick={() => void runAction(flag.ChatMessageFlagId, async () => { await moderatorDeleteMessage(flag.ChatMessageId, 'Removed after report'); await resolveFlag(flag.ChatMessageFlagId, 3, 'Message deleted'); })}>Delete</button>
                    {message?.SenderUserId && (
                      <>
                        <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={busy === flag.ChatMessageFlagId} onClick={() => void runAction(flag.ChatMessageFlagId, () => muteUser(flag.ChatChannelId, message.SenderUserId as string, new Date(Date.now() + MUTE_MS).toISOString()))}>Mute</button>
                        <button type="button" className="rgchat-btn rgchat-btn--danger" disabled={busy === flag.ChatMessageFlagId} onClick={() => void runAction(flag.ChatMessageFlagId, () => banUser(flag.ChatChannelId, message.SenderUserId as string, true))}>Ban</button>
                      </>
                    )}
                  </div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
