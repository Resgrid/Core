// Shared request queue and viewport observer for widget data (plan §10: client concurrency cap 6; widgets in the
// viewport load first, the rest after).

export const MAX_CONCURRENT_REQUESTS = 6;

interface QueuedTask {
  run: () => Promise<void>;
  priority: () => number;
  cancelled: boolean;
  order: number;
}

const queue: QueuedTask[] = [];
let active = 0;
let order = 0;
let pumpScheduled = false;

function pump(): void {
  pumpScheduled = false;
  while (active < MAX_CONCURRENT_REQUESTS && queue.length > 0) {
    // Lowest priority value first (0 = in view), then first come. Priorities are read now, so a widget scrolled into
    // view since it queued moves ahead.
    let best = 0;
    for (let index = 1; index < queue.length; index += 1) {
      const a = queue[index];
      const b = queue[best];
      const pa = a.priority();
      const pb = b.priority();
      if (pa < pb || (pa === pb && a.order < b.order)) {
        best = index;
      }
    }

    const task = queue.splice(best, 1)[0];
    if (task.cancelled) {
      continue;
    }

    active += 1;
    task
      .run()
      .catch(() => undefined)
      .finally(() => {
        active -= 1;
        schedulePump();
      });
  }
}

function schedulePump(): void {
  if (pumpScheduled) {
    return;
  }
  pumpScheduled = true;
  // A macrotask, not requestAnimationFrame: a hidden tab or preview pane stalls rAF.
  setTimeout(pump, 0);
}

/** Queues `run`; returns a function that drops it if it has not started. */
export function enqueueRequest(run: () => Promise<void>, priority: () => number): () => void {
  const task: QueuedTask = { run, priority, cancelled: false, order: (order += 1) };
  queue.push(task);
  schedulePump();

  return () => {
    task.cancelled = true;
    const index = queue.indexOf(task);
    if (index >= 0) {
      queue.splice(index, 1);
    }
  };
}

type VisibilityCallback = (inView: boolean) => void;

const callbacks = new Map<Element, VisibilityCallback>();
let observer: IntersectionObserver | null = null;

/** Reports whether `element` is in (or near) the viewport; the first report arrives after the next layout. */
export function observeVisibility(element: Element, callback: VisibilityCallback): () => void {
  if (typeof IntersectionObserver === 'undefined') {
    setTimeout(() => callback(true), 0);
    return () => undefined;
  }

  if (!observer) {
    observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          callbacks.get(entry.target)?.(entry.isIntersecting);
        }
      },
      { rootMargin: '200px 0px' },
    );
  }

  callbacks.set(element, callback);
  observer.observe(element);

  return () => {
    callbacks.delete(element);
    observer?.unobserve(element);
  };
}
