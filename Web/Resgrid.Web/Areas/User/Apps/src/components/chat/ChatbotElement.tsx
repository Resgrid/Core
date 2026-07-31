import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import './chat.css';
import { getCurrentUserId, type ChatbotChannelInfo, type ChatMessageDto } from './types';
import { getChatbotChannel, sendChatbotMessage, newChatbotSession } from './chatApi';
import { chatHub } from './chatHub';
import { createOptimisticMessage, setBotTyping, upsertMessage } from './chatStore';
import { loadInitialMessages, toggleReaction } from './chatActions';
import { newClientMessageId } from './chatFormat';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import Composer, { type ComposerSendPayload } from './atoms/Composer';
import MessageBubble from './atoms/MessageBubble';

const EMPTY: ChatMessageDto[] = [];

export interface ChatbotElementProps {
  hostElement?: HTMLElement;
}

export default function ChatbotElement(_props: ChatbotElementProps) {
  const [channel, setChannel] = useState<ChatbotChannelInfo | null>(null);
  const [available, setAvailable] = useState(true);
  const [ready, setReady] = useState(false);

  const currentUserId = getCurrentUserId();
  const channelId = channel?.ChatChannelId ?? '';

  const messages = useChatStore((state) => (channelId ? state.messagesByChannel[channelId] ?? EMPTY : EMPTY), shallowArrayEqual);
  const botTyping = useChatStore((state) => (channelId ? state.botTypingByChannel[channelId] ?? false : false));

  const scrollRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    void chatHub.acquire();
    getChatbotChannel()
      .then((info) => {
        if (info) {
          setChannel(info);
        } else {
          setAvailable(false);
        }
      })
      .catch(() => setAvailable(false))
      .finally(() => setReady(true));
    return () => {
      chatHub.release();
    };
  }, []);

  useEffect(() => {
    if (!channelId) {
      return;
    }
    void chatHub.joinChannel(channelId);
    void loadInitialMessages(channelId).catch((error) => console.error('Failed to load assistant messages.', error));
  }, [channelId]);

  useLayoutEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages, botTyping]);

  const handleSend = async (payload: ComposerSendPayload) => {
    if (!channelId || payload.body.trim().length === 0) {
      return;
    }
    const clientMessageId = newClientMessageId();
    upsertMessage(createOptimisticMessage(channelId, 0, currentUserId, 'You', payload.body, clientMessageId));
    setBotTyping(channelId, true);
    try {
      await sendChatbotMessage(payload.body, clientMessageId);
    } catch (error) {
      console.error('Failed to message the assistant.', error);
      setBotTyping(channelId, false);
    }
  };

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

  return (
    <div className="rgchat-root">
      <div className="rgchat-convo rgchat-bot" style={{ height: 'calc(100vh - 220px)', minHeight: 480, border: '1px solid var(--rgchat-border)', borderRadius: 12, overflow: 'hidden' }}>
        <div className="rgchat-convo__head rgchat-bot__head" style={{ color: '#fff' }}>
          <span className="rgchat-avatar rgchat-avatar--sm" style={{ backgroundColor: 'rgba(255,255,255,0.2)' }}>🤖</span>
          <div className="rgchat-convo__head-body">
            <div className="rgchat-convo__title" style={{ color: '#fff' }}>{channel?.Name ?? 'Assistant'}</div>
            <div className="rgchat-convo__sub" style={{ color: 'rgba(255,255,255,0.75)' }}>Resgrid AI assistant</div>
          </div>
          <button type="button" className="rgchat-iconbtn" title="New conversation" onClick={() => void startNewConversation()} style={{ color: '#fff' }}>
            ＋
          </button>
        </div>

        <div className="rgchat-convo__scroll" ref={scrollRef}>
          {!ready && <div className="rgchat-convo__sub" style={{ textAlign: 'center' }}>Loading…</div>}
          {ready && messages.length === 0 && (
            <div className="rgchat-empty">
              <div style={{ fontSize: 40 }}>🤖</div>
              <div>Ask the assistant about calls, personnel, units and more.</div>
            </div>
          )}
          {messages.map((message) => (
            <MessageBubble
              key={message.ChatMessageId}
              message={message}
              currentUserId={currentUserId}
              showAuthor
              variant="bot"
              onReact={(target, emoji, mine) => void toggleReaction(target, emoji, mine)}
            />
          ))}
          {botTyping && (
            <div className="rgchat-typing">
              <span className="rgchat-dots"><span /><span /><span /></span> Assistant is typing
            </div>
          )}
        </div>

        <Composer onSend={handleSend} onTyping={() => undefined} allowGifs={false} allowImages={false} allowUrgent={false} placeholder="Ask the assistant…" />
      </div>
    </div>
  );
}
