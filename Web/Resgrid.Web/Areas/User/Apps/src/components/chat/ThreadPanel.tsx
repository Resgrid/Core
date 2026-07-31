import { useEffect, useState } from 'react';
import { getThread, sendMessage } from './chatApi';
import { newClientMessageId } from './chatFormat';
import { toggleReaction, saveMessageEdit } from './chatActions';
import Composer, { type ComposerSendPayload } from './atoms/Composer';
import MessageBubble from './atoms/MessageBubble';
import type { ChatChannelDto, ChatMessageDto } from './types';

interface ThreadPanelProps {
  channel: ChatChannelDto;
  rootMessage: ChatMessageDto;
  currentUserId: string;
  onClose: () => void;
}

export default function ThreadPanel({ channel, rootMessage, currentUserId, onClose }: ThreadPanelProps) {
  const [replies, setReplies] = useState<ChatMessageDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    getThread(rootMessage.ChatMessageId)
      .then((result) => {
        if (active) {
          setReplies([...result].sort((a, b) => a.MessageSeq - b.MessageSeq));
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
  }, [rootMessage.ChatMessageId]);

  const handleSend = async (payload: ComposerSendPayload) => {
    const created = await sendMessage(channel.ChatChannelId, {
      Body: payload.body,
      MessageType: payload.messageType,
      Priority: payload.priority,
      MetadataJson: payload.metadataJson,
      ClientMessageId: newClientMessageId(),
      ThreadRootMessageId: rootMessage.ChatMessageId,
      AlsoSendToChannel: false,
    });
    if (created) {
      setReplies((current) => [...current, created]);
    }
  };

  return (
    <div className="rgchat-convo">
      <div className="rgchat-convo__head">
        <button type="button" className="rgchat-iconbtn" onClick={onClose} title="Close thread">
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
          onReact={(target, emoji, mine) => void toggleReaction(target, emoji, mine)}
          onSaveEdit={(target, body) => void saveMessageEdit(target, body)}
        />
        <div className="rgchat-daydivider">Replies</div>
        {loading && <div className="rgchat-convo__sub" style={{ textAlign: 'center' }}>Loading…</div>}
        {replies.map((reply) => (
          <MessageBubble
            key={reply.ChatMessageId}
            message={reply}
            currentUserId={currentUserId}
            showAuthor
            onReact={(target, emoji, mine) => void toggleReaction(target, emoji, mine)}
            onSaveEdit={(target, body) => void saveMessageEdit(target, body)}
          />
        ))}
      </div>

      <Composer onSend={handleSend} onTyping={() => undefined} placeholder="Reply in thread…" allowUrgent={false} />
    </div>
  );
}
