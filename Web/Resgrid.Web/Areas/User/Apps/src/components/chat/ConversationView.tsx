import { useCallback, useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { ChatMessageType, getCurrentDisplayName, type ChatChannelDto, type ChatMessageDto } from './types';
import { chatHub } from './chatHub';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import { chatStore, setHighlightMessage, type TypingEntry } from './chatStore';
import {
  acknowledgeMessage,
  discardFailedMessage,
  loadInitialMessages,
  loadOlderMessages,
  markConversationRead,
  removeMessage,
  retryFailedMessage,
  saveMessageEdit,
  seedPresenceFor,
  sendComposerMessage,
  setPinned,
  toggleReaction,
} from './chatActions';
import { channelDisplayName, formatRelativeDay } from './chatFormat';
import Composer, { type ComposerSendPayload } from './atoms/Composer';
import MessageBubble from './atoms/MessageBubble';
import TypingRow from './atoms/TypingRow';
import Lightbox from './atoms/Lightbox';
import { AuthErrorNotice, ConnectionBanner } from './atoms/StatusBanners';

const EMPTY_MESSAGES: ChatMessageDto[] = [];
const EMPTY_TYPING: TypingEntry[] = [];

interface ConversationViewProps {
  channel: ChatChannelDto;
  currentUserId: string;
  canModerate?: boolean;
  onOpenThread?: (message: ChatMessageDto) => void;
  onFlag?: (message: ChatMessageDto) => void;
  headerRight?: ReactNode;
  onBack?: () => void;
  variant?: 'default' | 'bot';
  sendOverride?: (payload: ComposerSendPayload) => void | Promise<void>;
}

function SkeletonRows() {
  return (
    <div className="rgchat-skeletons" aria-hidden="true">
      {[72, 88, 60, 80].map((width, index) => (
        <div key={index} className="rgchat-skeletonrow">
          <span className="rgchat-skeleton rgchat-skeleton--avatar" />
          <span className="rgchat-skeleton rgchat-skeleton--bubble" style={{ width: `${width}%` }} />
        </div>
      ))}
    </div>
  );
}

export default function ConversationView(props: ConversationViewProps) {
  const { channel, currentUserId, canModerate, variant } = props;
  const channelId = channel.ChatChannelId;

  const allMessages = useChatStore((state) => state.messagesByChannel[channelId] ?? EMPTY_MESSAGES, shallowArrayEqual);
  const hasMore = useChatStore((state) => state.hasMoreByChannel[channelId] ?? false);
  const typing = useChatStore((state) => state.typingByChannel[channelId] ?? EMPTY_TYPING, shallowArrayEqual);
  const botTyping = useChatStore((state) => state.botTypingByChannel[channelId] ?? false);
  const onlineUserIds = useChatStore((state) => state.onlineUserIds, shallowArrayEqual);
  const pendingAcks = useChatStore((state) => state.pendingAckMessageIds, shallowArrayEqual);
  const highlightMessageId = useChatStore((state) => state.highlightMessageId);

  // Defensive: thread-only replies must never render in the main channel list.
  const messages = allMessages.filter((message) => message.ThreadRootMessageId == null || message.AlsoSendToChannel);

  const [loading, setLoading] = useState(true);
  const [loadingOlder, setLoadingOlder] = useState(false);
  const [lightboxUrl, setLightboxUrl] = useState<string | null>(null);
  const [unseenCount, setUnseenCount] = useState(0);
  const [showJump, setShowJump] = useState(false);
  const scrollRef = useRef<HTMLDivElement | null>(null);
  const previousCountRef = useRef(0);
  const nearBottomRef = useRef(true);
  const firstUnseenIdRef = useRef<string | null>(null);
  const displayNameRef = useRef(getCurrentDisplayName());

  useEffect(() => {
    let active = true;
    setLoading(true);
    previousCountRef.current = 0;
    nearBottomRef.current = true;
    firstUnseenIdRef.current = null;
    setUnseenCount(0);
    setShowJump(false);
    // Ensure hub membership (idempotent). We intentionally do not leave on unmount so background
    // unread badges keep updating while the conversation is closed.
    void chatHub.joinChannel(channelId);
    loadInitialMessages(channelId)
      .then(() => {
        const loaded = chatStore.getState().messagesByChannel[channelId] ?? [];
        void seedPresenceFor(loaded.map((message) => message.SenderUserId));
      })
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

  // Keep the viewport pinned to the newest message when the user is already near the bottom
  // (or sent the new message themselves); otherwise accrue an unseen count.
  useLayoutEffect(() => {
    const container = scrollRef.current;
    if (!container) {
      return;
    }
    const grew = messages.length > previousCountRef.current;
    const added = grew ? messages.slice(previousCountRef.current) : [];
    previousCountRef.current = messages.length;
    if (!grew) {
      return;
    }
    const ownSend = added.some((message) => !!message.SenderUserId && message.SenderUserId === currentUserId);
    if (nearBottomRef.current || ownSend) {
      container.scrollTop = container.scrollHeight;
      firstUnseenIdRef.current = null;
      setUnseenCount(0);
    } else {
      if (!firstUnseenIdRef.current) {
        firstUnseenIdRef.current = added[0]?.ChatMessageId ?? null;
      }
      setUnseenCount((count) => count + added.length);
    }
  }, [messages, currentUserId]);

  // Advance the read pointer to the newest real message while this conversation is open.
  useEffect(() => {
    const realMessages = messages.filter((message) => message.MessageSeq < Number.MAX_SAFE_INTEGER - 100000);
    const last = realMessages[realMessages.length - 1];
    if (last) {
      markConversationRead(channel, last.MessageSeq);
    }
  }, [messages, channel]);

  // Search jump: scroll to + flash the matched message once it is loaded.
  useEffect(() => {
    if (loading || !highlightMessageId || !messages.some((message) => message.ChatMessageId === highlightMessageId)) {
      return;
    }
    const element = document.getElementById(`rgchat-msg-${highlightMessageId}`);
    element?.scrollIntoView({ block: 'center' });
    const timer = setTimeout(() => setHighlightMessage(null), 2400);
    return () => clearTimeout(timer);
  }, [loading, highlightMessageId, messages]);

  const handleScroll = () => {
    const container = scrollRef.current;
    if (!container) {
      return;
    }
    const distanceFromBottom = container.scrollHeight - container.scrollTop - container.clientHeight;
    nearBottomRef.current = distanceFromBottom < 80;
    setShowJump(distanceFromBottom > 300);
    if (nearBottomRef.current && unseenCount > 0) {
      firstUnseenIdRef.current = null;
      setUnseenCount(0);
    }
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

  const jumpToLatest = () => {
    const container = scrollRef.current;
    if (!container) {
      return;
    }
    container.scrollTo({ top: container.scrollHeight, behavior: 'smooth' });
    firstUnseenIdRef.current = null;
    setUnseenCount(0);
  };

  const handleSend = useCallback(
    (payload: ComposerSendPayload) => {
      if (props.sendOverride) {
        return props.sendOverride(payload);
      }
      return sendComposerMessage(channel, currentUserId, payload);
    },
    [props.sendOverride, channel, currentUserId],
  );

  const handleTyping = useCallback(
    (isTyping: boolean) => chatHub.typing(channelId, isTyping, displayNameRef.current || undefined),
    [channelId],
  );
  const handleReact = useCallback((target: ChatMessageDto, emoji: string, mine: boolean) => void toggleReaction(target, emoji, mine), []);
  const handleSaveEdit = useCallback((target: ChatMessageDto, body: string) => void saveMessageEdit(target, body), []);
  const handleDelete = useCallback((target: ChatMessageDto) => void removeMessage(target), []);
  const handlePin = useCallback((target: ChatMessageDto, pinned: boolean) => void setPinned(target, pinned), []);
  const handleOpenImage = useCallback((url: string) => setLightboxUrl(url), []);
  const handleCloseLightbox = useCallback(() => setLightboxUrl(null), []);
  const handleRetry = useCallback((target: ChatMessageDto) => void retryFailedMessage(channel, target), [channel]);
  const handleDiscard = useCallback((target: ChatMessageDto) => discardFailedMessage(target), []);

  const pendingAckMessage = messages.find(
    (message) => message.Priority === 1 && pendingAcks.includes(message.ChatMessageId),
  );

  const onlineSet = new Set(onlineUserIds);
  let lastSenderId: string | null = null;
  let lastSentOn = 0;
  let lastDayKey = '';
  let dividerRendered = false;

  return (
    <div className={`rgchat-convo${variant === 'bot' ? ' rgchat-bot' : ''}`}>
      <div className={`rgchat-convo__head${variant === 'bot' ? ' rgchat-bot__head' : ''}`}>
        {props.onBack && (
          <button type="button" className="rgchat-iconbtn rgchat-iconbtn--inherit" onClick={props.onBack} title="Back" aria-label="Back">
            ‹
          </button>
        )}
        <div className="rgchat-convo__head-body">
          <div className="rgchat-convo__title">{channelDisplayName(channel)}</div>
          <div className="rgchat-convo__sub">
            {channel.Topic ? channel.Topic : channel.IsLocked ? 'Locked' : ''}
          </div>
        </div>
        {props.headerRight}
      </div>

      <ConnectionBanner />
      <AuthErrorNotice />

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
        {loadingOlder && (
          <div className="rgchat-skeletonrow" aria-hidden="true">
            <span className="rgchat-skeleton rgchat-skeleton--avatar" />
            <span className="rgchat-skeleton rgchat-skeleton--bubble" style={{ width: '64%' }} />
          </div>
        )}

        {loading && messages.length === 0 && <SkeletonRows />}

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

          const showNewDivider = !dividerRendered && firstUnseenIdRef.current === message.ChatMessageId;
          if (showNewDivider) {
            dividerRendered = true;
          }

          return (
            <div key={message.ChatMessageId}>
              {showDivider && <div className="rgchat-daydivider">{formatRelativeDay(message.SentOn)}</div>}
              {showNewDivider && (
                <div className="rgchat-newdivider">
                  <span>New messages</span>
                </div>
              )}
              <MessageBubble
                message={message}
                currentUserId={currentUserId}
                showAuthor={showAuthor}
                online={!!message.SenderUserId && onlineSet.has(message.SenderUserId)}
                canModerate={canModerate}
                variant={variant}
                highlighted={message.ChatMessageId === highlightMessageId}
                onReact={handleReact}
                onOpenThread={variant === 'bot' ? undefined : props.onOpenThread}
                onSaveEdit={handleSaveEdit}
                onDelete={handleDelete}
                onPin={canModerate ? handlePin : undefined}
                onFlag={variant === 'bot' ? undefined : props.onFlag}
                onOpenImage={handleOpenImage}
                onRetrySend={variant === 'bot' ? undefined : handleRetry}
                onDiscardFailed={handleDiscard}
              />
            </div>
          );
        })}
      </div>

      {showJump && (
        <button type="button" className="rgchat-jumppill" onClick={jumpToLatest}>
          ↓ {unseenCount > 0 ? `${unseenCount} new — ` : ''}Jump to latest
        </button>
      )}

      <TypingRow entries={typing} botTyping={botTyping} />

      <Composer
        onSend={handleSend}
        onTyping={handleTyping}
        allowUrgent={variant !== 'bot'}
        allowGifs={variant !== 'bot'}
        allowImages={variant !== 'bot'}
        placeholder={variant === 'bot' ? 'Ask the assistant…' : 'Write a message…'}
      />

      {lightboxUrl && <Lightbox url={lightboxUrl} onClose={handleCloseLightbox} />}
    </div>
  );
}
