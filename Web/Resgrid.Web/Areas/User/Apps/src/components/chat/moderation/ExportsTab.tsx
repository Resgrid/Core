import { useEffect, useState } from 'react';
import { downloadExport, getExports, requestExport } from '../chatModerationApi';
import { formatRelativeDay } from '../chatFormat';
import type { ChatExportDto } from '../types';
import { moderationText } from '../moderationI18n';

const FORMAT_LABELS = ['JSON', 'CSV', 'ZIP'];
const STATUS_LABEL_KEYS = ['ExportQueued', 'ExportRunning', 'ExportComplete', 'ExportFailed'];

export default function ExportsTab() {
  const [exports, setExports] = useState<ChatExportDto[]>([]);
  const [format, setFormat] = useState(0);
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [busy, setBusy] = useState(false);

  const load = () => {
    getExports()
      .then(setExports)
      .catch(() => setExports([]));
  };

  useEffect(load, []);

  const submit = async () => {
    setBusy(true);
    try {
      await requestExport(
        null,
        startDate ? new Date(startDate).toISOString() : null,
        endDate ? new Date(endDate).toISOString() : null,
        format,
      );
      load();
    } catch (error) {
      console.error(moderationText('FailedRequestExport'), error);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end', marginBottom: 16 }}>
        <label>
          <div className="rgchat-convo__sub">{moderationText('Format')}</div>
          <select className="rgchat-input" value={format} onChange={(event) => setFormat(Number(event.target.value))}>
            {FORMAT_LABELS.map((label, index) => (
              <option key={label} value={index}>{label}</option>
            ))}
          </select>
        </label>
        <label>
          <div className="rgchat-convo__sub">{moderationText('StartDate')}</div>
          <input className="rgchat-input" type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} />
        </label>
        <label>
          <div className="rgchat-convo__sub">{moderationText('EndDate')}</div>
          <input className="rgchat-input" type="date" value={endDate} onChange={(event) => setEndDate(event.target.value)} />
        </label>
        <button type="button" className="rgchat-btn rgchat-btn--primary" onClick={() => void submit()} disabled={busy}>
          {busy ? moderationText('Requesting') : moderationText('RequestExport')}
        </button>
        <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={load}>{moderationText('Refresh')}</button>
      </div>

      <table className="rgchat-table">
        <thead>
          <tr>
            <th>{moderationText('Requested')}</th>
            <th>{moderationText('Range')}</th>
            <th>{moderationText('Format')}</th>
            <th>{moderationText('Status')}</th>
            <th>{moderationText('Download')}</th>
          </tr>
        </thead>
        <tbody>
          {exports.length === 0 && (
            <tr>
              <td colSpan={5} className="rgchat-convo__sub">{moderationText('NoExportJobs')}</td>
            </tr>
          )}
          {exports.map((job) => (
            <tr key={job.ChatExportId}>
              <td>{formatRelativeDay(job.RequestedOn)}</td>
              <td>
                {job.StartDate ? new Date(job.StartDate).toLocaleDateString() : moderationText('All')} –{' '}
                {job.EndDate ? new Date(job.EndDate).toLocaleDateString() : moderationText('Now')}
              </td>
              <td>{FORMAT_LABELS[job.Format] ?? job.Format}</td>
              <td>
                <span className={`rgchat-pill ${job.Status === 2 ? 'rgchat-pill--done' : job.Status === 3 ? 'rgchat-pill--open' : 'rgchat-pill--muted'}`}>
                  {STATUS_LABEL_KEYS[job.Status] ? moderationText(STATUS_LABEL_KEYS[job.Status]) : job.Status}
                </span>
              </td>
              <td>
                {job.Status === 2 ? (
                  <button type="button" className="rgchat-btn rgchat-btn--ghost" onClick={() => void downloadExport(job.ChatExportId)}>
                    {moderationText('Download')}
                  </button>
                ) : job.Error ? (
                  <span className="rgchat-convo__sub" title={job.Error}>{moderationText('ExportFailed')}</span>
                ) : (
                  moderationText('NoValue')
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
