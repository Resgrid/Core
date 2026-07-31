import { useEffect, useMemo, useState } from 'react';
import { createAdHocChannel, createDirectMessage, getPersonnelRecipients, type PersonRecipient } from './chatApi';
import { initialsFor, colorFor } from './chatFormat';
import type { ChatChannelDto } from './types';

interface NewConversationDialogProps {
  currentUserId: string;
  onClose: () => void;
  onCreated: (channel: ChatChannelDto) => void;
}

export default function NewConversationDialog({ currentUserId, onClose, onCreated }: NewConversationDialogProps) {
  const [people, setPeople] = useState<PersonRecipient[]>([]);
  const [query, setQuery] = useState('');
  const [selected, setSelected] = useState<string[]>([]);
  const [groupName, setGroupName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getPersonnelRecipients()
      .then((result) => setPeople(result.filter((person) => person.userId !== currentUserId)))
      .catch(() => setError('Unable to load personnel.'));
  }, [currentUserId]);

  const filtered = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    return normalized.length === 0
      ? people
      : people.filter((person) => person.name.toLowerCase().includes(normalized));
  }, [people, query]);

  const toggle = (userId: string) => {
    setSelected((current) =>
      current.includes(userId) ? current.filter((id) => id !== userId) : [...current, userId],
    );
  };

  const create = async () => {
    if (selected.length === 0) {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      let channel: ChatChannelDto | null;
      if (selected.length === 1) {
        channel = await createDirectMessage(selected[0]);
      } else {
        const name = groupName.trim().length > 0 ? groupName.trim() : 'New group';
        channel = await createAdHocChannel(name, selected);
      }
      if (channel) {
        onCreated(channel);
      } else {
        setError('Could not create the conversation.');
      }
    } catch {
      setError('Could not create the conversation.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="rgchat-dialog__backdrop" onClick={onClose}>
      <div className="rgchat-dialog" onClick={(event) => event.stopPropagation()}>
        <div className="rgchat-dialog__head">New conversation</div>
        <div className="rgchat-dialog__body">
          {error && <div className="rgchat-error">{error}</div>}
          <input
            className="rgchat-input"
            placeholder="Search personnel"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            autoFocus
          />
          {selected.length > 1 && (
            <input
              className="rgchat-input"
              placeholder="Group name"
              value={groupName}
              onChange={(event) => setGroupName(event.target.value)}
              style={{ marginTop: 8 }}
            />
          )}
          <div style={{ marginTop: 8, maxHeight: 300, overflowY: 'auto' }}>
            {filtered.map((person) => (
              <label key={person.userId} className="rgchat-pick">
                <input
                  type="checkbox"
                  checked={selected.includes(person.userId)}
                  onChange={() => toggle(person.userId)}
                />
                <span
                  className="rgchat-avatar rgchat-avatar--sm"
                  style={{ backgroundColor: colorFor(person.userId) }}
                >
                  {initialsFor(person.name)}
                </span>
                <span>{person.name}</span>
              </label>
            ))}
            {filtered.length === 0 && <div className="rgchat-convo__sub" style={{ padding: 8 }}>No matches.</div>}
          </div>
        </div>
        <div className="rgchat-dialog__foot">
          <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={onClose}>
            Cancel
          </button>
          <button type="button" className="rgchat-btn rgchat-btn--primary" onClick={() => void create()} disabled={busy || selected.length === 0}>
            {selected.length > 1 ? 'Create group' : 'Start chat'}
          </button>
        </div>
      </div>
    </div>
  );
}
