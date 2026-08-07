import { useEffect, useState } from 'react';
import './chat.css';
import ChatbotElement from './ChatbotElement';
import { useChatStore } from './useChatStore';

export interface AssistantPanelElementProps {
  hostElement?: HTMLElement;
  // Localized strings supplied by the Razor host via element attributes (commonLocalizer).
  label?: string;
  closeLabel?: string;
}

// Footer button + right-hand slide-out drawer hosting the assistant conversation. The assistant
// is intentionally not a standalone page: the drawer overlays whatever the user is working on.
export default function AssistantPanelElement({ hostElement, label = 'Assistant', closeLabel = 'Close' }: AssistantPanelElementProps) {
  // Piggybacks on the chat store populated by <rg-chat>'s bootstrap (both elements share the
  // module store), so this element never issues its own channel fetch just to gate visibility.
  const available = useChatStore((state) => state.chatAvailable);
  const loaded = useChatStore((state) => state.channelsLoaded);
  const [open, setOpen] = useState(false);

  const ready = loaded && available;

  useEffect(() => {
    if (hostElement) {
      hostElement.style.display = ready ? '' : 'none';
    }
  }, [hostElement, ready]);

  useEffect(() => {
    if (!open) {
      return;
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [open]);

  if (!ready) {
    return null;
  }

  return (
    <>
      <button type="button" className="rgchat-root rgchat-footerbtn rgchat-footerbtn--assistant" aria-label={label} onClick={() => setOpen(true)}>
        <span aria-hidden="true">✨</span>
        <span>{label}</span>
      </button>
      {open && (
        <div className="rgchat-root rgchat-drawer" role="dialog" aria-modal="false" aria-label={label}>
          <div className="rgchat-drawer__head">
            <div className="rgchat-panel__title">
              <span aria-hidden="true">✨</span>
              <span>{label}</span>
            </div>
            <button type="button" className="rgchat-iconbtn" title={closeLabel} aria-label={closeLabel} onClick={() => setOpen(false)}>
              ✕
            </button>
          </div>
          <div className="rgchat-drawer__body">
            {/* Mounted only while open, so the chatbot channel/session is provisioned lazily on first use. */}
            <ChatbotElement />
          </div>
        </div>
      )}
    </>
  );
}
