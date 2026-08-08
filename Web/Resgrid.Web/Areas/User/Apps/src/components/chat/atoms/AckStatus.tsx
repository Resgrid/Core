import { useEffect, useState } from 'react';
import { getAcks, type ChatAckDto } from '../chatApi';
import { useChatStore } from '../useChatStore';

interface AckStatusProps {
  messageId: string;
}

function formatAckTime(value: string): string {
  const date = new Date(value);
  return date.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
}

/**
 * Acknowledgment roll-up under an urgent message: "n/total acknowledged" with an expandable
 * list of who acknowledged (and when) and who is still pending. Rendered only for the sender
 * or a moderator — GetAcks returns 401 for anyone else. Live-refreshes off the per-message
 * ack revision the hub bumps on each chatReceiptUpdated ack event.
 */
export default function AckStatus({ messageId }: AckStatusProps) {
  const revision = useChatStore((state) => state.ackRevisionByMessage[messageId] ?? 0);
  const [acks, setAcks] = useState<ChatAckDto[] | null>(null);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    let active = true;
    getAcks(messageId)
      .then((result) => {
        if (active) {
          setAcks(result);
        }
      })
      .catch(() => {
        if (active) {
          setAcks(null);
        }
      });
    return () => {
      active = false;
    };
  }, [messageId, revision]);

  if (!acks || acks.length === 0) {
    return null;
  }

  const acked = acks.filter((ack) => !!ack.AcknowledgedOn);
  const pending = acks.filter((ack) => !ack.AcknowledgedOn);
  const allAcked = pending.length === 0;

  return (
    <div className="rgchat-ackstatus">
      <button
        type="button"
        className={`rgchat-ackstatus__summary${allAcked ? ' rgchat-ackstatus__summary--done' : ''}`}
        onClick={() => setOpen((value) => !value)}
        aria-expanded={open}
      >
        ✔ {acked.length}/{acks.length} acknowledged
      </button>
      {open && (
        <div className="rgchat-ackstatus__list">
          {acked.map((ack) => (
            <div key={ack.ChatMessageAckId} className="rgchat-ackstatus__row">
              <span className="rgchat-ackstatus__name">{ack.DisplayName || ack.UserId}</span>
              <span className="rgchat-ackstatus__time">{ack.AcknowledgedOn ? formatAckTime(ack.AcknowledgedOn) : ''}</span>
            </div>
          ))}
          {pending.map((ack) => (
            <div key={ack.ChatMessageAckId} className="rgchat-ackstatus__row rgchat-ackstatus__row--pending">
              <span className="rgchat-ackstatus__name">{ack.DisplayName || ack.UserId}</span>
              <span className="rgchat-ackstatus__time">pending</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
