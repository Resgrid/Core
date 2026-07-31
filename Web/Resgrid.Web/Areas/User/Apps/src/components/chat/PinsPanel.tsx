import { useEffect, useState } from 'react';
import { getPins } from './chatApi';
import { setPinned } from './chatActions';
import { formatRelativeDay } from './chatFormat';
import type { ChatMessageDto } from './types';

interface PinsPanelProps {
  channelId: string;
  canModerate: boolean;
}

export default function PinsPanel({ channelId, canModerate }: PinsPanelProps) {
  const [pins, setPins] = useState<ChatMessageDto[]>([]);
  const [loading, setLoading] = useState(true);

  const load = () => {
    setLoading(true);
    getPins(channelId)
      .then(setPins)
      .catch(() => setPins([]))
      .finally(() => setLoading(false));
  };

  useEffect(load, [channelId]);

  if (loading) {
    return <div className="rgchat-aside__section rgchat-convo__sub">Loading…</div>;
  }

  if (pins.length === 0) {
    return <div className="rgchat-aside__section rgchat-convo__sub">No pinned messages.</div>;
  }

  return (
    <div className="rgchat-aside__section">
      {pins.map((pin) => (
        <div key={pin.ChatMessageId} className="rgchat-bubble" style={{ marginBottom: 8 }}>
          <div className="rgchat-msg__meta" style={{ margin: '0 0 4px' }}>
            <span className="rgchat-msg__author">{pin.SenderDisplayName ?? 'Unknown'}</span>
            <span>{formatRelativeDay(pin.SentOn)}</span>
          </div>
          <div>{pin.Body}</div>
          {canModerate && (
            <button
              type="button"
              className="rgchat-thread-link"
              onClick={() => {
                void setPinned(pin, false);
                setPins((current) => current.filter((item) => item.ChatMessageId !== pin.ChatMessageId));
              }}
            >
              Unpin
            </button>
          )}
        </div>
      ))}
    </div>
  );
}
