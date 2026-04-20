export function dispatchElementEvent<TDetail>(
  hostElement: HTMLElement,
  eventName: string,
  detail: TDetail,
): void {
  hostElement.dispatchEvent(
    new CustomEvent<TDetail>(eventName, {
      bubbles: true,
      composed: true,
      detail,
    }),
  );
}
