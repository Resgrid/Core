import { useEffect, type RefObject } from 'react';

// Closes a popover on Escape or on pointer-down outside the given container.
export function usePopoverClose(containerRef: RefObject<HTMLElement | null>, open: boolean, onClose: () => void): void {
  useEffect(() => {
    if (!open) {
      return;
    }
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.stopPropagation();
        onClose();
      }
    };
    const handlePointer = (event: PointerEvent) => {
      const container = containerRef.current;
      if (container && event.target instanceof Node && !container.contains(event.target)) {
        onClose();
      }
    };
    document.addEventListener('keydown', handleKey, true);
    document.addEventListener('pointerdown', handlePointer);
    return () => {
      document.removeEventListener('keydown', handleKey, true);
      document.removeEventListener('pointerdown', handlePointer);
    };
  }, [containerRef, open, onClose]);
}
