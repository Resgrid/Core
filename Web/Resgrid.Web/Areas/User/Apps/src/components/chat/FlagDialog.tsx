import { useState } from 'react';
import Dialog from './atoms/Dialog';

interface FlagDialogProps {
  onClose: () => void;
  onSubmit: (reason: number, note: string) => void;
}

const REASONS: { value: number; label: string }[] = [
  { value: 1, label: 'Inappropriate content' },
  { value: 2, label: 'Harassment' },
  { value: 3, label: 'Spam' },
  { value: 4, label: 'Sensitive information' },
  { value: 5, label: 'Policy violation' },
  { value: 0, label: 'Other' },
];

export default function FlagDialog({ onClose, onSubmit }: FlagDialogProps) {
  const [reason, setReason] = useState(1);
  const [note, setNote] = useState('');

  return (
    <Dialog
      title="Report message"
      onClose={onClose}
      footer={
        <>
          <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className="rgchat-btn rgchat-btn--danger"
            onClick={() => {
              onSubmit(reason, note.trim());
              onClose();
            }}
          >
            Report
          </button>
        </>
      }
    >
      <div className="rgchat-form-row">
        <label htmlFor="rgchat-flag-reason">Reason</label>
        <select
          id="rgchat-flag-reason"
          className="rgchat-input rgchat-dialog__select"
          value={reason}
          onChange={(event) => setReason(Number(event.target.value))}
        >
          {REASONS.map((item) => (
            <option key={item.value} value={item.value}>
              {item.label}
            </option>
          ))}
        </select>
      </div>
      <textarea
        className="rgchat-input rgchat-dialog__field"
        placeholder="Add a note (optional)"
        value={note}
        rows={3}
        onChange={(event) => setNote(event.target.value)}
      />
    </Dialog>
  );
}
