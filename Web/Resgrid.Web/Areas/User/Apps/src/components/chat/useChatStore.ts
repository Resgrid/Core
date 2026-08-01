import { useCallback, useRef, useSyncExternalStore } from 'react';
import { chatStore, type ChatState } from './chatStore';

// Selector hook over the external chat store. Selectors returning derived objects should pass a
// custom isEqual so referential churn does not force re-renders.
export function useChatStore<T>(
  selector: (state: ChatState) => T,
  isEqual: (a: T, b: T) => boolean = Object.is,
): T {
  const cacheRef = useRef<{ value: T } | null>(null);

  const getSnapshot = useCallback(() => {
    const next = selector(chatStore.getState());
    if (cacheRef.current && isEqual(cacheRef.current.value, next)) {
      return cacheRef.current.value;
    }
    cacheRef.current = { value: next };
    return next;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selector, isEqual]);

  return useSyncExternalStore(chatStore.subscribe, getSnapshot, getSnapshot);
}

export function shallowArrayEqual<T>(a: readonly T[], b: readonly T[]): boolean {
  if (a === b) {
    return true;
  }
  if (a.length !== b.length) {
    return false;
  }
  for (let index = 0; index < a.length; index += 1) {
    if (a[index] !== b[index]) {
      return false;
    }
  }
  return true;
}
