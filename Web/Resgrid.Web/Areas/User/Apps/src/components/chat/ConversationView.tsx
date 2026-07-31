import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { ChatMessageType, type ChatChannelDto, type ChatMessageDto } from './types';
import { chatHub } from './chatHub';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import type { TypingEntry } from './chatStore';
import {
  acknowledgeMessage,
  loadInitialMessages,
  loadOlderMessages,
  markConversationRead,
  removeMessage,
  saveMessageEdit,
  sendComposerMessage,
  setPinned,
  toggleReaction,
} from './chatActions';
import { channelDisplayName, formatRelativeDay } from './chatFormat';
import Composer, { type ComposerSendPayload } from './atoms/Composer';
import MessageBubble from './atoms/MessageBubble';
import TypingRow from './atoms/TypingRow';

const EMPTY_MESSAGES: ChatMessageDto[] = [];
const EMPTY_TYPING: TypingEntry[] = [];
const EMPTY_STRINGS: string[] = [];

interface ConversationViewProps {
  channel: ChatChannelDto;
  currentUserId: string;
  canModerate?: boolean;
  memberCount?: number;
  onOpenThread?: (message: ChatMessageDto) => void;
  onFlag?: (message: ChatMessageDto) => void;
  headerRight?: ReactNode;
  onBack?: () => void;
  variant?: 'default' | 'bot';
}

export default function ConversationView(props: ConversationViewProps) {
  const { channel, currentUserId, canModerate, variant } = props;
  const channelId = channel.ChatChannelId;

  const messages = useChatStore((state) => state.messagesByChannel[channelId] ?? EMPTY_MESSAGES, shallowArrayEqual);
  const hasMore = useChatStore((state) => state.hasMoreByChannel[channelId] ?? false);
  const typing = useChatStore((state) => state.typingByChannel[channelId] ?? EMPTY_TYPING, shallowArrayEqual);
  const botTyping = useChatStore((state) => state.botTypingByChannel[channelId] ?? false);
  const onlineUserIds = useChatStore((state) => state.onlineUserIds, shallowArrayEqual);
  const pendingAcks = useChatStore((state) => state.pendingAckMessageIds, shallowArrayEqual);

  const [loading, setLoading] = useState(true);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const previousCountRef = useRef(0);
  const nearBottomRef = useRef(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    // Ensure hub membership (idempotent). We intentionally do not leave on unmount so background
    // unread badges keep updating while the conversation is closed.
    void chatHub.joinChannel(channelId);
    loadInitialMessages(channelId)
      .catch((error) => console.error('Failed to load messages.', error))
      .finally(() => {
        if (active) {
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [channelId]);

  // Keep the viewport pinned to the newest message when the user is already near the bottom.
  useLayoutEffect(() => {
    const container = scrollRef.current;
    if (!container) {
      return;
    }
    const grew = messages.length > previousCountRef.current;
    previousCountRef.current = messages.length;
    if (grew && nearBottomRef.current) {
      container.scrollTop = container.scrollHeight;
    }
  }, [messages]);

  // Advance the read pointer to the newest real message while this conversation is open.
  useEffect(() => {
    const realMessages = messages.filter((message) => message.MessageSeq < Number.MAX_SAFE_INTEGER - 100000);
    const last = realMessages[realMessages.length - 1];
    if (last) {
      markConversationRead(channel, last.MessageSeq);
    }
  }, [messages, channel]);

  const handleScroll = () => {
    const container = scrollRef.current;
    if (!container) {
      return;
    }
    nearBottomRef.current = container.scrollHeight - container.scrollTop - container.clientHeight < 80;
    if (container.scrollTop < 40 && hasMore && !loadingOlder) {
      setLoadingOlder(true);
      const previousHeight = container.scrollHeight;
      loadOlderMessages(channelId)
        .catch((error) => console.error('Failed to load older messages.', error))
        .finally(() => {
          setLoadingOlder(false);
          requestAnimationFrame(() => {
            if (scrollRef.current) {
              scrollRef.current.scrollTop = scrollRef.current.scrollHeight - previousHeight;
            }
          });
        });
    }
  };

  const handleSend = (payload: ComposerSendPayload) => sendComposerMessage(channel, currentUserId, payload);

  const pendingAckMessage = messages.find(
    (message) => message.Priority === 1 && pendingAcks.includes(message.ChatMessageId),
  );

  const onlineSet = new Set(onlineUserIds);
  let lastSenderId: string | null = null;
  let lastSentOn = 0;
  let lastDayKey = '';

  return (
    <div className={`rgchat-convo${variant === 'bot' ? ' rgchat-bot' : ''}`}>
      <div className={`rgchat-convo__head${variant === 'bot' ? ' rgchat-bot__head' : ''}`}>
        {props.onBack && (
          <button type="button" className="rgchat-iconbtn" onClick={props.onBack} title="Back" style={{ color: 'inherit' }}>
            ‹
          </button>
        )}
        <div className="rgchat-convo__head-body">
          <div className="rgchat-convo__title">{channelDisplayName(channel)}</div>
          <div className="rgchat-convo__sub">
            {channel.Topic
              ? channel.Topic
              : props.memberCount !== undefined
                ? `${props.memberCount} member${props.memberCount === 1 ? '' : 's'}`
                : channel.IsLocked
                  ? 'Locked'
                  : ''}
          </div>
        </div>
        {props.headerRight}
      </div>

      {pendingAckMessage && (
        <div className="rgchat-ackbanner">
          <span>⚠ Urgent message requires your acknowledgment</span>
          <button type="button" onClick={() => void acknowledgeMessage(pendingAckMessage)}>
            Acknowledge
          </button>
        </div>
      )}

      <div className="rgchat-convo__scroll" ref={scrollRef} onScroll={handleScroll}>
        {hasMore && (
          <button type="button" className="rgchat-loadmore" onClick={handleScroll} disabled={loadingOlder}>
            {loadingOlder ? 'Loading…' : 'Load earlier messages'}
          </button>
        )}

        {loading && messages.length === 0 && <div className="rgchat-convo__sub" style={{ textAlign: 'center' }}>Loading…</div>}

        {messages.map((message) => {
          const sentOn = new Date(message.SentOn).getTime();
          const dayKey = new Date(message.SentOn).toDateString();
          const showDivider = dayKey !== lastDayKey;
          const showAuthor =
            showDivider || message.SenderUserId !== lastSenderId || sentOn - lastSentOn > 5 * 60 * 1000;
          lastSenderId = message.SenderUserId;
          lastSentOn = sentOn;
          lastDayKey = dayKey;

          if (message.MessageType === ChatMessageType.System) {
            return (
              <div key={message.ChatMessageId} className="rgchat-daydivider">
                {message.Body}
              </div>
            );
          }

          return (
            <div key={message.ChatMessageId}>
              {showDivider && <div className="rgchat-daydivider">{formatRelativeDay(message.SentOn)}</div>}
              <MessageBubble
                message={message}
                currentUserId={currentUserId}
                showAuthor={showAuthor}
                online={!!message.SenderUserId && onlineSet.has(message.SenderUserId)}
                canModerate={canModerate}
                variant={variant}
                onReact={(target, emoji, mine) => void toggleReaction(target, emoji, mine)}
                onOpenThread={variant === 'bot' ? undefined : props.onOpenThread}
                onSaveEdit={(target, body) => void saveMessageEdit(target, body)}
                onDelete={(target) => void removeMessage(target)}
                onPin={canModerate ? (target, pinned) => void setPinned(target, pinned) : undefined}
                onFlag={variant === 'bot' ? undefined : props.onFlag}
              />
            </div>
          );
        })}
      </div>

      <TypingRow entries={typing} botTyping={botTyping} />

      <Composer
        onSend={handleSend}
        onTyping={(isTyping) => chatHub.typing(channelId, isTyping)}
        allowUrgent={variant !== 'bot'}
        allowGifs={variant !== 'bot'}
        allowImages={variant !== 'bot'}
        placeholder={variant === 'bot' ? 'Ask the assistant…' : 'Write a message…'}
      />
    </div>
  );
}
