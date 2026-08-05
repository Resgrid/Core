import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  completeModerationRequest,
  downloadModerationEvidence,
  getModerationRequests,
  type ModerationRequestDto,
  type ModerationSearch,
} from '../moderationApi';
import { getPersonnelRecipients, type PersonRecipient } from '../chatApi';
import { moderationText } from '../moderationI18n';

const ITEM_LABEL_KEYS: Record<number, string> = {
  0: 'ItemTypeChatMessage',
  1: 'ItemTypeMessage',
  2: 'ItemTypeCallNote',
  3: 'ItemTypeCallImage',
};

const REASON_LABEL_KEYS: Record<number, string> = {
  0: 'ReasonOther',
  1: 'ReasonInappropriate',
  2: 'ReasonHarassment',
  3: 'ReasonSpam',
  4: 'ReasonSensitiveInformation',
  5: 'ReasonPolicyViolation',
};

const ACTION_LABEL_KEYS: Record<number, string> = {
  0: 'ActionReportSubmitted',
  1: 'ActionRequestReopened',
  2: 'ActionCompletedNoAction',
  3: 'ActionContentRemoved',
  4: 'ActionEvidenceDownloaded',
};

const PAGE_SIZE = 100;
const FILTER_DEBOUNCE_MS = 300;

interface ModerationRequestsTableProps {
  reportMode?: boolean;
}

function formatTimestamp(value?: string | null): string {
  if (!value) return moderationText('NoValue');
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function summarize(value?: string | null, limit = 240): string {
  if (!value) return moderationText('NoValue');
  const normalized = value.replace(/<[^>]*>/g, ' ').replace(/\s+/g, ' ').trim();
  return normalized.length > limit ? `${normalized.slice(0, limit)}…` : normalized;
}

export default function ModerationRequestsTable({ reportMode = false }: ModerationRequestsTableProps) {
  const [status, setStatus] = useState(reportMode ? -1 : 0);
  const [itemType, setItemType] = useState(-1);
  const [contentAuthorUserId, setContentAuthorUserId] = useState('');
  const [reportedByUserId, setReportedByUserId] = useState('');
  const [debouncedContentAuthorUserId, setDebouncedContentAuthorUserId] = useState('');
  const [debouncedReportedByUserId, setDebouncedReportedByUserId] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [requests, setRequests] = useState<ModerationRequestDto[]>([]);
  const [page, setPage] = useState(1);
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [people, setPeople] = useState<PersonRecipient[]>([]);

  useEffect(() => {
    getPersonnelRecipients().then(setPeople).catch(() => setPeople([]));
  }, []);

  useEffect(() => {
    if (contentAuthorUserId === debouncedContentAuthorUserId && reportedByUserId === debouncedReportedByUserId) return undefined;

    const timer = window.setTimeout(() => {
      setDebouncedContentAuthorUserId(contentAuthorUserId);
      setDebouncedReportedByUserId(reportedByUserId);
      setPage(1);
    }, FILTER_DEBOUNCE_MS);

    return () => window.clearTimeout(timer);
  }, [contentAuthorUserId, debouncedContentAuthorUserId, reportedByUserId, debouncedReportedByUserId]);

  const personName = useCallback((userId?: string | null) => {
    if (!userId) return moderationText('SystemOrUnknown');
    const person = people.find((item) => item.userId === userId);
    return person ? `${person.name} (${userId})` : userId;
  }, [people]);

  const search = useMemo<ModerationSearch>(() => ({
    status: status < 0 ? undefined : status,
    itemType: itemType < 0 ? undefined : itemType,
    contentAuthorUserId: debouncedContentAuthorUserId.trim() || undefined,
    reportedByUserId: debouncedReportedByUserId.trim() || undefined,
    from: from ? new Date(`${from}T00:00:00`).toISOString() : undefined,
    to: to ? new Date(`${to}T23:59:59.999`).toISOString() : undefined,
    page,
    pageSize: PAGE_SIZE,
  }), [debouncedContentAuthorUserId, debouncedReportedByUserId, from, itemType, page, status, to]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setRequests(await getModerationRequests(search));
    } catch (loadError) {
      console.error(moderationText('UnableLoadRequests'), loadError);
      setRequests([]);
      setError(moderationText('UnableLoadRequests'));
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => { void load(); }, [load]);

  const complete = async (request: ModerationRequestDto, disposition: 1 | 2) => {
    setBusy(request.ModerationRequestId);
    setError(null);
    try {
      const updated = await completeModerationRequest(
        request.ModerationRequestId,
        disposition,
        notes[request.ModerationRequestId]?.trim() ?? '',
      );
      if (updated) {
        setRequests((current) => current.map((item) => (
          item.ModerationRequestId === updated.ModerationRequestId ? updated : item
        )));
      } else {
        await load();
      }
    } catch (actionError) {
      console.error(moderationText('UnableCompleteRequest'), actionError);
      setError(moderationText('UnableCompleteRequest'));
    } finally {
      setBusy(null);
    }
  };

  return (
    <div>
      <div className="rgchat-mod__filters">
        <label>
          <span>{moderationText('Status')}</span>
          <select className="rgchat-input" value={status} onChange={(event) => { setStatus(Number(event.target.value)); setPage(1); }}>
            <option value={-1}>{moderationText('All')}</option>
            <option value={0}>{moderationText('Pending')}</option>
            <option value={1}>{moderationText('Completed')}</option>
          </select>
        </label>
        <label>
          <span>{moderationText('ContentType')}</span>
          <select className="rgchat-input" value={itemType} onChange={(event) => { setItemType(Number(event.target.value)); setPage(1); }}>
            <option value={-1}>{moderationText('All')}</option>
            {Object.entries(ITEM_LABEL_KEYS).map(([value, key]) => <option key={value} value={value}>{moderationText(key)}</option>)}
          </select>
        </label>
        {reportMode && (
          <>
            <label>
              <span>{moderationText('AddedByUserId')}</span>
              <input className="rgchat-input" list="rg-moderation-authors" value={contentAuthorUserId} onChange={(event) => setContentAuthorUserId(event.target.value)} />
            </label>
            <label>
              <span>{moderationText('ReportedByUserId')}</span>
              <input className="rgchat-input" list="rg-moderation-reporters" value={reportedByUserId} onChange={(event) => setReportedByUserId(event.target.value)} />
            </label>
            <datalist id="rg-moderation-authors">
              {people.map((person) => <option key={person.userId} value={person.userId}>{person.name}</option>)}
            </datalist>
            <datalist id="rg-moderation-reporters">
              {people.map((person) => <option key={person.userId} value={person.userId}>{person.name}</option>)}
            </datalist>
            <label>
              <span>{moderationText('From')}</span>
              <input type="date" className="rgchat-input" value={from} onChange={(event) => { setFrom(event.target.value); setPage(1); }} />
            </label>
            <label>
              <span>{moderationText('To')}</span>
              <input type="date" className="rgchat-input" value={to} onChange={(event) => { setTo(event.target.value); setPage(1); }} />
            </label>
          </>
        )}
        <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={() => void load()}>{moderationText('Refresh')}</button>
      </div>

      {error && <div className="rgchat-error">{error}</div>}
      {loading && <div className="rgchat-convo__sub">{moderationText('LoadingModerationRequests')}</div>}
      {!loading && requests.length === 0 && <div className="rgchat-convo__sub">{moderationText('NoMatchingRequests')}</div>}

      <div className="rgchat-table-wrap">
        <table className="rgchat-table">
          <thead>
            <tr>
              <th>{moderationText('Status')}</th>
              <th>{moderationText('Item')}</th>
              <th>{moderationText('OriginalEvidence')}</th>
              <th>{moderationText('AddedBy')}</th>
              <th>{moderationText('Reports')}</th>
              <th>{moderationText('Action')}</th>
            </tr>
          </thead>
          <tbody>
            {requests.map((request) => {
              const isPending = request.Status === 0;
              const isBusy = busy === request.ModerationRequestId;
              const isExpanded = expanded[request.ModerationRequestId] === true;
              return (
                <tr key={request.ModerationRequestId}>
                  <td>
                    <span className={`rgchat-pill ${isPending ? 'rgchat-pill--open' : 'rgchat-pill--done'}`}>
                      {isPending ? moderationText('Pending') : moderationText('Completed')}
                    </span>
                    {!isPending && <div className="rgchat-convo__sub">{request.Disposition === 2 ? moderationText('ContentRemoved') : moderationText('NoAction')}</div>}
                    <div className="rgchat-convo__sub">{formatTimestamp(request.ModifiedOn)}</div>
                  </td>
                  <td>
                    <strong>{moderationText(ITEM_LABEL_KEYS[request.ItemType] ?? 'UnknownContentType')}</strong>
                    <div className="rgchat-convo__sub">{moderationText('IdFormat', request.ItemId)}</div>
                    {request.CallId !== null && request.CallId !== undefined && <div className="rgchat-convo__sub">{moderationText('CallFormat', request.CallId)}</div>}
                  </td>
                  <td className="rgchat-mod__evidence">
                    {request.OriginalSubject && <strong>{request.OriginalSubject}</strong>}
                    <div>{summarize(request.OriginalText)}</div>
                    {request.OriginalFileName && <div className="rgchat-convo__sub">{request.OriginalFileName}</div>}
                    {request.HasOriginalContent && (
                      <button type="button" className="rgchat-thread-link" onClick={() => void downloadModerationEvidence(request)}>
                        {moderationText('DownloadRetainedEvidence')}
                      </button>
                    )}
                  </td>
                  <td>
                    <div>{personName(request.ContentAuthorUserId)}</div>
                    <div className="rgchat-convo__sub">{formatTimestamp(request.ContentCreatedOn)}</div>
                  </td>
                  <td>
                    {request.Reports.map((report) => (
                      <div key={report.ModerationReportId} className="rgchat-mod__report">
                        <strong>{personName(report.ReportedByUserId)}</strong>
                        {report.ReporterGroupId !== null && report.ReporterGroupId !== undefined && <span className="rgchat-convo__sub"> · {moderationText('GroupFormat', report.ReporterGroupId)}</span>}
                        <div>{moderationText(REASON_LABEL_KEYS[report.Reason] ?? 'ReasonOther')}{report.Note ? ` — ${report.Note}` : ''}</div>
                        <div className="rgchat-convo__sub">{formatTimestamp(report.ReportedOn)}</div>
                      </div>
                    ))}
                  </td>
                  <td>
                    {isPending ? (
                      <div className="rgchat-mod__actions">
                        <textarea
                          className="rgchat-input"
                          rows={3}
                          maxLength={4000}
                          placeholder={moderationText('CompletionNotePlaceholder')}
                          value={notes[request.ModerationRequestId] ?? ''}
                          onChange={(event) => setNotes((current) => ({ ...current, [request.ModerationRequestId]: event.target.value }))}
                        />
                        <div>
                          <button type="button" className="rgchat-btn rgchat-btn--danger" disabled={isBusy} onClick={() => void complete(request, 2)}>{moderationText('RemoveContent')}</button>{' '}
                          <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={isBusy} onClick={() => void complete(request, 1)}>{moderationText('CompleteNoAction')}</button>
                        </div>
                      </div>
                    ) : (
                      <div>
                        <div><strong>{moderationText('CompletedBy')}:</strong> {personName(request.CompletedByUserId)}</div>
                        <div>{request.AdminNote || moderationText('NoCompletionNote')}</div>
                      </div>
                    )}
                    <button
                      type="button"
                      className="rgchat-thread-link"
                      onClick={() => setExpanded((current) => ({ ...current, [request.ModerationRequestId]: !isExpanded }))}
                    >
                      {isExpanded ? moderationText('HideAuditTrail') : moderationText('AuditTrailCount', request.Actions.length)}
                    </button>
                    {isExpanded && (
                      <ol className="rgchat-mod__audit">
                        {request.Actions.map((action) => (
                          <li key={action.ModerationActionId}>
                            <strong>{moderationText(ACTION_LABEL_KEYS[action.ActionType] ?? 'Action')}</strong> {moderationText('By')} {personName(action.PerformedByUserId)}
                            <div>{action.Note || moderationText('NoNote')}</div>
                            <div className="rgchat-convo__sub">
                              {formatTimestamp(action.PerformedOn)} · {action.ActorRole ? moderationText(`Actor${action.ActorRole}`) : moderationText('UnknownRole')} · {action.IpAddress || moderationText('NoIp')} · {moderationText('TraceFormat', action.TraceId || moderationText('NotAvailable'))}
                            </div>
                          </li>
                        ))}
                      </ol>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={page === 1} onClick={() => setPage((value) => Math.max(1, value - 1))}>
          {moderationText('Previous')}
        </button>
        <span className="rgchat-convo__sub" style={{ alignSelf: 'center' }}>{moderationText('PageFormat', page)}</span>
        <button type="button" className="rgchat-btn rgchat-btn--ghost" disabled={requests.length < PAGE_SIZE} onClick={() => setPage((value) => value + 1)}>
          {moderationText('Next')}
        </button>
      </div>
    </div>
  );
}
