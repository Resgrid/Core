import { useEffect, useRef, useState } from 'react';
import { type PlanSource } from './PlansPanel';
import { apiFetchJson, ApiError } from '../../runtime/api';
import { Chip, Ibox, ProgressBar } from './SetupVisuals';

type Evidence = { id: string; kind: string; titleKey: string; textKeys: string[]; numbers: Record<string, number>; state: string; destination?: string; citationId: string; catalogVersion: string; asOfUtc: string; publicText?: string };
type Answer = { conversationId: string | null; revision: number; outcome: string; evidence: Evidence[]; inputTokens: number; outputTokens: number };
// The question is kept only in this page's memory for turns asked here; reopened conversations return evidence, not the text.
type Turn = Answer & { question?: string };
type Conversation = { id: string; revision: number; modifiedOnUtc: string };
type Status = { available: boolean; reason: string; tokensRemaining: number; tier?: string; freeQuestionsRemaining?: number; freeQuestionsAllowance?: number; freeWindowEndsUtc?: string };
type Tone = 'ok' | 'fail' | 'unknown' | 'info' | 'muted';
const endpoint = 'api/v4/AdminAssist/';
const topics = ['setup', 'settings', 'permissions', 'addons', 'reference'];
const taskTone: Record<string, Tone> = { AccessAvailable: 'ok', ChecksPassed: 'ok', ConfigurationPresent: 'ok', Learned: 'ok', Blocked: 'fail', Pending: 'info', Unknown: 'unknown' };
const outcomeVisual = (outcome: string): { icon: string; tone: Tone } =>
  outcome === 'Answered' ? { icon: 'fa-check-circle', tone: 'ok' } : outcome === 'Abstained' ? { icon: 'fa-info-circle', tone: 'muted' } : { icon: 'fa-exclamation-circle', tone: 'unknown' };

export default function AskPanel({ t, localLink, settings, onPlan }: { onPlan?: (source: PlanSource) => void; settings: { id: string; labelKey: string; valueType: string }[]; t: (key: string) => string; localLink: (url?: string | null) => string | undefined }) {
  const ui = (key: string) => t(`Ui.${key}`);
  const [question, setQuestion] = useState('');
  const [topic, setTopic] = useState('setup');
  const [settingId, setSettingId] = useState('');
  const [proposedValue, setProposedValue] = useState('');
  const [turns, setTurns] = useState<Turn[]>([]);
  const [conversation, setConversation] = useState<Conversation | null>(null);
  const [history, setHistory] = useState<Conversation[]>([]);
  const [status, setStatus] = useState<Status | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState('');
  const [asking, setAsking] = useState('');
  const pending = useRef<AbortController | null>(null);
  const thread = useRef<HTMLDivElement | null>(null);
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
      setTurns([]); setConversation(null); setHistory([]); setStatus(null); setBusy(false);
      void load(abort.signal).catch(() => { if (!abort.signal.aborted) setNotice('Ask.Unavailable'); });
    };
    reloadPrivate();
    window.addEventListener('resgrid:adp-reveal-changed', reloadPrivate);
    return () => { pending.current?.abort(); window.removeEventListener('resgrid:adp-reveal-changed', reloadPrivate); };
  }, []);
  // Keep the newest turn in view inside the thread without scrolling the page.
  useEffect(() => { if (thread.current) thread.current.scrollTop = thread.current.scrollHeight; }, [turns, busy]);
  async function run(action: (signal: AbortSignal) => Promise<void>) {
    if (busy) return;
    pending.current?.abort(); const abort = new AbortController(); pending.current = abort;
    setBusy(true); setNotice('');
    try { await action(abort.signal); if (!abort.signal.aborted) await load(abort.signal); }
    catch (error) {
      if (!abort.signal.aborted) {
        setNotice(error instanceof ApiError && error.status === 409 ? 'Conflict' : 'Ask.Unavailable');
        if (error instanceof ApiError && error.status === 403) { setTurns([]); setStatus(null); setHistory([]); setConversation(null); }
      }
    } finally { if (pending.current === abort) { setBusy(false); setAsking(''); } }
  }
  const reset = () => { setConversation(null); setTurns([]); setQuestion(''); setNotice(''); };
  const turnLimit = (conversation?.revision ?? 0) >= 100;
  const canAsk = !busy && !!status?.available && !!question.trim() && !turnLimit;
  const noticeIsProblem = notice === 'Ask.Unavailable' || notice === 'Conflict';

  const ask = () => void run(async signal => {
    const asked = question.trim();
    setAsking(asked);
    const answer = await apiFetchJson<Answer>(`${endpoint}Ask`, { method: 'POST', signal, headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ question, topic, settingId: topic === 'settings' && settingId ? settingId : null, proposedValue: topic === 'settings' && settingId ? proposedValue : null, conversationId: conversation?.id ?? null, expectedRevision: conversation?.revision ?? 0 }) });
    if (signal.aborted) return;
    setTurns(previous => [...previous, { ...answer, question: asked }].slice(-10)); setAsking('');
    if (answer.conversationId) { setConversation({ id: answer.conversationId, revision: answer.revision, modifiedOnUtc: new Date().toISOString() }); setQuestion(''); }
  });
  const open = (item: Conversation) => void run(async signal => {
    const rows = await apiFetchJson<Answer[]>(`${endpoint}Conversation`, { signal }, { conversationId: item.id });
    if (!signal.aborted) { setTurns(rows); setConversation({ ...item, revision: rows.at(-1)?.revision ?? item.revision }); setQuestion(''); setNotice('Ask.Refreshed'); }
  });
  const remove = () => conversation && void run(async signal => {
    await apiFetchJson(`${endpoint}DeleteConversation`, { method: 'POST', signal, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ conversationId: conversation.id, expectedRevision: conversation.revision }) });
    if (!signal.aborted) { reset(); setNotice('Ask.Deleted'); }
  });
  const exportConversation = () => conversation && void run(async signal => {
    const data = await apiFetchJson(`${endpoint}ConversationExport`, { signal }, { conversationId: conversation.id });
    if (signal.aborted) return;
    const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }));
    const link = document.createElement('a'); link.href = url; link.download = 'admin-assist-private-history.json'; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  });

  const evidenceState = (card: Evidence): { label: string; tone: Tone } =>
    card.kind === 'SetupTask' ? { label: ui(`TaskState.${card.state}`), tone: taskTone[card.state] ?? 'muted' }
      : card.state === 'Known' && card.kind !== 'Capability' ? { label: ui('Ask.CurrentEvidence'), tone: 'ok' }
      : { label: ui(card.state), tone: card.state === 'Unknown' ? 'unknown' : card.state === 'Known' ? 'info' : 'muted' };

  const remaining = status?.freeQuestionsRemaining ?? 0;
  const allowance = status?.freeQuestionsAllowance ?? 0;
  const usage = status && <div className={`rgaa-ask-usage${status.tier === 'Free' && allowance > 0 && remaining / allowance <= 0.2 ? ' rgaa-ask-usage--low' : ''}`}>
    {status.tier === 'Free' ? <>
      <span className="rgaa-metric__label">{ui('Ask.FreeQuestions')}</span>
      <span className="rgaa-ask-usage__value">{remaining}<small> / {allowance}</small></span>
      <ProgressBar value={remaining} total={allowance} label={`${remaining} / ${allowance}`} />
      {status.freeWindowEndsUtc && <span className="rgaa-small rgaa-muted"><i className="fa fa-calendar-o" aria-hidden="true" /> {ui('Ask.WindowEnds')}: <time dateTime={status.freeWindowEndsUtc}>{new Date(status.freeWindowEndsUtc).toLocaleDateString()}</time></span>}
    </> : <>
      <span className="rgaa-metric__label">{ui('Ask.TokensRemaining')}</span>
      <span className="rgaa-ask-usage__value">{status.tokensRemaining.toLocaleString()}</span>
    </>}
  </div>;

  const conversationTools = <div className="rgaa-ask__tools">
    {conversation && onPlan && <button type="button" className="btn btn-white btn-xs" disabled={busy} onClick={() => onPlan({ source: 'conversation', sourceId: conversation.id, goal: question })}><i className="fa fa-list-ol" aria-hidden="true" /> {t('Plan.New')}</button>}
    {conversation && <button type="button" className="btn btn-white btn-xs" disabled={busy} onClick={exportConversation}><i className="fa fa-download" aria-hidden="true" /> {ui('Ask.Export')}</button>}
    {conversation && <button type="button" className="btn btn-white btn-xs rgaa-btn-danger" disabled={busy} onClick={remove}><i className="fa fa-trash-o" aria-hidden="true" /> {ui('Ask.Delete')}</button>}
    <button type="button" className="btn btn-white btn-xs" disabled={busy || (!conversation && turns.length === 0 && !question)} onClick={reset}><i className="fa fa-plus" aria-hidden="true" /> {ui('Ask.New')}</button>
  </div>;

  return <div className="rgaa-workbench rgaa-ask" aria-busy={busy}>
    <div className="rgaa-ask__main">
      <Ibox title={<><i className="fa fa-comments-o" aria-hidden="true" /> {ui('ask')}</>} tools={conversationTools}>
        <div className="rgaa-ask__thread" ref={thread} aria-live="polite">
          {turns.length === 0 && !busy && <div className="rgaa-ask__empty">
            <span className="rgaa-ask__empty-icon" aria-hidden="true"><i className="fa fa-comments-o" /></span>
            <p>{ui('Ask.Boundary')}</p>
          </div>}
          {turns.map((turn, index) => {
            const visual = outcomeVisual(turn.outcome);
            return <article className="rgaa-ask-turn" key={`${turn.revision}-${index}`}>
              {turn.question && <p className="rgaa-ask-turn__question">{turn.question}</p>}
              <div className="rgaa-ask-turn__answer">
                <h4 className="rgaa-ask-turn__outcome"><i className={`fa ${visual.icon} rgaa-icon--${visual.tone}`} aria-hidden="true" /> {ui(`Ask.${turn.outcome}`)}</h4>
                {turn.evidence.length > 0 && <div className="rgaa-evidence-grid">{turn.evidence.map(card => {
                  const state = evidenceState(card);
                  const destination = localLink(card.destination);
                  return <section className="rgaa-evidence-card" key={card.id}>
                    <header className="rgaa-evidence-card__head"><h5>{t(card.titleKey)}</h5><Chip tone={state.tone}>{state.label}</Chip></header>
                    {card.textKeys.map((key, i) => <p key={`${key}-${i}`}>{t(key)}</p>)}
                    {Object.keys(card.numbers).length > 0 && <dl className="rgaa-evidence-card__numbers">{Object.entries(card.numbers).map(([label, value]) => <div key={label}><dt>{t(label)}</dt><dd>{value.toLocaleString()}</dd></div>)}</dl>}
                    {card.publicText && <p className="rgaa-source rgaa-evidence-card__quote">{card.publicText}</p>}
                    <footer className="rgaa-evidence-card__foot">
                      <small className="rgaa-muted">{ui('Ask.Source')}: {card.citationId} · {card.catalogVersion} · {new Date(card.asOfUtc).toLocaleString()}</small>
                      {destination && <a className="btn btn-white btn-xs" href={destination}><i className="fa fa-external-link" aria-hidden="true" /> {ui('Configure')}</a>}
                    </footer>
                  </section>;
                })}</div>}
              </div>
            </article>;
          })}
          {busy && asking && <div className="rgaa-ask-turn"><p className="rgaa-ask-turn__question">{asking}</p></div>}
          {busy && <div className="rgaa-ask__thinking" role="status"><span aria-hidden="true" /><span aria-hidden="true" /><span aria-hidden="true" /><span className="sr-only">{ui('Loading')}</span></div>}
        </div>
        {notice && <div className={`alert ${noticeIsProblem ? 'alert-warning' : 'alert-info'} rgaa-ask__notice`} role="status">{ui(notice)}</div>}
        {status && !status.available && <div className="alert alert-warning rgaa-ask__notice" role="status"><i className="fa fa-exclamation-triangle" aria-hidden="true" /> {ui(`Ask.${status.reason}`)}</div>}
        <form className="rgaa-ask__composer" onSubmit={e => { e.preventDefault(); if (canAsk) ask(); }}>
          <div className="rgaa-ask__options">
            <div className="form-group"><label htmlFor="rgaa-ask-topic">{ui('Ask.Topic')}</label>
              <select id="rgaa-ask-topic" className="form-control input-sm" value={topic} disabled={busy} onChange={e => setTopic(e.target.value)}>
                {topics.map(value => <option key={value} value={value}>{ui(`Ask.Topic.${value}`)}</option>)}
              </select></div>
            {topic === 'settings' && <div className="form-group"><label htmlFor="rgaa-ask-setting">{ui('Ask.Setting')}</label>
              <select id="rgaa-ask-setting" className="form-control input-sm" value={settingId} disabled={busy} onChange={e => { setSettingId(e.target.value); setProposedValue(''); }}>
                <option value="">{ui('Ask.NoProposal')}</option>{settings.map(item => <option key={item.id} value={item.id}>{t(item.labelKey)}</option>)}
              </select></div>}
            {topic === 'settings' && settingId && <div className="form-group"><label htmlFor="rgaa-ask-value">{ui('Ask.ProposedValue')}</label>
              {settings.find(s => s.id === settingId)?.valueType === 'boolean'
                ? <select id="rgaa-ask-value" className="form-control input-sm" value={proposedValue} required disabled={busy} onChange={e => setProposedValue(e.target.value)}><option value="">{ui('Ask.SelectValue')}</option><option value="true">{ui('True')}</option><option value="false">{ui('False')}</option></select>
                : <input id="rgaa-ask-value" className="form-control input-sm" type="number" min={0} step={1} required value={proposedValue} disabled={busy} onChange={e => setProposedValue(e.target.value)} />}
            </div>}
          </div>
          <label htmlFor="rgaa-ask-question" className="sr-only">{ui('Ask.Question')}</label>
          <div className="rgaa-ask__input">
            <textarea id="rgaa-ask-question" className="form-control" rows={3} required maxLength={2000} value={question} disabled={busy} placeholder={ui('Ask.Question')}
              onChange={e => setQuestion(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); if (canAsk) e.currentTarget.form?.requestSubmit(); } }} />
            <div className="rgaa-ask__send">
              <span className="rgaa-small rgaa-muted" aria-hidden="true">{question.length} / 2000</span>
              {busy && <button type="button" className="btn btn-white btn-sm" onClick={() => { pending.current?.abort(); setBusy(false); setAsking(''); setNotice('Ask.Cancelled'); }}><i className="fa fa-stop" aria-hidden="true" /> {ui('Ask.Cancel')}</button>}
              <button type="submit" className="btn btn-primary btn-sm" disabled={!canAsk} title="Ctrl+Enter"><i className="fa fa-paper-plane" aria-hidden="true" /> {ui('Ask.Send')}</button>
            </div>
          </div>
        </form>
      </Ibox>
    </div>
    <aside className="rgaa-workbench__side">
      {usage}
      <Ibox title={<><i className="fa fa-history" aria-hidden="true" /> {ui('Ask.History')}</>}>
        <p className="rgaa-small rgaa-muted">{ui('Ask.HistoryHelp')}</p>
        {history.length > 0
          ? <ul className="rgaa-history-list">{history.map(item => <li key={item.id}>
            <button type="button" className="rgaa-plain rgaa-history-list__item" aria-current={conversation?.id === item.id ? 'true' : undefined} disabled={busy} onClick={() => open(item)}>
              <i className="fa fa-comment-o" aria-hidden="true" />
              <time dateTime={item.modifiedOnUtc}>{new Date(item.modifiedOnUtc).toLocaleString()}</time>
              <span className="rgaa-history-list__count">{item.revision}</span>
            </button>
          </li>)}</ul>
          : <p className="rgaa-muted rgaa-small">{ui('None')}</p>}
      </Ibox>
      <div className="rgaa-privacy"><i className="fa fa-lock" aria-hidden="true" /><p>{ui('Ask.Privacy')}</p></div>
    </aside>
  </div>;
}
