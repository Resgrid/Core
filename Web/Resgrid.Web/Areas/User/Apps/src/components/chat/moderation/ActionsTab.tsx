import { useCallback, useEffect, useState } from 'react';
import { getActions } from '../chatModerationApi';
import { formatRelativeDay } from '../chatFormat';
import type { ChatModerationActionDto } from '../types';

const ACTION_LABELS: Record<number, string> = {
  0: 'Delete message',
  1: 'Mute user',
  2: 'Ban user',
  3: 'Unban user',
  4: 'Lock channel',
  5: 'Unlock channel',
  6: 'Resolve flag',
};

function actionLabel(actionType: number): string {
  return ACTION_LABELS[actionType] ?? `Action #${actionType}`;
}

export default function ActionsTab() {
  const [actions, setActions] = useState<ChatModerationActionDto[]>([]);
  const [page, setPage] = useState(0);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    setLoading(true);
    getActions(undefined, page)
      .then(setActions)
      .catch(() => setActions([]))
      .finally(() => setLoading(false));
  }, [page]);

  useEffect(load, [load]);

  return (
    <div>
      {loading && <div className="rgchat-convo__sub">Loading…</div>}
      {!loading && actions.length === 0 && <div className="rgchat-convo__sub">No moderation activity.</div>}

      <table className="rgchat-table">
        <thead>
          <tr>
            <th>Action</th>
            <th>Target</th>
            <th>Reason</th>
            <th>When</th>
          </tr>
        </thead>
        <tbody>
          {actions.map((action) => (
            <tr key={action.ChatModerationActionId}>
              <td>{actionLabel(action.ActionType)}</td>
              <td>{action.TargetUserId ?? (action.TargetUnitId ? `Unit ${action.TargetUnitId}` : action.ChatMessageId ? 'Message' : '—')}</td>
              <td>{action.Reason || '—'}</td>
              <td>{formatRelativeDay(action.PerformedOn)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={page === 0} onClick={() => setPage((value) => Math.max(0, value - 1))}>
          Previous
        </button>
        <span className="rgchat-convo__sub" style={{ alignSelf: 'center' }}>Page {page + 1}</span>
        <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={actions.length < 50} onClick={() => setPage((value) => value + 1)}>
          Next
        </button>
      </div>
    </div>
  );
}
