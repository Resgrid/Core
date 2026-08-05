import { useState } from 'react';
import Dialog from './atoms/Dialog';
import type { ModerationRequestDto } from './moderationApi';
import { moderationText } from './moderationI18n';

interface FlagDialogProps {
  onClose: () => void;
  onSubmit: (reason: number, note: string) => void;
  existingRequest?: ModerationRequestDto | null;
  statusLoading?: boolean;
}

export default function FlagDialog({ onClose, onSubmit, existingRequest, statusLoading = false }: FlagDialogProps) {
  const [reason, setReason] = useState(1);
  const [note, setNote] = useState('');
  const reasons: { value: number; label: string }[] = [
    { value: 1, label: moderationText('ReasonInappropriate') },
    { value: 2, label: moderationText('ReasonHarassment') },
    { value: 3, label: moderationText('ReasonSpam') },
    { value: 4, label: moderationText('ReasonSensitiveInformation') },
    { value: 5, label: moderationText('ReasonPolicyViolation') },
    { value: 0, label: moderationText('ReasonOther') },
  ];

  return (
    <Dialog
      title={existingRequest ? moderationText('ModerationReportStatus') : moderationText('ReportMessage')}
      onClose={onClose}
      footer={
        existingRequest ? (
          <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={onClose}>{moderationText('Close')}</button>
        ) : (
          <>
            <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={onClose}>{moderationText('Cancel')}</button>
            <button
              type="button"
              className="rgchat-btn rgchat-btn--danger"
              disabled={statusLoading}
              onClick={() => {
                onSubmit(reason, note.trim());
                onClose();
              }}
            >
              {moderationText('Report')}
            </button>
          </>
        )
      }
    >
      {statusLoading ? (
        <div className="rgchat-convo__sub">{moderationText('CheckingReportStatus')}</div>
      ) : existingRequest ? (
        <div>
          <p>
            {moderationText('StatusLabel')}: <strong>{existingRequest.Status === 1 ? moderationText('Completed') : moderationText('Pending')}</strong>
          </p>
          {existingRequest.Status === 1 && (
            <p>{moderationText('Action')}: {existingRequest.Disposition === 2 ? moderationText('ContentWasRemoved') : moderationText('NoContentWasRemoved')}</p>
          )}
          {existingRequest.AdminNote && <p>{moderationText('ModeratorNoteLabel')}: {existingRequest.AdminNote}</p>}
        </div>
      ) : (
        <>
          <div className="rgchat-form-row">
            <label htmlFor="rgchat-flag-reason">{moderationText('Reason')}</label>
            <select
              id="rgchat-flag-reason"
              className="rgchat-input rgchat-dialog__select"
              value={reason}
              onChange={(event) => setReason(Number(event.target.value))}
            >
              {reasons.map((item) => (
                <option key={item.value} value={item.value}>{item.label}</option>
              ))}
            </select>
          </div>
          <textarea
            className="rgchat-input rgchat-dialog__field"
            placeholder={moderationText('AddNoteOptional')}
            value={note}
            rows={3}
            maxLength={4000}
            onChange={(event) => setNote(event.target.value)}
          />
        </>
      )}
    </Dialog>
  );
}
