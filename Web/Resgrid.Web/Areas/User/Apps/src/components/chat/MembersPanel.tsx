import { useCallback, useEffect, useState } from 'react';
import { getMembers, getPersonnelRecipients, removeMember } from './chatApi';
import { banUser, muteUser } from './chatModerationApi';
import { seedPresenceFor } from './chatActions';
import { setChannelMembers } from './chatStore';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import Avatar from './atoms/Avatar';
import type { ChatMemberDto } from './types';

interface MembersPanelProps {
  channelId: string;
  canModerate: boolean;
  currentUserId: string;
}

const MUTE_DURATION_MS = 24 * 60 * 60 * 1000;
const EMPTY_MEMBERS: ChatMemberDto[] = [];

export default function MembersPanel({ channelId, canModerate, currentUserId }: MembersPanelProps) {
  const members = useChatStore((state) => state.membersByChannel[channelId] ?? EMPTY_MEMBERS, shallowArrayEqual);
  const online = useChatStore((state) => state.onlineUserIds, shallowArrayEqual);
  const [names, setNames] = useState<Record<string, string>>({});
  const [busyUserId, setBusyUserId] = useState<string | null>(null);

  const load = useCallback(async () => {
    // Independent loads: a failure of one must not drop the other (members is the primary data,
    // personnel names are display enrichment), so settle each and handle it on its own.
    const [membersResult, personnelResult] = await Promise.allSettled([getMembers(channelId), getPersonnelRecipients()]);

    if (membersResult.status === 'fulfilled') {
      setChannelMembers(channelId, membersResult.value);
      void seedPresenceFor(membersResult.value.map((member) => member.UserId));
    } else {
      // Leave any already-loaded members in place rather than wiping the panel on a transient error.
      console.error('Failed to load channel members.', membersResult.reason);
    }

    if (personnelResult.status === 'fulfilled') {
      const nameMap: Record<string, string> = {};
      for (const person of personnelResult.value) {
        nameMap[person.userId] = person.name;
      }
      setNames(nameMap);
    } else {
      console.error('Failed to load personnel names.', personnelResult.reason);
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
              <div className="rgchat-member__actions">
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
