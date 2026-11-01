import ChatbotElement from '../../../chat/ChatbotElement';
import { EmptyState } from '../../primitives';
import type { WidgetProps } from '../types';
import './intelligence.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Intelligence/AssistantWidgetData.cs */
export interface AssistantWidgetData {
  Available: boolean;
}

/**
 * ai.assistant (plan §7.11): the existing assistant conversation in place. <rg-assistant> is the footer launcher whose
 * drawer renders ChatbotElement (the component behind <rg-chatbot>); the widget renders that same component directly —
 * not the custom element, whose own React root would be unmounted synchronously inside the dashboard's render when the
 * board changes. Its text-only restrictions and the chat hub stay exactly as in the drawer, and the footer launcher is
 * not duplicated on the page.
 */
export default function AssistantWidget({ data, t, ctx }: WidgetProps<AssistantWidgetData>) {
  if (!data.Available) {
    return <EmptyState icon="fa-magic" text={t('W.ai.assistant.Unavailable')} />;
  }

  // Customize mode covers bodies anyway; keep the conversation out of the tab order while editing.
  return (
    <div className="rgchat-root rg-assistant-embed" aria-hidden={ctx.editing || undefined}>
      <ChatbotElement />
    </div>
  );
}
