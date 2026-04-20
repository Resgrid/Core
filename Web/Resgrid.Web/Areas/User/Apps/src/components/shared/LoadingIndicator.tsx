interface LoadingIndicatorProps {
  label: string;
}

export default function LoadingIndicator({ label }: LoadingIndicatorProps) {
  return (
    <div className="rg-loading" role="status" aria-live="polite">
      <div className="rg-loading__stack">
        <span className="rg-loading__spinner" aria-hidden="true" />
        <span>{label}</span>
      </div>
    </div>
  );
}
