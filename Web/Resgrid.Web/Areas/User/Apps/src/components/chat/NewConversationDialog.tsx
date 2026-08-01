import { useEffect, useMemo, useState } from 'react';
import { createAdHocChannel, createDirectMessage, getPersonnelRecipients, type PersonRecipient } from './chatApi';
import { initialsFor, colorFor } from './chatFormat';
import Dialog from './atoms/Dialog';
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
    <Dialog
      title="New conversation"
      onClose={onClose}
      footer={
        <>
          <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={onClose}>
            Cancel
          </button>
          <button type="button" className="rgchat-btn rgchat-btn--primary" onClick={() => void create()} disabled={busy || selected.length === 0}>
            {selected.length > 1 ? 'Create group' : 'Start chat'}
          </button>
        </>
      }
    >
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
          className="rgchat-input rgchat-dialog__field"
          placeholder="Group name"
          value={groupName}
          onChange={(event) => setGroupName(event.target.value)}
        />
      )}
      <div className="rgchat-dialog__list">
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
        {filtered.length === 0 && <div className="rgchat-popover__note">No matches.</div>}
      </div>
    </Dialog>
  );
}
