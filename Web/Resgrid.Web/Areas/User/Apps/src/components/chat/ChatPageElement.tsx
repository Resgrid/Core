import { useCallback, useState } from 'react';
import './chat.css';
import { getCurrentUserId, isDepartmentAdmin, type ChatChannelDto, type ChatMessageDto } from './types';
import { useChatBootstrap } from './useChatBootstrap';
import { useChatStore, shallowArrayEqual } from './useChatStore';
import { setActiveChannel, setHighlightMessage } from './chatStore';
import { flagChatMessage } from './chatActions';
import { searchMessages } from './chatApi';
import { channelDisplayName, formatRelativeDay } from './chatFormat';
import ChannelList from './ChannelList';
import ConversationView from './ConversationView';
import ThreadPanel from './ThreadPanel';
import MembersPanel from './MembersPanel';
import PinsPanel from './PinsPanel';
import NewConversationDialog from './NewConversationDialog';
import FlagDialog from './FlagDialog';
import { getMyModerationRequest, type ModerationRequestDto } from './moderationApi';
import { NoticeToast, AuthErrorNotice } from './atoms/StatusBanners';

type AsideTab = 'members' | 'pins' | 'thread';

export interface ChatPageElementProps {
  hostElement?: HTMLElement;
}

export default function ChatPageElement(_props: ChatPageElementProps) {
  const { available, loaded } = useChatBootstrap({ connectImmediately: true });
  const channels = useChatStore((state) => state.channels, shallowArrayEqual);

  const [activeChannelId, setActiveChannelId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [results, setResults] = useState<ChatMessageDto[] | null>(null);
  const [asideTab, setAsideTab] = useState<AsideTab>('members');
  const [thread, setThread] = useState<ChatMessageDto | null>(null);
  const [showNew, setShowNew] = useState(false);
  const [flagTarget, setFlagTarget] = useState<ChatMessageDto | null>(null);
  const [flagStatus, setFlagStatus] = useState<ModerationRequestDto | null | undefined>(undefined);

  const currentUserId = getCurrentUserId();
  const canModerate = isDepartmentAdmin();
  const activeChannel = channels.find((channel) => channel.ChatChannelId === activeChannelId) ?? null;

  // Stable identities: ChannelRow and MessageBubble are memo'd, so these callbacks must not be recreated
  // each render or those children re-render on every ChatPageElement state change (defeating their memo).
  const openChannel = useCallback((channelId: string, messageId?: string) => {
    setActiveChannelId(channelId);
    setActiveChannel(channelId);
    setHighlightMessage(messageId ?? null);
    setResults(null);
    setThread(null);
    setAsideTab('members');
  }, []);

  const openThread = useCallback((message: ChatMessageDto) => {
    setThread(message);
    setAsideTab('thread');
  }, []);

  const openFlag = useCallback((message: ChatMessageDto) => {
    setFlagTarget(message);
    setFlagStatus(undefined);
    getMyModerationRequest(0, message.ChatMessageId)
      .then(setFlagStatus)
      .catch(() => setFlagStatus(null));
  }, []);

  const runSearch = () => {
    const query = search.trim();
    if (query.length === 0) {
      setResults(null);
      return;
    }
    searchMessages(query)
      .then(setResults)
      .catch(() => setResults([]));
  };

  if (loaded && !available) {
    return (
      <div className="rgchat-root">
        <div className="rgchat-empty">Chat is not enabled for this department.</div>
      </div>
    );
  }

  return (
    <div className="rgchat-root">
      <AuthErrorNotice />
      <div className="rgchat-page">
        <div className="rgchat-page__sidebar">
          <div className="rgchat-page__sidebar-head">
            <input
              className="rgchat-input"
              placeholder="Search messages / channels"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  runSearch();
                }
              }}
            />
            <button type="button" className="rgchat-iconbtn rgchat-iconbtn--dark" title="New conversation" aria-label="New conversation" onClick={() => setShowNew(true)}>
              ＋
            </button>
          </div>
          <ChannelList channels={channels} activeChannelId={activeChannelId} filter={results ? undefined : search} loading={!loaded} onSelect={openChannel} />
        </div>

        <div className="rgchat-page__center">
          {results ? (
            <div className="rgchat-convo">
              <div className="rgchat-convo__head">
                <div className="rgchat-convo__head-body">
                  <div className="rgchat-convo__title">Search results</div>
                  <div className="rgchat-convo__sub">{results.length} match{results.length === 1 ? '' : 'es'}</div>
                </div>
                <button type="button" className="rgchat-iconbtn" title="Close search" aria-label="Close search" onClick={() => setResults(null)}>
                  ✕
                </button>
              </div>
              <div className="rgchat-convo__scroll">
                {results.length === 0 && <div className="rgchat-convo__sub">No messages found.</div>}
                {results.map((message) => {
                  const channel = channels.find((item) => item.ChatChannelId === message.ChatChannelId);
                  return (
                    <button
                      key={message.ChatMessageId}
                      type="button"
                      className="rgchat-chan"
                      onClick={() => openChannel(message.ChatChannelId, message.ChatMessageId)}
                    >
                      <div className="rgchat-chan__body">
                        <div className="rgchat-chan__name">
                          {channel ? channelDisplayName(channel) : 'Channel'} · {message.SenderDisplayName ?? 'Unknown'}
                        </div>
                        <div className="rgchat-chan__preview">{message.Body}</div>
                      </div>
                      <span className="rgchat-chan__time">{formatRelativeDay(message.SentOn)}</span>
                    </button>
                  );
                })}
              </div>
            </div>
          ) : activeChannel ? (
            <ConversationView
              channel={activeChannel}
              currentUserId={currentUserId}
              canModerate={canModerate}
              onOpenThread={openThread}
              onFlag={openFlag}
            />
          ) : (
            <div className="rgchat-empty">
              <div className="rgchat-empty__icon" aria-hidden="true">💬</div>
              <div>Select a conversation to start chatting.</div>
            </div>
          )}
        </div>

        {activeChannel && (
          <div className="rgchat-page__aside">
            <div className="rgchat-aside__tabs">
              {(['members', 'pins', 'thread'] as AsideTab[]).map((tab) => (
                <button
                  key={tab}
                  type="button"
                  className={`rgchat-aside__tab${asideTab === tab ? ' rgchat-aside__tab--active' : ''}`}
                  onClick={() => setAsideTab(tab)}
                >
                  {tab === 'members' ? 'Members' : tab === 'pins' ? 'Pins' : 'Thread'}
                </button>
              ))}
            </div>

            {asideTab === 'members' && (
              <MembersPanel channelId={activeChannel.ChatChannelId} canModerate={canModerate} currentUserId={currentUserId} />
            )}
            {asideTab === 'pins' && <PinsPanel channelId={activeChannel.ChatChannelId} canModerate={canModerate} />}
            {asideTab === 'thread' &&
              (thread ? (
                <ThreadPanel channel={activeChannel} rootMessage={thread} currentUserId={currentUserId} onClose={() => setThread(null)} />
              ) : (
                <div className="rgchat-aside__section rgchat-convo__sub">Open a message thread to view replies here.</div>
              ))}
          </div>
        )}
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
          existingRequest={flagStatus}
          statusLoading={flagStatus === undefined}
          onClose={() => setFlagTarget(null)}
          onSubmit={(reason, note) => void flagChatMessage(flagTarget, reason, note)}
        />
      )}

      <NoticeToast />
    </div>
  );
}
