import { useCallback, useEffect, useState } from 'react';
import { getMembers, getPersonnelRecipients, getPresence, removeMember } from './chatApi';
import { banUser, muteUser } from './chatModerationApi';
import Avatar from './atoms/Avatar';
import type { ChatMemberDto } from './types';

interface MembersPanelProps {
  channelId: string;
  canModerate: boolean;
  currentUserId: string;
}

const MUTE_DURATION_MS = 24 * 60 * 60 * 1000;

export default function MembersPanel({ channelId, canModerate, currentUserId }: MembersPanelProps) {
  const [members, setMembers] = useState<ChatMemberDto[]>([]);
  const [names, setNames] = useState<Record<string, string>>({});
  const [online, setOnline] = useState<string[]>([]);
  const [busyUserId, setBusyUserId] = useState<string | null>(null);

  const load = useCallback(async () => {
    const [memberRows, personnel] = await Promise.all([getMembers(channelId), getPersonnelRecipients().catch(() => [])]);
    setMembers(memberRows);
    const nameMap: Record<string, string> = {};
    for (const person of personnel) {
      nameMap[person.userId] = person.name;
    }
    setNames(nameMap);
    const userIds = memberRows.map((member) => member.UserId).filter((id): id is string => !!id);
    if (userIds.length > 0) {
      setOnline(await getPresence(userIds).catch(() => []));
    }
  }, [channelId]);

  useEffect(() => {
    void load();
  }, [load]);

  const nameFor = (member: ChatMemberDto): string =>
    member.DisplayNameOverride || (member.UserId ? names[member.UserId] : undefined) || 'Member';

  const runAction = async (userId: string, action: () => Promise<void>) => {
    setBusyUserId(userId);
    try {
      await action();
      await load();
    } catch (error) {
      console.error('Moderation action failed.', error);
    } finally {
      setBusyUserId(null);
    }
  };

  const onlineSet = new Set(online);

  return (
    <div className="rgchat-aside__section">
      {members.length === 0 && <div className="rgchat-convo__sub">No members.</div>}
      {members.map((member) => {
        const userId = member.UserId ?? '';
        const isMuted = !!member.MutedUntil && new Date(member.MutedUntil).getTime() > Date.now();
        return (
          <div key={member.ChatChannelMemberId} className="rgchat-member">
            <Avatar name={nameFor(member)} userId={member.UserId} size="sm" showPresence online={onlineSet.has(userId)} />
            <span className="rgchat-member__name">{nameFor(member)}</span>
            {member.IsModerator && <span className="rgchat-tag">Mod</span>}
            {member.IsBanned && <span className="rgchat-tag">Banned</span>}
            {isMuted && <span className="rgchat-tag">Muted</span>}
            {canModerate && userId && userId !== currentUserId && (
              <div style={{ display: 'flex', gap: 4 }}>
                <button
                  type="button"
                  className="rgchat-iconbtn"
                  title={isMuted ? 'Unmute' : 'Mute (24h)'}
                  disabled={busyUserId === userId}
                  onClick={() =>
                    void runAction(userId, () =>
                      muteUser(channelId, userId, isMuted ? null : new Date(Date.now() + MUTE_DURATION_MS).toISOString()),
                    )
                  }
                >
                  {isMuted ? '🔊' : '🔇'}
                </button>
                <button
                  type="button"
                  className="rgchat-iconbtn"
                  title={member.IsBanned ? 'Unban' : 'Ban'}
                  disabled={busyUserId === userId}
                  onClick={() => void runAction(userId, () => banUser(channelId, userId, !member.IsBanned))}
                >
                  🚫
                </button>
                <button
                  type="button"
                  className="rgchat-iconbtn"
                  title="Remove"
                  disabled={busyUserId === userId}
                  onClick={() => void runAction(userId, () => removeMember(channelId, userId))}
                >
                  ✕
                </button>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
