import { useEffect } from 'react';
import { useChatStore } from '../useChatStore';
import { setNotice } from '../chatStore';

// Slim realtime status banner rendered at the top of conversation surfaces.
export function ConnectionBanner() {
  const status = useChatStore((state) => state.connectionStatus);
  if (status === 'connected') {
    return null;
  }
  return (
    <div className={`rgchat-statusbar${status === 'offline' ? ' rgchat-statusbar--offline' : ''}`} role="status">
      {status === 'reconnecting' ? 'Reconnecting…' : 'Offline — messages may be delayed'}
    </div>
  );
}

// Rendered when any chat API call returns 401.
export function AuthErrorNotice() {
  const authError = useChatStore((state) => state.authError);
  if (!authError) {
    return null;
  }
  return (
    <div className="rgchat-statusbar rgchat-statusbar--offline" role="alert">
      Session expired — please refresh the page.
    </div>
  );
}

// Transient notice (e.g. removed from a channel). Auto-dismisses.
export function NoticeToast() {
  const notice = useChatStore((state) => state.notice);
  useEffect(() => {
    if (!notice) {
      return;
    }
    const timer = setTimeout(() => setNotice(null), 6000);
    return () => clearTimeout(timer);
  }, [notice]);

  if (!notice) {
    return null;
  }
  return (
    <div className="rgchat-toast" role="status">
      <span>{notice}</span>
      <button type="button" className="rgchat-toast__close" aria-label="Dismiss" onClick={() => setNotice(null)}>
        ✕
      </button>
    </div>
  );
}
