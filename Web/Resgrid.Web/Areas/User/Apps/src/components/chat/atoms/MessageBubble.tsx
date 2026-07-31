import { useState } from 'react';
import { ChatMessageType, type ChatMessageDto } from '../types';
import { formatClockTime, linkifySegments, parseMetadata } from '../chatFormat';
import Avatar from './Avatar';
import ReactionChips from './ReactionChips';
import AttachmentImage from './AttachmentImage';
import { QUICK_REACTIONS } from './emoji';

export interface MessageBubbleCallbacks {
  onReact: (message: ChatMessageDto, emoji: string, mine: boolean) => void;
  onOpenThread?: (message: ChatMessageDto) => void;
  onSaveEdit?: (message: ChatMessageDto, body: string) => void;
  onDelete?: (message: ChatMessageDto) => void;
  onFlag?: (message: ChatMessageDto) => void;
  onPin?: (message: ChatMessageDto, pinned: boolean) => void;
  onOpenImage?: (url: string) => void;
}

interface MessageBubbleProps extends MessageBubbleCallbacks {
  message: ChatMessageDto;
  currentUserId: string;
  showAuthor: boolean;
  online?: boolean;
  canModerate?: boolean;
  variant?: 'default' | 'bot';
}

function BubbleText({ body }: { body: string }) {
  return (
    <>
      {linkifySegments(body).map((segment, index) =>
        segment.href ? (
          <a key={index} href={segment.href} target="_blank" rel="noopener noreferrer">
            {segment.text}
          </a>
        ) : (
          <span key={index}>{segment.text}</span>
        ),
      )}
    </>
  );
}

export default function MessageBubble(props: MessageBubbleProps) {
  const { message, currentUserId, showAuthor, online, canModerate, variant } = props;
  const [editing, setEditing] = useState(false);
  const [editText, setEditText] = useState(message.Body ?? '');
  const [showReactions, setShowReactions] = useState(false);

  const isMine = !!message.SenderUserId && message.SenderUserId === currentUserId;
  const isDeleted = !!message.DeletedOn;
  const isUrgent = message.Priority === 1;
  const metadata = parseMetadata(message.MetadataJson);

  const bubbleClasses = ['rgchat-bubble'];
  if (isUrgent && !isDeleted) {
    bubbleClasses.push('rgchat-bubble--urgent');
  }
  if (isDeleted) {
    bubbleClasses.push('rgchat-bubble--tomb');
  }
  if (variant === 'bot') {
    bubbleClasses.push('rgchat-bubble--bot');
  }

  const canEdit = isMine && message.MessageType === ChatMessageType.Text && !isDeleted;
  const canDelete = (isMine || canModerate) && !isDeleted;

  const renderContent = () => {
    if (isDeleted) {
      return <span>This message was deleted</span>;
    }
    switch (message.MessageType) {
      case ChatMessageType.Image: {
        const attachment = message.Attachments[0];
        return (
          <>
            {attachment ? (
              <AttachmentImage
                attachmentId={attachment.ChatAttachmentId}
                fileName={attachment.FileName}
                onOpen={props.onOpenImage}
              />
            ) : (
              <span className="rgchat-convo__sub">📷 Photo</span>
            )}
            {message.Body ? <div style={{ marginTop: 4 }}><BubbleText body={message.Body} /></div> : null}
          </>
        );
      }
      case ChatMessageType.Gif:
        return metadata.gif ? (
          <img className="rgchat-bubble__img" src={metadata.gif.url} alt="GIF" />
        ) : (
          <span>GIF</span>
        );
      case ChatMessageType.Location: {
        const location = metadata.location;
        if (!location) {
          return <span>📍 Shared location</span>;
        }
        const href = `https://www.google.com/maps/search/?api=1&query=${location.latitude},${location.longitude}`;
        return (
          <a className="rgchat-bubble__location" href={href} target="_blank" rel="noopener noreferrer">
            <div className="rgchat-bubble__location-map">📍</div>
            <div style={{ marginTop: 4 }}>{location.label ?? 'Shared location'}</div>
          </a>
        );
      }
      default:
        return (
          <>
            {message.Body ? <BubbleText body={message.Body} /> : null}
            {metadata.link ? (
              <a className="rgchat-link" href={metadata.link.url} target="_blank" rel="noopener noreferrer">
                <div className="rgchat-link__body">
                  <div className="rgchat-link__title">{metadata.link.title ?? metadata.link.url}</div>
                  {metadata.link.description ? (
                    <div className="rgchat-link__desc">{metadata.link.description}</div>
                  ) : null}
                </div>
              </a>
            ) : null}
          </>
        );
    }
  };

  return (
    <div className={`rgchat-msg${isMine ? ' rgchat-msg--mine' : ''}`}>
      {showAuthor ? (
        <Avatar name={message.SenderDisplayName} userId={message.SenderUserId} online={online} showPresence={!isMine && !!message.SenderUserId} />
      ) : (
        <span style={{ width: 36, flexShrink: 0 }} />
      )}

      <div className="rgchat-msg__col">
        {showAuthor && (
          <div className="rgchat-msg__meta">
            <span className="rgchat-msg__author">{message.SenderDisplayName ?? 'Unknown'}</span>
            <span>{formatClockTime(message.SentOn)}</span>
          </div>
        )}

        <div className={bubbleClasses.join(' ')}>
          {isUrgent && !isDeleted && <span className="rgchat-bubble__urgent-tag">⚠ Urgent</span>}

          {editing ? (
            <div>
              <textarea
                className="rgchat-input"
                value={editText}
                onChange={(event) => setEditText(event.target.value)}
                rows={2}
                autoFocus
              />
              <div style={{ display: 'flex', gap: 6, marginTop: 6 }}>
                <button
                  type="button"
                  className="rgchat-btn rgchat-btn--primary"
                  onClick={() => {
                    if (editText.trim().length > 0) {
                      props.onSaveEdit?.(message, editText.trim());
                    }
                    setEditing(false);
                  }}
                >
                  Save
                </button>
                <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={() => setEditing(false)}>
                  Cancel
                </button>
              </div>
            </div>
          ) : (
            <>
              {renderContent()}
              {message.EditedOn && !isDeleted && <span className="rgchat-bubble__edited">(edited)</span>}
            </>
          )}
        </div>

        {!isDeleted && (
          <ReactionChips
            reactions={message.Reactions}
            currentUserId={currentUserId}
            onToggle={(emoji, mine) => props.onReact(message, emoji, mine)}
          />
        )}

        {message.ThreadReplyCount > 0 && props.onOpenThread && (
          <button type="button" className="rgchat-thread-link" onClick={() => props.onOpenThread?.(message)}>
            {message.ThreadReplyCount} {message.ThreadReplyCount === 1 ? 'reply' : 'replies'}
          </button>
        )}
      </div>

      {!isDeleted && !editing && (
        <div className="rgchat-msg__actions">
          <div style={{ position: 'relative' }}>
            <button
              type="button"
              className="rgchat-msg__action"
              title="React"
              onClick={() => setShowReactions((value) => !value)}
            >
              😊
            </button>
            {showReactions && (
              <div className="rgchat-popover" style={{ left: 'auto', right: 0, bottom: 28, display: 'flex', gap: 2, padding: 6 }}>
                {QUICK_REACTIONS.map((emoji) => (
                  <button
                    key={emoji}
                    type="button"
                    style={{ border: 'none', background: 'transparent', fontSize: 18, cursor: 'pointer' }}
                    onClick={() => {
                      props.onReact(message, emoji, false);
                      setShowReactions(false);
                    }}
                  >
                    {emoji}
                  </button>
                ))}
              </div>
            )}
          </div>
          {props.onOpenThread && (
            <button type="button" className="rgchat-msg__action" title="Reply in thread" onClick={() => props.onOpenThread?.(message)}>
              💬
            </button>
          )}
          {canEdit && (
            <button type="button" className="rgchat-msg__action" title="Edit" onClick={() => { setEditText(message.Body ?? ''); setEditing(true); }}>
              ✏️
            </button>
          )}
          {props.onPin && canModerate && (
            <button type="button" className="rgchat-msg__action" title={message.PinnedOn ? 'Unpin' : 'Pin'} onClick={() => props.onPin?.(message, !message.PinnedOn)}>
              📌
            </button>
          )}
          {props.onFlag && !isMine && (
            <button type="button" className="rgchat-msg__action" title="Flag" onClick={() => props.onFlag?.(message)}>
              🚩
            </button>
          )}
          {canDelete && props.onDelete && (
            <button type="button" className="rgchat-msg__action" title="Delete" onClick={() => props.onDelete?.(message)}>
              🗑️
            </button>
          )}
        </div>
      )}
    </div>
  );
}
