import { useCallback, useEffect, useState } from 'react';
import { getThread, sendMessage } from './chatApi';
import { newClientMessageId } from './chatFormat';
import { toggleReaction, saveMessageEdit } from './chatActions';
import { setThreadMessages, upsertThreadMessage } from './chatStore';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import Composer, { type ComposerSendPayload } from './atoms/Composer';
import MessageBubble from './atoms/MessageBubble';
import type { ChatChannelDto, ChatMessageDto } from './types';

interface ThreadPanelProps {
  channel: ChatChannelDto;
  rootMessage: ChatMessageDto;
  currentUserId: string;
  onClose: () => void;
}

const EMPTY: ChatMessageDto[] = [];

export default function ThreadPanel({ channel, rootMessage, currentUserId, onClose }: ThreadPanelProps) {
  const rootId = rootMessage.ChatMessageId;
  // Live replies: hub thread events are routed here by the store; the REST fetch seeds/merges.
  const replies = useChatStore((state) => state.threadMessagesByRoot[rootId] ?? EMPTY, shallowArrayEqual);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    getThread(rootId)
      .then((result) => {
        if (active) {
          setThreadMessages(rootId, result);
        }
      })
      .catch((error) => console.error('Failed to load thread.', error))
      .finally(() => {
        if (active) {
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [rootId]);

  const handleSend = async (payload: ComposerSendPayload) => {
    const clientMessageId = newClientMessageId();
    const created = await sendMessage(channel.ChatChannelId, {
      Body: payload.body,
      MessageType: payload.messageType,
      Priority: payload.priority,
      MetadataJson: payload.metadataJson,
      ClientMessageId: clientMessageId,
      ThreadRootMessageId: rootId,
      AlsoSendToChannel: false,
    });
    if (created) {
      // Dedupes against the hub echo by ClientMessageId inside the store merge.
      upsertThreadMessage(rootId, created);
    }
  };

  const handleReact = useCallback((target: ChatMessageDto, emoji: string, mine: boolean) => void toggleReaction(target, emoji, mine), []);
  const handleSaveEdit = useCallback((target: ChatMessageDto, body: string) => void saveMessageEdit(target, body), []);

  return (
    <div className="rgchat-convo">
      <div className="rgchat-convo__head">
        <button type="button" className="rgchat-iconbtn" onClick={onClose} title="Close thread" aria-label="Close thread">
          ‹
        </button>
        <div className="rgchat-convo__head-body">
          <div className="rgchat-convo__title">Thread</div>
          <div className="rgchat-convo__sub">{replies.length} {replies.length === 1 ? 'reply' : 'replies'}</div>
        </div>
      </div>

      <div className="rgchat-convo__scroll">
        <MessageBubble
          message={rootMessage}
          currentUserId={currentUserId}
          showAuthor
          onReact={handleReact}
          onSaveEdit={handleSaveEdit}
        />
        <div className="rgchat-daydivider">Replies</div>
        {loading && replies.length === 0 && (
          <div className="rgchat-skeletonrow" aria-hidden="true">
            <span className="rgchat-skeleton rgchat-skeleton--avatar" />
            <span className="rgchat-skeleton rgchat-skeleton--bubble" style={{ width: '70%' }} />
          </div>
        )}
        {replies.map((reply) => (
          <MessageBubble
            key={reply.ChatMessageId}
            message={reply}
            currentUserId={currentUserId}
            showAuthor
            onReact={handleReact}
            onSaveEdit={handleSaveEdit}
          />
        ))}
      </div>

      <Composer onSend={handleSend} onTyping={() => undefined} placeholder="Reply in thread…" allowUrgent={false} />
    </div>
  );
}
