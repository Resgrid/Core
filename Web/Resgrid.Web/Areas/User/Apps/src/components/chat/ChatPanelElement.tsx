import { useEffect, useState } from 'react';
import './chat.css';
import { getCurrentUserId, type ChatChannelDto, type ChatMessageDto } from './types';
import { useChatBootstrap } from './useChatBootstrap';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import { setActiveChannel } from './chatStore';
import { flagChatMessage } from './chatActions';
import { channelDisplayName } from './chatFormat';
import ChannelList from './ChannelList';
import ConversationView from './ConversationView';
import ThreadPanel from './ThreadPanel';
import NewConversationDialog from './NewConversationDialog';
import FlagDialog from './FlagDialog';
import { NoticeToast, AuthErrorNotice } from './atoms/StatusBanners';

export interface ChatPanelElementProps {
  hostElement?: HTMLElement;
  // Localized label supplied by the Razor host via the element attribute (commonLocalizer).
  label?: string;
}

export default function ChatPanelElement({ hostElement, label = 'Chat' }: ChatPanelElementProps) {
  const { available, loaded, loadFailed, reload, connect } = useChatBootstrap();
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

  // Feature-flag gate: the footer button stays hidden until the server confirms the
  // Chat.System flag is on for this department (GetChannels 404s when it is off).
  const chatReady = loaded && available;

  useEffect(() => {
    if (hostElement) {
      hostElement.style.display = chatReady ? '' : 'none';
    }
  }, [hostElement, chatReady]);

  const openPanel = () => {
    setOpen(true);
    // Lazy realtime: the hub only connects the first time the panel is opened.
    connect();
  };

  const openChannel = (channelId: string) => {
    setActiveChannelId(channelId);
    setActiveChannel(channelId);
    setThread(null);
  };

  if (!chatReady) {
    return null;
  }

  if (!open) {
    // Collapsed state renders inline inside the site footer (see _Footer.cshtml) so the
    // button never overlaps page content the way the old floating FAB did.
    return (
      <button type="button" className="rgchat-root rgchat-footerbtn" aria-label={label} onClick={openPanel}>
        <span aria-hidden="true">💬</span>
        <span>{label}</span>
        {unread > 0 && (
          <span className="rgchat-footerbtn__badge" aria-live="polite">
            <span className="rgchat-sr-only">{unread} unread messages</span>
            <span aria-hidden="true">{unread > 99 ? '99+' : unread}</span>
          </span>
        )}
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
              aria-label="Back to conversations"
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
            <span aria-hidden="true">💬</span>
            <span>{activeChannel ? channelDisplayName(activeChannel) : label}</span>
          </div>
          {!activeChannel && (
            <button type="button" className="rgchat-iconbtn" title="New conversation" aria-label="New conversation" onClick={() => setShowNew(true)}>
              ＋
            </button>
          )}
          <button type="button" className="rgchat-iconbtn" title="Minimize" aria-label="Minimize chat" onClick={() => setOpen(false)}>
            —
          </button>
        </div>

        <div className="rgchat-panel__body">
          <AuthErrorNotice />
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
              <ChannelList
                channels={channels}
                activeChannelId={activeChannelId}
                filter={search}
                loading={!loaded}
                loadFailed={loadFailed}
                onRetry={reload}
                onSelect={openChannel}
              />
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

      <NoticeToast />
    </div>
  );
}
