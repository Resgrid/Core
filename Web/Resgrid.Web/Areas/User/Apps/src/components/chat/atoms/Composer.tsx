import { useEffect, useRef, useState } from 'react';
import { ChatMessageType, type GifDto } from '../types';
import { searchGifs } from '../chatApi';
import { stringifyMetadata } from '../chatFormat';
import { EMOJI_SET } from './emoji';

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
  placeholder?: string;
  disabled?: boolean;
}

type Popover = 'emoji' | 'gif' | null;

export default function Composer({
  onSend,
  onTyping,
  allowGifs = true,
  allowImages = true,
  allowUrgent = true,
  placeholder = 'Write a message…',
  disabled = false,
}: ComposerProps) {
  const [text, setText] = useState('');
  const [urgent, setUrgent] = useState(false);
  const [popover, setPopover] = useState<Popover>(null);
  const [gifQuery, setGifQuery] = useState('');
  const [gifs, setGifs] = useState<GifDto[]>([]);
  const [gifLoading, setGifLoading] = useState(false);

  const fileRef = useRef<HTMLInputElement | null>(null);
  const typingActiveRef = useRef(false);
  const typingTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

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

  return (
    <div className="rgchat-composer">
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
            <div className="rgchat-convo__sub" style={{ padding: 8 }}>Searching…</div>
          ) : gifs.length === 0 ? (
            <div className="rgchat-convo__sub" style={{ padding: 8 }}>No GIFs found.</div>
          ) : (
            <div className="rgchat-gifgrid">
              {gifs.map((gif) => (
                <img key={gif.Id} src={gif.PreviewUrl} alt={gif.Title} onClick={() => void pickGif(gif)} />
              ))}
            </div>
          )}
        </div>
      )}

      <div className="rgchat-composer__row">
        <div className="rgchat-composer__tools">
          <button type="button" className="rgchat-iconbtn" title="Emoji" onClick={() => setPopover(popover === 'emoji' ? null : 'emoji')} style={{ color: '#7b8794' }}>
            😊
          </button>
          {allowGifs && (
            <button type="button" className="rgchat-iconbtn" title="GIF" onClick={() => setPopover(popover === 'gif' ? null : 'gif')} style={{ color: '#7b8794', fontWeight: 700, fontSize: 12 }}>
              GIF
            </button>
          )}
          {allowImages && (
            <button type="button" className="rgchat-iconbtn" title="Attach image" onClick={() => fileRef.current?.click()} style={{ color: '#7b8794' }}>
              📎
            </button>
          )}
        </div>

        <textarea
          className="rgchat-composer__text"
          value={text}
          placeholder={placeholder}
          onChange={(event) => handleChange(event.target.value)}
          onKeyDown={handleKeyDown}
          disabled={disabled}
          rows={1}
        />

        <button type="button" className="rgchat-send" title="Send" onClick={() => void submitText()} disabled={disabled || text.trim().length === 0}>
          ➤
        </button>
      </div>

      {allowUrgent && (
        <label className="rgchat-composer__urgent" style={{ marginTop: 6 }}>
          <input type="checkbox" checked={urgent} onChange={(event) => setUrgent(event.target.checked)} />
          Send as urgent (requires acknowledgment)
        </label>
      )}

      <input ref={fileRef} type="file" accept="image/*" style={{ display: 'none' }} onChange={(event) => void handleFile(event.target.files?.[0])} />
    </div>
  );
}
