import { useCallback, useEffect, useRef, useState } from 'react';
import './chat.css';
import { ChatChannelType, getCurrentUserId, type ChatChannelDto, type ChatbotChannelInfo, type ChatMessageDto } from './types';
import { getChatbotChannel, sendChatbotMessage, newChatbotSession } from './chatApi';
import { createOptimisticMessage, markMessageFailed, setBotTyping, upsertMessage } from './chatStore';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import { newClientMessageId } from './chatFormat';
import ConversationView from './ConversationView';
import type { ComposerSendPayload } from './atoms/Composer';

const EMPTY_MESSAGES: ChatMessageDto[] = [];
const BOT_TYPING_TIMEOUT_MS = 60000;

export interface ChatbotElementProps {
  hostElement?: HTMLElement;
}

function toChannelDto(info: ChatbotChannelInfo): ChatChannelDto {
  return {
    ChatChannelId: info.ChatChannelId,
    ChannelType: ChatChannelType.Chatbot,
    Name: info.Name,
    Topic: 'Resgrid AI assistant',
    GroupId: null,
    CallId: null,
    CommandStructureNodeId: null,
    OwnerUserId: null,
    IsArchived: false,
    IsLocked: false,
    LastMessageSeq: info.LastMessageSeq,
    LastMessageOn: info.LastMessageOn,
    CreatedOn: new Date().toISOString(),
    UnreadCount: 0,
    NotificationPreference: 0,
    MyLastReadSeq: 0,
  };
}

export default function ChatbotElement(_props: ChatbotElementProps) {
  const [channel, setChannel] = useState<ChatChannelDto | null>(null);
  const [available, setAvailable] = useState(true);
  const [ready, setReady] = useState(false);

  const currentUserId = getCurrentUserId();
  const channelId = channel?.ChatChannelId ?? '';

  const messages = useChatStore((state) => (channelId ? state.messagesByChannel[channelId] ?? EMPTY_MESSAGES : EMPTY_MESSAGES), shallowArrayEqual);
  const botTypingRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    getChatbotChannel()
      .then((info) => {
        if (info) {
          setChannel(toChannelDto(info));
        } else {
          setAvailable(false);
        }
      })
      .catch(() => setAvailable(false))
      .finally(() => setReady(true));
    return () => {
      if (botTypingRef.current) {
        clearTimeout(botTypingRef.current);
      }
    };
  }, []);

  // Hub echo (bot reply rendered) clears the client-side typing timeout.
  useEffect(() => {
    if (!channelId || !botTypingRef.current) {
      return;
    }
    const last = messages[messages.length - 1];
    if (last && last.SenderUserId !== currentUserId) {
      clearTimeout(botTypingRef.current);
      botTypingRef.current = null;
      setBotTyping(channelId, false);
    }
  }, [messages, channelId, currentUserId]);

  const handleSend = useCallback(
    async (payload: ComposerSendPayload) => {
      if (!channelId || payload.body.trim().length === 0) {
        return;
      }
      const clientMessageId = newClientMessageId();
      upsertMessage(createOptimisticMessage(channelId, 0, currentUserId, 'You', payload.body, clientMessageId));
      setBotTyping(channelId, true);
      if (botTypingRef.current) {
        clearTimeout(botTypingRef.current);
      }
      // Safety: never leave the typing row stuck if the hub echo never arrives.
      botTypingRef.current = setTimeout(() => {
        botTypingRef.current = null;
        setBotTyping(channelId, false);
      }, BOT_TYPING_TIMEOUT_MS);
      try {
        await sendChatbotMessage(payload.body, clientMessageId);
      } catch (error) {
        console.error('Failed to message the assistant.', error);
        markMessageFailed(channelId, clientMessageId);
        if (botTypingRef.current) {
          clearTimeout(botTypingRef.current);
          botTypingRef.current = null;
        }
        setBotTyping(channelId, false);
      }
    },
    [channelId, currentUserId],
  );

  const startNewConversation = async () => {
    try {
      await newChatbotSession();
    } catch (error) {
      console.error('Failed to start a new assistant session.', error);
    }
  };

  if (ready && !available) {
    return (
      <div className="rgchat-root">
        <div className="rgchat-empty">The assistant is not enabled for this department.</div>
      </div>
    );
  }

  if (!channel) {
    return (
      <div className="rgchat-root">
        <div className="rgchat-convo rgchat-bot rgchat-botframe">
          <div className="rgchat-skeletons" aria-hidden="true">
            {[70, 85, 60].map((width, index) => (
              <div key={index} className="rgchat-skeletonrow">
                <span className="rgchat-skeleton rgchat-skeleton--avatar" />
                <span className="rgchat-skeleton rgchat-skeleton--bubble" style={{ width: `${width}%` }} />
              </div>
            ))}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="rgchat-root">
      <div className="rgchat-botframe">
        <ConversationView
          channel={channel}
          currentUserId={currentUserId}
          variant="bot"
          sendOverride={handleSend}
          headerRight={
            <button
              type="button"
              className="rgchat-iconbtn rgchat-iconbtn--inherit"
              title="New conversation"
              aria-label="New assistant conversation"
              onClick={() => void startNewConversation()}
            >
              ＋
            </button>
          }
        />
      </div>
    </div>
  );
}
