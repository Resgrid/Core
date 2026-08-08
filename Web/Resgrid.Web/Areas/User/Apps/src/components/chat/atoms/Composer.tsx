import { useCallback, useEffect, useRef, useState } from 'react';
import { ChatMessageType, type GifDto } from '../types';
import { searchGifs } from '../chatApi';
import { stringifyMetadata } from '../chatFormat';
import { EMOJI_SET } from './emoji';
import { usePopoverClose } from './usePopoverClose';

export interface ComposerSendPayload {
  body: string;
  messageType: number;
  priority: number;
  metadataJson: string | null;
  file?: File;
}

interface ComposerProps {
  onSend: (payload: ComposerSendPayload) => void | Promise<void>;
  onTyping: (isTyping: boolean) => void;
  allowGifs?: boolean;
  allowImages?: boolean;
  allowUrgent?: boolean;
  allowEmoji?: boolean;
  placeholder?: string;
  disabled?: boolean;
}

type Popover = 'emoji' | 'gif' | null;

const MAX_LENGTH = 4000;
const COUNTER_THRESHOLD = 300;
// ~5 rows of text, then the textarea scrolls.
const MAX_TEXTAREA_HEIGHT = 122;

export default function Composer({
  onSend,
  onTyping,
  allowGifs = true,
  allowImages = true,
  allowUrgent = true,
  allowEmoji = true,
  placeholder = 'Write a message…',
  disabled = false,
}: ComposerProps) {
  const [text, setText] = useState('');
  const [urgent, setUrgent] = useState(false);
  const [popover, setPopover] = useState<Popover>(null);
  const [gifQuery, setGifQuery] = useState('');
  const [gifs, setGifs] = useState<GifDto[]>([]);
  const [gifLoading, setGifLoading] = useState(false);

  const rootRef = useRef<HTMLDivElement | null>(null);
  const textRef = useRef<HTMLTextAreaElement | null>(null);
  const fileRef = useRef<HTMLInputElement | null>(null);
  const typingActiveRef = useRef(false);
  const typingTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const closePopover = useCallback(() => setPopover(null), []);
  usePopoverClose(rootRef, popover !== null, closePopover);

  const autoGrow = () => {
    const textarea = textRef.current;
    if (!textarea) {
      return;
    }
    textarea.style.height = 'auto';
    textarea.style.height = `${Math.min(textarea.scrollHeight, MAX_TEXTAREA_HEIGHT)}px`;
  };

  useEffect(() => {
    autoGrow();
  }, [text]);

  const stopTyping = () => {
    if (typingActiveRef.current) {
      typingActiveRef.current = false;
      onTyping(false);
    }
    if (typingTimerRef.current) {
      clearTimeout(typingTimerRef.current);
      typingTimerRef.current = null;
    }
  };

  useEffect(() => stopTyping, []); // eslint-disable-line react-hooks/exhaustive-deps

  const handleChange = (value: string) => {
    setText(value);
    if (value.length > 0) {
      if (!typingActiveRef.current) {
        typingActiveRef.current = true;
        onTyping(true);
      }
      if (typingTimerRef.current) {
        clearTimeout(typingTimerRef.current);
      }
      typingTimerRef.current = setTimeout(stopTyping, 3000);
    } else {
      stopTyping();
    }
  };

  const submitText = async () => {
    const trimmed = text.trim();
    if (trimmed.length === 0 || disabled) {
      return;
    }
    setText('');
    setPopover(null);
    stopTyping();
    await onSend({
      body: trimmed,
      messageType: ChatMessageType.Text,
      priority: urgent ? 1 : 0,
      metadataJson: null,
    });
    setUrgent(false);
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void submitText();
    }
  };

  const handleFile = async (file: File | undefined) => {
    if (!file) {
      return;
    }
    await onSend({
      body: '',
      messageType: ChatMessageType.Image,
      priority: urgent ? 1 : 0,
      metadataJson: null,
      file,
    });
    setUrgent(false);
    if (fileRef.current) {
      fileRef.current.value = '';
    }
  };

  const handlePaste = (event: React.ClipboardEvent<HTMLTextAreaElement>) => {
    if (!allowImages) {
      return;
    }
    const file = Array.from(event.clipboardData?.files ?? []).find((item) => item.type.startsWith('image/'));
    if (file) {
      event.preventDefault();
      void handleFile(file);
    }
  };

  const pickGif = async (gif: GifDto) => {
    setPopover(null);
    await onSend({
      body: '',
      messageType: ChatMessageType.Gif,
      priority: 0,
      metadataJson: stringifyMetadata({
        gif: { url: gif.GifUrl, previewUrl: gif.PreviewUrl, width: gif.Width, height: gif.Height },
      }),
    });
  };

  useEffect(() => {
    if (popover !== 'gif') {
      return;
    }
    let active = true;
    setGifLoading(true);
    const timer = setTimeout(() => {
      searchGifs(gifQuery)
        .then((result) => {
          if (active) {
            setGifs(result);
          }
        })
        .catch(() => {
          if (active) {
            setGifs([]);
          }
        })
        .finally(() => {
          if (active) {
            setGifLoading(false);
          }
        });
    }, 300);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [popover, gifQuery]);

  const remaining = MAX_LENGTH - text.length;

  return (
    <div className="rgchat-composer" ref={rootRef}>
      {popover === 'emoji' && (
        <div className="rgchat-popover">
          <div className="rgchat-emojigrid">
            {EMOJI_SET.map((emoji) => (
              <button key={emoji} type="button" onClick={() => { handleChange(text + emoji); setPopover(null); }}>
                {emoji}
              </button>
            ))}
          </div>
        </div>
      )}

      {popover === 'gif' && (
        <div className="rgchat-popover">
          <input
            className="rgchat-input"
            placeholder="Search GIFs"
            value={gifQuery}
            onChange={(event) => setGifQuery(event.target.value)}
            autoFocus
          />
          {gifLoading ? (
            <div className="rgchat-popover__note">Searching…</div>
          ) : gifs.length === 0 ? (
            <div className="rgchat-popover__note">No GIFs found.</div>
          ) : (
            <div className="rgchat-gifgrid">
              {gifs.map((gif) => (
                <img
                  key={gif.Id}
                  src={gif.PreviewUrl}
                  alt={gif.Title}
                  loading="lazy"
                  decoding="async"
                  width={gif.Width}
                  height={gif.Height}
                  onClick={() => void pickGif(gif)}
                />
              ))}
            </div>
          )}
        </div>
      )}

      <div className="rgchat-composer__row">
        <div className="rgchat-composer__tools">
          {allowEmoji && (
            <button
              type="button"
              className="rgchat-iconbtn rgchat-composer__tool"
              title="Emoji"
              aria-label="Insert emoji"
              onClick={() => setPopover(popover === 'emoji' ? null : 'emoji')}
            >
              😊
            </button>
          )}
          {allowGifs && (
            <button
              type="button"
              className="rgchat-iconbtn rgchat-composer__tool rgchat-composer__tool--gif"
              title="GIF"
              aria-label="Insert GIF"
              onClick={() => setPopover(popover === 'gif' ? null : 'gif')}
            >
              GIF
            </button>
          )}
          {allowImages && (
            <button
              type="button"
              className="rgchat-iconbtn rgchat-composer__tool"
              title="Attach image"
              aria-label="Attach image"
              onClick={() => fileRef.current?.click()}
            >
              📎
            </button>
          )}
        </div>

        <textarea
          ref={textRef}
          className="rgchat-composer__text"
          value={text}
          placeholder={placeholder}
          onChange={(event) => handleChange(event.target.value)}
          onKeyDown={handleKeyDown}
          onPaste={handlePaste}
          disabled={disabled}
          rows={1}
          maxLength={MAX_LENGTH}
        />

        {remaining <= COUNTER_THRESHOLD && <span className="rgchat-composer__count">{remaining}</span>}

        <button
          type="button"
          className="rgchat-send"
          title="Send"
          aria-label="Send message"
          onClick={() => void submitText()}
          disabled={disabled || text.trim().length === 0}
        >
          ➤
        </button>
      </div>

      {allowUrgent && (
        <label className="rgchat-composer__urgent">
          <input type="checkbox" checked={urgent} onChange={(event) => setUrgent(event.target.checked)} />
          Send as urgent (requires acknowledgment)
        </label>
      )}

      {allowImages && (
        <input
          ref={fileRef}
          type="file"
          accept="image/*"
          className="rgchat-fileinput"
          style={{ display: 'none' }}
          onChange={(event) => void handleFile(event.target.files?.[0])}
        />
      )}
    </div>
  );
}
