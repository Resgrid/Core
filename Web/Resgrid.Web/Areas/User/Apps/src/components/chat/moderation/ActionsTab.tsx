import { useCallback, useEffect, useState } from 'react';
import { getActions } from '../chatModerationApi';
import { formatRelativeDay } from '../chatFormat';
import type { ChatModerationActionDto } from '../types';
import { moderationText } from '../moderationI18n';

const ACTION_LABEL_KEYS: Record<number, string> = {
  0: 'LegacyActionDeleteMessage',
  1: 'LegacyActionMuteUser',
  2: 'LegacyActionBanUser',
  3: 'LegacyActionUnbanUser',
  4: 'LegacyActionLockChannel',
  5: 'LegacyActionUnlockChannel',
  6: 'LegacyActionResolveFlag',
};

function actionLabel(actionType: number): string {
  return ACTION_LABEL_KEYS[actionType]
    ? moderationText(ACTION_LABEL_KEYS[actionType])
    : moderationText('ActionNumberFormat', actionType);
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
      {loading && <div className="rgchat-convo__sub">{moderationText('Loading')}</div>}
      {!loading && actions.length === 0 && <div className="rgchat-convo__sub">{moderationText('NoModerationActivity')}</div>}

      <table className="rgchat-table">
        <thead>
          <tr>
            <th>{moderationText('Action')}</th>
            <th>{moderationText('Target')}</th>
            <th>{moderationText('Reason')}</th>
            <th>{moderationText('When')}</th>
          </tr>
        </thead>
        <tbody>
          {actions.map((action) => (
            <tr key={action.ChatModerationActionId}>
              <td>{actionLabel(action.ActionType)}</td>
              <td>{action.TargetUserId ?? (action.TargetUnitId ? moderationText('UnitFormat', action.TargetUnitId) : action.ChatMessageId ? moderationText('ItemTypeMessage') : moderationText('NoValue'))}</td>
              <td>{action.Reason || moderationText('NoValue')}</td>
              <td>{formatRelativeDay(action.PerformedOn)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={page === 0} onClick={() => setPage((value) => Math.max(0, value - 1))}>
          {moderationText('Previous')}
        </button>
        <span className="rgchat-convo__sub" style={{ alignSelf: 'center' }}>{moderationText('PageFormat', page + 1)}</span>
        <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={actions.length < 50} onClick={() => setPage((value) => value + 1)}>
          {moderationText('Next')}
        </button>
      </div>
    </div>
  );
}
