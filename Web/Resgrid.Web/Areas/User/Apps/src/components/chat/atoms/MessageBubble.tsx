import { memo, useRef, useState } from 'react';
import { ChatMessageType, type ChatMessageDto } from '../types';
import { formatClockTime, isSafeUrl, linkifySegments, parseMetadata } from '../chatFormat';
import Avatar from './Avatar';
import ReactionChips from './ReactionChips';
import AttachmentImage from './AttachmentImage';
import AckStatus from './AckStatus';
import { QUICK_REACTIONS } from './emoji';
import { usePopoverClose } from './usePopoverClose';
import { moderationText } from '../moderationI18n';

export interface MessageBubbleCallbacks {
  onReact?: (message: ChatMessageDto, emoji: string, mine: boolean) => void;
  onOpenThread?: (message: ChatMessageDto) => void;
  onSaveEdit?: (message: ChatMessageDto, body: string) => void;
  onDelete?: (message: ChatMessageDto) => void;
  onFlag?: (message: ChatMessageDto) => void;
  onPin?: (message: ChatMessageDto, pinned: boolean) => void;
  onOpenImage?: (url: string) => void;
  onRetrySend?: (message: ChatMessageDto) => void;
  onDiscardFailed?: (message: ChatMessageDto) => void;
}

interface MessageBubbleProps extends MessageBubbleCallbacks {
  message: ChatMessageDto;
  currentUserId: string;
  showAuthor: boolean;
  online?: boolean;
  canModerate?: boolean;
  variant?: 'default' | 'bot';
  highlighted?: boolean;
  /** Render the urgent-message acknowledgment roll-up (sender/moderator only). */
  showAckStatus?: boolean;
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

function MessageBubble(props: MessageBubbleProps) {
  const { message, currentUserId, showAuthor, online, canModerate, variant } = props;
  const [editing, setEditing] = useState(false);
  const [editText, setEditText] = useState(message.Body ?? '');
  const [showReactions, setShowReactions] = useState(false);
  const [showOverflow, setShowOverflow] = useState(false);
  const actionsRef = useRef<HTMLDivElement | null>(null);
  usePopoverClose(actionsRef, showReactions, () => setShowReactions(false));

  const isMine = !!message.SenderUserId && message.SenderUserId === currentUserId;
  const isDeleted = !!message.DeletedOn;
  const isUrgent = message.Priority === 1;
  const isFailed = message.ClientStatus === 'failed';
  const isPending = message.ClientStatus === 'pending';
  const metadata = parseMetadata(message.MetadataJson);

  const bubbleClasses = ['rgchat-bubble'];
  if (isUrgent && !isDeleted) {
    bubbleClasses.push('rgchat-bubble--urgent');
  }
  if (isDeleted) {
    bubbleClasses.push('rgchat-bubble--tomb');
  }
  if (isFailed) {
    bubbleClasses.push('rgchat-bubble--failed');
  }
  if (variant === 'bot') {
    bubbleClasses.push('rgchat-bubble--bot');
  }

  const canEdit = isMine && message.MessageType === ChatMessageType.Text && !isDeleted && !isFailed && !!props.onSaveEdit;
  const canDelete = (isMine || canModerate) && !isDeleted && !isFailed;

  const renderContent = () => {
    if (isDeleted) {
      return <span>{message.IsModerated ? moderationText('MessageRemovedByModeration') : moderationText('MessageDeleted')}</span>;
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
            {message.Body ? <div className="rgchat-bubble__caption"><BubbleText body={message.Body} /></div> : null}
          </>
        );
      }
      case ChatMessageType.Gif:
        // Belt-and-braces: parseMetadata already drops unsafe URLs; re-check at render.
        return metadata.gif && isSafeUrl(metadata.gif.url) ? (
          <img
            className="rgchat-bubble__img"
            src={metadata.gif.url}
            alt="GIF"
            loading="lazy"
            decoding="async"
            width={metadata.gif.width}
            height={metadata.gif.height}
          />
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
            <div className="rgchat-bubble__caption">{location.label ?? 'Shared location'}</div>
          </a>
        );
      }
      default:
        return (
          <>
            {message.Body ? <BubbleText body={message.Body} /> : null}
            {metadata.link && isSafeUrl(metadata.link.url) ? (
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
    <div
      className={`rgchat-msg${isMine ? ' rgchat-msg--mine' : ''}${showOverflow ? ' rgchat-msg--actions-open' : ''}`}
      id={`rgchat-msg-${message.ChatMessageId}`}
    >
      {showAuthor ? (
        <Avatar name={message.SenderDisplayName} userId={message.SenderUserId} online={online} showPresence={!isMine && !!message.SenderUserId} />
      ) : (
        <span className="rgchat-msg__spacer" />
      )}

      <div className="rgchat-msg__col">
        {showAuthor && (
          <div className="rgchat-msg__meta">
            <span className="rgchat-msg__author">{message.SenderDisplayName ?? 'Unknown'}</span>
            <span>{formatClockTime(message.SentOn)}</span>
          </div>
        )}

        <div className={`${bubbleClasses.join(' ')}${props.highlighted ? ' rgchat-bubble--flash' : ''}`}>
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
              <div className="rgchat-bubble__editrow">
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
              {isPending && <span className="rgchat-bubble__status">Sending…</span>}
            </>
          )}
        </div>

        {isFailed && (
          <div className="rgchat-failed">
            <span className="rgchat-failed__label">Not sent.</span>
            {props.onRetrySend && (
              <button type="button" className="rgchat-failed__action" onClick={() => props.onRetrySend?.(message)}>
                Retry
              </button>
            )}
            {props.onDiscardFailed && (
              <button type="button" className="rgchat-failed__action rgchat-failed__action--danger" onClick={() => props.onDiscardFailed?.(message)}>
                Delete
              </button>
            )}
          </div>
        )}

        {isUrgent && !isDeleted && !isFailed && props.showAckStatus && <AckStatus messageId={message.ChatMessageId} />}

        {!isDeleted && !isFailed && (
          <ReactionChips
            reactions={message.Reactions}
            currentUserId={currentUserId}
            onToggle={(emoji, mine) => props.onReact?.(message, emoji, mine)}
          />
        )}

        {message.ThreadReplyCount > 0 && props.onOpenThread && (
          <button type="button" className="rgchat-thread-link" onClick={() => props.onOpenThread?.(message)}>
            {message.ThreadReplyCount} {message.ThreadReplyCount === 1 ? 'reply' : 'replies'}
          </button>
        )}
      </div>

      {!isDeleted && !editing && !isFailed && (
        <div className="rgchat-msg__actions" ref={actionsRef}>
          {props.onReact && (
            <div className="rgchat-msg__actionwrap">
              <button
                type="button"
                className="rgchat-msg__action"
                title="React"
                aria-label="Add reaction"
                onClick={() => setShowReactions((value) => !value)}
              >
                😊
              </button>
              {showReactions && (
                <div className="rgchat-popover rgchat-popover--reactions">
                  {QUICK_REACTIONS.map((emoji) => (
                    <button
                      key={emoji}
                      type="button"
                      className="rgchat-popover__emoji"
                      onClick={() => {
                        props.onReact?.(message, emoji, false);
                        setShowReactions(false);
                      }}
                    >
                      {emoji}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
          {props.onOpenThread && (
            <button type="button" className="rgchat-msg__action" title="Reply in thread" aria-label="Reply in thread" onClick={() => props.onOpenThread?.(message)}>
              💬
            </button>
          )}
          {canEdit && (
            <button type="button" className="rgchat-msg__action" title="Edit" aria-label="Edit message" onClick={() => { setEditText(message.Body ?? ''); setEditing(true); }}>
              ✏️
            </button>
          )}
          {props.onPin && canModerate && (
            <button type="button" className="rgchat-msg__action" title={message.PinnedOn ? 'Unpin' : 'Pin'} aria-label={message.PinnedOn ? 'Unpin message' : 'Pin message'} onClick={() => props.onPin?.(message, !message.PinnedOn)}>
              📌
            </button>
          )}
          {props.onFlag && !isMine && (
            <button type="button" className="rgchat-msg__action" title={moderationText('Report')} aria-label={moderationText('ReportMessage')} onClick={() => props.onFlag?.(message)}>
              🚩
            </button>
          )}
          {canDelete && props.onDelete && (
            <button type="button" className="rgchat-msg__action" title="Delete" aria-label="Delete message" onClick={() => props.onDelete?.(message)}>
              🗑️
            </button>
          )}
        </div>
      )}

      {!isDeleted && !editing && !isFailed && (
        <button
          type="button"
          className="rgchat-msg__overflow"
          title="Message actions"
          aria-label="Message actions"
          onClick={() => setShowOverflow((value) => !value)}
        >
          ⋯
        </button>
      )}
    </div>
  );
}

export default memo(MessageBubble);
