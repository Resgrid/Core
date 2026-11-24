import './aiPromptWarning.css';

declare global {
  interface Window {
    /** Localized "never include PII or PHI" notice, set by _UserLayout from Common.AiPromptPiiWarning. */
    rgAiPromptWarning?: string;
  }
}

const FALLBACK = 'Never include personal information (PII) or health information (PHI) in your prompt: no names, phone numbers, addresses, patient details or incident narratives. AI prompts may be sent to the AI model.';

export function aiPromptWarningText(): string {
  return (typeof window !== 'undefined' && window.rgAiPromptWarning?.trim()) || FALLBACK;
}

/**
 * Shown directly above every free-text box whose content is sent to an AI model (Admin Assist ask, the AI assistant
 * chat). One string for all of them, so the warning cannot drift between prompts.
 */
export default function AiPromptWarning({ className, id }: { className?: string; id?: string }) {
  return (
    <div id={id} className={`rg-ai-prompt-warning${className ? ` ${className}` : ''}`} role="note">
      <i className="fa fa-exclamation-triangle" aria-hidden="true" />
      <span>{aiPromptWarningText()}</span>
    </div>
  );
}
