declare global {
  interface Window {
    rgModerationI18n?: Record<string, string>;
  }
}

export function moderationText(key: string, ...arguments_: Array<string | number>): string {
  const template = window.rgModerationI18n?.[key] ?? key;
  return arguments_.reduce<string>(
    (value, argument, index) => value.replaceAll(`{${index}}`, String(argument)),
    template,
  );
}
