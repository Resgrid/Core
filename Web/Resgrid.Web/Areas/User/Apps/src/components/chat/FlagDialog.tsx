import { useState } from 'react';

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
    <div className="rgchat-dialog__backdrop" onClick={onClose}>
      <div className="rgchat-dialog" onClick={(event) => event.stopPropagation()}>
        <div className="rgchat-dialog__head">Report message</div>
        <div className="rgchat-dialog__body">
          <div className="rgchat-form-row">
            <label htmlFor="rgchat-flag-reason">Reason</label>
            <select
              id="rgchat-flag-reason"
              className="rgchat-input"
              style={{ width: 200 }}
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
            className="rgchat-input"
            placeholder="Add a note (optional)"
            value={note}
            rows={3}
            onChange={(event) => setNote(event.target.value)}
            style={{ marginTop: 8 }}
          />
        </div>
        <div className="rgchat-dialog__foot">
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
        </div>
      </div>
    </div>
  );
}
