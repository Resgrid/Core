import { useEffect, useRef, useState } from 'react';
import { apiFetchJson, ApiError } from '../../runtime/api';

type Evidence = { kind: string; id: string; titleKey: string; textKeys: string[]; numbers: Record<string, number>; state: string; destination?: string; citationId: string; catalogVersion: string; asOfUtc: string; publicText?: string };
type Answer = { conversationId: string | null; revision: number; outcome: string; evidence: Evidence[]; inputTokens: number; outputTokens: number };
type Conversation = { id: string; revision: number; modifiedOnUtc: string };
type Status = { available: boolean; reason: string; tokensRemaining: number; tier?: string; freeQuestionsRemaining?: number; freeQuestionsAllowance?: number; freeWindowEndsUtc?: string };
const endpoint = 'api/v4/AdminAssist/';

export default function AskPanel({ t, localLink, settings }: { settings: { id: string; labelKey: string; valueType: string }[]; t: (key: string) => string; localLink: (url?: string | null) => string | undefined }) {
  const ui = (key: string) => t(`Ui.${key}`);
  const [question, setQuestion] = useState('');
  const [topic, setTopic] = useState('setup');
  const [settingId, setSettingId] = useState('');
  const [proposedValue, setProposedValue] = useState('');
  const [answers, setAnswers] = useState<Answer[]>([]);
  const [conversation, setConversation] = useState<Conversation | null>(null);
  const [history, setHistory] = useState<Conversation[]>([]);
  const [status, setStatus] = useState<Status | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const pending = useRef<AbortController | null>(null);
  const load = async (signal?: AbortSignal) => {
    const [nextStatus, nextHistory] = await Promise.all([
      apiFetchJson<Status>(`${endpoint}AskStatus`, { signal }),
      apiFetchJson<Conversation[]>(`${endpoint}Conversations`, { signal }),
    ]);
    if (!signal?.aborted) { setStatus(nextStatus); setHistory(nextHistory); }
  };
  useEffect(() => {
    const reloadPrivate = () => {
      pending.current?.abort();
      const abort = new AbortController(); pending.current = abort;
      setAnswers([]); setConversation(null); setHistory([]); setStatus(null); setBusy(false);
      void load(abort.signal).catch(() => { if (!abort.signal.aborted) setNotice('Ask.Unavailable'); });
    };
    reloadPrivate();
    window.addEventListener('resgrid:adp-reveal-changed', reloadPrivate);
    return () => { pending.current?.abort(); window.removeEventListener('resgrid:adp-reveal-changed', reloadPrivate); };
  }, []);
  async function run(action: (signal: AbortSignal) => Promise<void>) {
    if (busy) return;
    pending.current?.abort(); const abort = new AbortController(); pending.current = abort;
    setBusy(true); setNotice('');
    try { await action(abort.signal); if (!abort.signal.aborted) await load(abort.signal); }
    catch (error) {
      if (!abort.signal.aborted) {
        setNotice(error instanceof ApiError && error.status === 409 ? 'Conflict' : 'Ask.Unavailable');
        if (error instanceof ApiError && error.status === 403) { setAnswers([]); setStatus(null); setHistory([]); setConversation(null); }
      }
    } finally { if (pending.current === abort) setBusy(false); }
  }
  const reset = () => { setConversation(null); setAnswers([]); setQuestion(''); setNotice(''); };
  return <section aria-busy={busy}>
    <h3>{ui('ask')}</h3><p>{ui('Ask.Boundary')}</p><p>{ui('Ask.Privacy')}</p>
    {status && (status.tier === 'Free' ? <p>{ui('Ask.FreeQuestions')}: {status.freeQuestionsRemaining} / {status.freeQuestionsAllowance} {status.freeWindowEndsUtc && <>· {ui('Ask.WindowEnds')}: {new Date(status.freeWindowEndsUtc).toLocaleDateString()}</>}</p> : <p>{ui('Ask.TokensRemaining')}: {status.tokensRemaining.toLocaleString()}</p>)}
    {status && !status.available && <p role="status">{ui(`Ask.${status.reason}`)}</p>}
    <form onSubmit={e => { e.preventDefault(); void run(async signal => {
      const answer = await apiFetchJson<Answer>(`${endpoint}Ask`, { method: 'POST', signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, topic, settingId: topic === 'settings' && settingId ? settingId : null, proposedValue: topic === 'settings' && settingId ? proposedValue : null, conversationId: conversation?.id ?? null, expectedRevision: conversation?.revision ?? 0 }) });
      if (signal.aborted) return;
      setAnswers(previous => [...previous, answer].slice(-10)); setNotice(`Ask.${answer.outcome}`);
      if (answer.conversationId) { setConversation({ id: answer.conversationId, revision: answer.revision, modifiedOnUtc: new Date().toISOString() }); setQuestion(''); }
    }); }}>
      <label>{ui('Ask.Topic')} <select value={topic} disabled={busy} onChange={e => setTopic(e.target.value)}>
        {['setup', 'settings', 'permissions', 'addons', 'reference'].map(value => <option key={value} value={value}>{ui(`Ask.Topic.${value}`)}</option>)}
      </select></label>
      {topic === 'settings' && <>
        <label>{ui('Ask.Setting')} <select value={settingId} disabled={busy} onChange={e => { setSettingId(e.target.value); setProposedValue(''); }}><option value="">{ui('Ask.NoProposal')}</option>{settings.map(item => <option key={item.id} value={item.id}>{t(item.labelKey)}</option>)}</select></label>
        {settingId && <label>{ui('Ask.ProposedValue')} {settings.find(s => s.id === settingId)?.valueType === 'boolean' ? <select value={proposedValue} required disabled={busy} onChange={e => setProposedValue(e.target.value)}><option value="">{ui('Ask.SelectValue')}</option><option value="true">{ui('True')}</option><option value="false">{ui('False')}</option></select> : <input type="number" min={0} step={1} required value={proposedValue} disabled={busy} onChange={e => setProposedValue(e.target.value)} />}</label>}
      </>}
      <label>{ui('Ask.Question')} <textarea required maxLength={2000} value={question} disabled={busy} onChange={e => setQuestion(e.target.value)} /></label>
      <button type="submit" disabled={busy || !status?.available || !question.trim() || (conversation?.revision ?? 0) >= 100}>{ui('Ask.Send')}</button>
      {busy && <button type="button" onClick={() => { pending.current?.abort(); setBusy(false); setNotice('Ask.Cancelled'); }}>{ui('Ask.Cancel')}</button>}
    </form>
    {notice && <p role="status">{ui(notice)}</p>}
    <button type="button" disabled={busy} onClick={reset}>{ui('Ask.New')}</button>
    {conversation && <button type="button" disabled={busy} onClick={() => void run(async signal => {
      await apiFetchJson(`${endpoint}DeleteConversation`, { method: 'POST', signal, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ conversationId: conversation.id, expectedRevision: conversation.revision }) });
      if (!signal.aborted) { reset(); setNotice('Ask.Deleted'); }
    })}>{ui('Ask.Delete')}</button>}
    {conversation && <button type="button" disabled={busy} onClick={() => void run(async signal => {
      const data = await apiFetchJson(`${endpoint}ConversationExport`, { signal }, { conversationId: conversation.id });
      if (signal.aborted) return;
      const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }));
      const link = document.createElement('a'); link.href = url; link.download = 'admin-assist-private-history.json'; link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    })}>{ui('Ask.Export')}</button>}
    <details><summary>{ui('Ask.History')}</summary><p>{ui('Ask.HistoryHelp')}</p>
      {history.map(item => <button type="button" key={item.id} disabled={busy} onClick={() => void run(async signal => {
        const rows = await apiFetchJson<Answer[]>(`${endpoint}Conversation`, { signal }, { conversationId: item.id });
        if (!signal.aborted) { setAnswers(rows); setConversation({ ...item, revision: rows.at(-1)?.revision ?? item.revision }); setQuestion(''); setNotice('Ask.Refreshed'); }
      })}>{new Date(item.modifiedOnUtc).toLocaleString()} · {item.revision}</button>)}
    </details>
    <div aria-live="polite">{answers.map((answer, index) => <article key={`${answer.revision}-${index}`}>
      <h4>{ui(`Ask.${answer.outcome}`)}</h4>
      {answer.evidence.map(card => <section className="rgaa-card" key={card.id}>
        <h5>{t(card.titleKey)}</h5><p>{ui(card.kind === 'SetupTask' ? `TaskState.${card.state}` : card.state === 'Known' && card.kind !== 'Capability' ? 'Ask.CurrentEvidence' : card.state)}</p>
        {card.textKeys.map((key, i) => <p key={`${key}-${i}`}>{t(key)}</p>)}
        {Object.entries(card.numbers).map(([label, value]) => <p key={label}>{t(label)}: {value.toLocaleString()}</p>)}
        {card.publicText && <p className="rgaa-source">{card.publicText}</p>}
        {localLink(card.destination) && <p><a href={localLink(card.destination)}>{ui('Configure')}</a></p>}
        <small>{ui('Ask.Source')}: {card.citationId} · {card.catalogVersion} · {new Date(card.asOfUtc).toLocaleString()}</small>
      </section>)}
    </article>)}</div>
  </section>;
}
