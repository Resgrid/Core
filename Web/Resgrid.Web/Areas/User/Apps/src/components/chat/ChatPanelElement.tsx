import { useEffect, useState } from 'react';
import './chat.css';
import { getCurrentUserId, type ChatChannelDto, type ChatMessageDto } from './types';
import { useChatBootstrap } from './useChatBootstrap';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import { setActiveChannel } from './chatStore';
import { flagChatMessage } from './chatActions';
import ChannelList from './ChannelList';
import ConversationView from './ConversationView';
import ThreadPanel from './ThreadPanel';
import NewConversationDialog from './NewConversationDialog';
import FlagDialog from './FlagDialog';

export interface ChatPanelElementProps {
  hostElement?: HTMLElement;
}

export default function ChatPanelElement({ hostElement }: ChatPanelElementProps) {
  const { available, loaded } = useChatBootstrap();
  const channels = useChatStore((state) => state.channels, shallowArrayEqual);
  const unread = useChatStore((state) => state.channels.reduce((total, channel) => total + Math.max(0, channel.UnreadCount), 0));

  const [open, setOpen] = useState(false);
  const [activeChannelId, setActiveChannelId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [showNew, setShowNew] = useState(false);
  const [thread, setThread] = useState<ChatMessageDto | null>(null);
  const [flagTarget, setFlagTarget] = useState<ChatMessageDto | null>(null);

  const currentUserId = getCurrentUserId();
  const activeChannel = channels.find((channel) => channel.ChatChannelId === activeChannelId) ?? null;

  useEffect(() => {
    if (hostElement) {
      hostElement.style.display = loaded && !available ? 'none' : '';
    }
  }, [hostElement, loaded, available]);

  const openChannel = (channelId: string) => {
    setActiveChannelId(channelId);
    setActiveChannel(channelId);
    setThread(null);
  };

  if (loaded && !available) {
    return null;
  }

  if (!open) {
    return (
      <button type="button" className="rgchat-root rgchat-fab" onClick={() => setOpen(true)}>
        <span>💬</span>
        <span>Chat</span>
        {unread > 0 && <span className="rgchat-fab__badge">{unread > 99 ? '99+' : unread}</span>}
      </button>
    );
  }

  return (
    <div className="rgchat-root">
      <div className="rgchat-panel rgchat-panel--light">
        <div className="rgchat-panel__head">
          {activeChannel && (
            <button
              type="button"
              className="rgchat-iconbtn"
              title="Back to conversations"
              onClick={() => {
                setActiveChannelId(null);
                setActiveChannel(null);
                setThread(null);
              }}
            >
              ‹
            </button>
          )}
          <div className="rgchat-panel__title">
            <span>💬</span>
            <span>{activeChannel ? '' : 'Chat'}</span>
          </div>
          {!activeChannel && (
            <button type="button" className="rgchat-iconbtn" title="New conversation" onClick={() => setShowNew(true)}>
              ＋
            </button>
          )}
          <button type="button" className="rgchat-iconbtn" title="Minimize" onClick={() => setOpen(false)}>
            —
          </button>
        </div>

        <div className="rgchat-panel__body">
          {thread && activeChannel ? (
            <ThreadPanel
              channel={activeChannel}
              rootMessage={thread}
              currentUserId={currentUserId}
              onClose={() => setThread(null)}
            />
          ) : activeChannel ? (
            <ConversationView
              channel={activeChannel}
              currentUserId={currentUserId}
              onOpenThread={(message) => setThread(message)}
              onFlag={(message) => setFlagTarget(message)}
            />
          ) : (
            <>
              <div className="rgchat-list__search">
                <input
                  className="rgchat-input"
                  placeholder="Search conversations"
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                />
              </div>
              <ChannelList channels={channels} activeChannelId={activeChannelId} filter={search} onSelect={openChannel} />
            </>
          )}
        </div>
      </div>

      {showNew && (
        <NewConversationDialog
          currentUserId={currentUserId}
          onClose={() => setShowNew(false)}
          onCreated={(channel: ChatChannelDto) => {
            setShowNew(false);
            openChannel(channel.ChatChannelId);
          }}
        />
      )}

      {flagTarget && (
        <FlagDialog
          onClose={() => setFlagTarget(null)}
          onSubmit={(reason, note) => void flagChatMessage(flagTarget, reason, note)}
        />
      )}
    </div>
  );
}
