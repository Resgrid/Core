import { useEffect, useRef, useState, type Dispatch, type RefObject, type SetStateAction } from 'react';
import { dashboardApi, DashboardHttpError } from '../api';
import type { WidgetDefinition, WidgetError } from '../types';
import { liveTopics, RESUBSCRIBED, type BusStatus, type DashboardBus } from './dashboardBus';
import { enqueueRequest, observeVisibility } from './scheduler';

// Widget data loading (plan §10, §9A.4):
// - GET WidgetData for stored instances, POST WidgetPreview for unsaved ones (new or edited settings, template mode);
// - at most 6 requests at a time, widgets in the viewport first;
// - timer polling at the definition's DefaultRefreshSeconds, paused while the tab is hidden, and every widget older
//   than its interval refetches when the tab becomes visible again. Widgets with realtime topics refresh on the topic
//   (1.5 s trailing debounce) and only poll slowly as a safety net while the hub is connected;
// - ADP: while the page is revealed, widgets with protected fields attach the grant, stop timer polling and refresh
//   only on a topic or manual refresh, never more often than every 120 s. Reveal and conceal refetch at once.

export type WidgetRequest =
  | { kind: 'stored'; instanceId: string; boardId: string | null }
  | { kind: 'preview'; key: string; size: string; settings: Record<string, unknown> };

export type WidgetDataStatus = 'idle' | 'loading' | 'ready' | 'error';

export interface WidgetDataState {
  status: WidgetDataStatus;
  data: unknown;
  error: WidgetError | null;
  generatedUtc: string | null;
  /** Client time of the last completed fetch (drives the "updated" age). */
  fetchedAt: number | null;
  /** A refetch is in flight while data is showing. */
  refreshing: boolean;
  /** The last background refresh failed; the data shown is from an earlier fetch. */
  refreshFailed: boolean;
}

export interface UseWidgetDataOptions {
  definition: WidgetDefinition;
  /** False when nothing should load (no client component for the key yet). */
  enabled: boolean;
  /** Changes whenever the data-relevant parts of the instance change (key, size, settings); a change refetches. */
  signature: string;
  /** Read at request time, so a save in between switches a preview to the stored read. */
  getRequest: () => WidgetRequest;
  /** The page-level ADP reveal state. */
  revealed: boolean;
  bus: DashboardBus | null;
  busStatus: BusStatus;
  elementRef: RefObject<Element | null>;
}

export interface WidgetDataHandle extends WidgetDataState {
  /** Manual refresh. Returns false when it was deferred by the revealed-widget 120 s floor. */
  refresh: () => boolean;
}

const TOPIC_DEBOUNCE_MS = 1500;
const TOPIC_MAX_WAIT_MS = 5000;
const REVEALED_MIN_INTERVAL_MS = 120000;
const REQUEST_TIMEOUT_MS = 30000;
const MIN_POLL_SECONDS = 15;
const SAFETY_POLL_FACTOR = 5;
const SAFETY_POLL_MIN_SECONDS = 300;
const FIRST_VIEW_FALLBACK_MS = 400;

const initialState: WidgetDataState = {
  status: 'idle',
  data: null,
  error: null,
  generatedUtc: null,
  fetchedAt: null,
  refreshing: false,
  refreshFailed: false,
};

type Timer = ReturnType<typeof setTimeout>;

class WidgetLoader {
  public inView: boolean | null = null;
  public started = false;
  public disposed = false;

  private cancelQueued: (() => void) | null = null;
  private controller: AbortController | null = null;
  private lastFetch = 0;
  private pollTimer: Timer | null = null;
  private topicTimer: Timer | null = null;
  private topicFirst = 0;
  private pendingVisible = false;
  private hasData = false;

  constructor(
    private readonly options: () => UseWidgetDataOptions,
    private readonly setState: Dispatch<SetStateAction<WidgetDataState>>,
  ) {}

  public start(): void {
    if (this.started || this.disposed) {
      return;
    }
    this.started = true;
    this.load();
  }

  public load(clearData = false): void {
    if (this.disposed || !this.options().enabled) {
      return;
    }

    this.cancelQueued?.();
    if (clearData) {
      this.hasData = false;
      this.controller?.abort();
      this.controller = null;
      this.setState({ ...initialState, status: 'loading' });
    }

    this.cancelQueued = enqueueRequest(
      () => this.fetch(),
      () => (this.inView === false ? 1 : 0),
    );
  }

  public refresh(): boolean {
    if (this.revealedProtected()) {
      const wait = this.lastFetch + REVEALED_MIN_INTERVAL_MS - Date.now();
      if (wait > 0) {
        this.clearTopicTimer();
        this.topicTimer = setTimeout(() => {
          this.topicTimer = null;
          this.load();
        }, wait);
        return false;
      }
    }

    this.load();
    return true;
  }

  public onTopic(): void {
    if (this.disposed || !this.started || !this.options().enabled) {
      return;
    }

    const now = Date.now();
    if (this.topicTimer) {
      clearTimeout(this.topicTimer);
    } else {
      this.topicFirst = now;
    }

    // Trailing debounce so a burst of unit-status events coalesces, capped so a steady stream still refreshes.
    let delay = Math.min(TOPIC_DEBOUNCE_MS, Math.max(0, this.topicFirst + TOPIC_MAX_WAIT_MS - now));
    if (this.revealedProtected()) {
      delay = Math.max(delay, this.lastFetch + REVEALED_MIN_INTERVAL_MS - now);
    }

    this.topicTimer = setTimeout(() => {
      this.topicTimer = null;
      if (document.visibilityState === 'hidden') {
        this.pendingVisible = true;
        return;
      }
      this.load();
    }, delay);
  }

  public onVisible(): void {
    if (this.disposed || !this.started || !this.options().enabled) {
      return;
    }

    if (this.pendingVisible && this.revealedProtected()) {
      this.pendingVisible = false;
      this.onTopic();
      return;
    }

    const interval = this.pollInterval();
    if (this.pendingVisible || (interval !== null && Date.now() - this.lastFetch >= interval)) {
      this.pendingVisible = false;
      this.load();
    } else {
      this.schedulePoll();
    }
  }

  /** Reveal/conceal: refetch now, past the 120 s floor; on conceal the revealed values leave the DOM first. */
  public onRevealChanged(revealed: boolean): void {
    if (!this.started || this.disposed) {
      return;
    }
    this.clearTopicTimer();
    this.clearPoll();
    this.load(!revealed);
  }

  public reschedule(): void {
    if (this.started && !this.controller && !this.cancelQueued) {
      this.schedulePoll();
    }
  }

  public dispose(): void {
    this.disposed = true;
    this.cancelQueued?.();
    this.cancelQueued = null;
    this.controller?.abort();
    this.controller = null;
    this.clearPoll();
    this.clearTopicTimer();
  }

  private revealedProtected(): boolean {
    const options = this.options();
    return options.revealed && options.definition.HasProtectedFields;
  }

  private pollInterval(): number | null {
    const options = this.options();
    const definition = options.definition;
    if (this.revealedProtected()) {
      return null;
    }

    const seconds = Math.max(definition.DefaultRefreshSeconds || 300, definition.MinRefreshSeconds || 0, MIN_POLL_SECONDS);
    const live = options.bus !== null && liveTopics(definition.RealtimeTopics).length > 0;
    if (live && options.busStatus === 'connected') {
      return Math.max(seconds * SAFETY_POLL_FACTOR, SAFETY_POLL_MIN_SECONDS) * 1000;
    }
    return seconds * 1000;
  }

  private schedulePoll(): void {
    this.clearPoll();
    if (this.disposed || !this.options().enabled) {
      return;
    }

    const interval = this.pollInterval();
    if (interval === null) {
      return;
    }

    const due = Math.max(1000, this.lastFetch + interval - Date.now());
    this.pollTimer = setTimeout(() => {
      this.pollTimer = null;
      // Hidden: skip; onVisible refetches anything older than its interval.
      if (document.visibilityState !== 'hidden') {
        this.load();
      }
    }, due);
  }

  private clearPoll(): void {
    if (this.pollTimer) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }

  private clearTopicTimer(): void {
    if (this.topicTimer) {
      clearTimeout(this.topicTimer);
      this.topicTimer = null;
    }
  }

  private async fetch(): Promise<void> {
    this.cancelQueued = null;
    if (this.disposed) {
      return;
    }

    this.controller?.abort();
    const controller = new AbortController();
    this.controller = controller;
    let timedOut = false;
    const timeout = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, REQUEST_TIMEOUT_MS);

    this.lastFetch = Date.now();
    this.clearPoll();
    this.setState((state) =>
      this.hasData && state.status === 'ready' ? { ...state, refreshing: true } : { ...state, status: 'loading', refreshing: false },
    );

    try {
      const options = this.options();
      const request = options.getRequest();
      const grant = options.revealed && options.definition.HasProtectedFields;
      const result =
        request.kind === 'stored'
          ? await dashboardApi.widgetData(request.instanceId, request.boardId, { signal: controller.signal, grant })
          : await dashboardApi.widgetPreview(
              { Key: request.key, Size: request.size, Settings: request.settings },
              { signal: controller.signal, grant },
            );

      if (this.disposed || this.controller !== controller) {
        return;
      }

      const now = Date.now();
      if (result?.Error?.Code) {
        this.hasData = false;
        this.setState({
          status: 'error',
          data: null,
          error: result.Error,
          generatedUtc: result.GeneratedUtc ?? null,
          fetchedAt: now,
          refreshing: false,
          refreshFailed: false,
        });
      } else {
        this.hasData = true;
        this.setState({
          status: 'ready',
          data: result?.Data ?? null,
          error: null,
          generatedUtc: result?.GeneratedUtc ?? null,
          fetchedAt: now,
          refreshing: false,
          refreshFailed: false,
        });
      }
    } catch (error) {
      if (this.disposed || this.controller !== controller || (controller.signal.aborted && !timedOut)) {
        return;
      }

      const forbidden = error instanceof DashboardHttpError && (error.status === 401 || error.status === 403);
      if (this.hasData && !forbidden) {
        // A background refresh failed (network, timeout): keep the last data and say so in the frame.
        this.setState((state) => ({ ...state, refreshing: false, refreshFailed: true }));
      } else {
        this.hasData = false;
        this.setState({
          status: 'error',
          data: null,
          error: { Code: forbidden ? 'forbidden' : 'failed', MessageKey: forbidden ? 'WidgetErrorForbidden' : 'WidgetErrorFailed' },
          generatedUtc: null,
          fetchedAt: Date.now(),
          refreshing: false,
          refreshFailed: false,
        });
      }
    } finally {
      clearTimeout(timeout);
      if (this.controller === controller) {
        this.controller = null;
        this.schedulePoll();
      }
    }
  }
}

export function useWidgetData(options: UseWidgetDataOptions): WidgetDataHandle {
  const [state, setState] = useState<WidgetDataState>(initialState);
  const optionsRef = useRef(options);
  optionsRef.current = options;

  const loaderRef = useRef<WidgetLoader | null>(null);
  if (!loaderRef.current) {
    loaderRef.current = new WidgetLoader(() => optionsRef.current, setState);
  }
  const loader = loaderRef.current;

  // Mount: wait for the first viewport report so in-view widgets queue ahead, with a fallback for a hidden tab
  // (IntersectionObserver reports only after a rendering update, which a background tab may not run).
  useEffect(() => {
    loader.disposed = false;
    const element = optionsRef.current.elementRef.current;
    const fallback = setTimeout(() => {
      if (loader.inView === null) {
        loader.inView = false;
        loader.start();
      }
    }, FIRST_VIEW_FALLBACK_MS);

    const stopObserving = element
      ? observeVisibility(element, (inView) => {
          loader.inView = inView;
          loader.start();
        })
      : (() => {
          loader.inView = true;
          loader.start();
          return () => undefined;
        })();

    const onVisibility = () => {
      if (document.visibilityState === 'visible') {
        loader.onVisible();
      }
    };
    document.addEventListener('visibilitychange', onVisibility);

    return () => {
      clearTimeout(fallback);
      stopObserving();
      document.removeEventListener('visibilitychange', onVisibility);
      loader.dispose();
    };
  }, [loader]);

  const firstSignature = useRef(true);
  useEffect(() => {
    if (firstSignature.current) {
      firstSignature.current = false;
      return;
    }
    if (loader.started) {
      loader.load();
    }
  }, [loader, options.signature]);

  const firstEnabled = useRef(true);
  useEffect(() => {
    if (firstEnabled.current) {
      firstEnabled.current = false;
      return;
    }
    if (options.enabled && loader.started) {
      loader.load();
    }
  }, [loader, options.enabled]);

  const firstRevealed = useRef(true);
  useEffect(() => {
    if (firstRevealed.current) {
      firstRevealed.current = false;
      return;
    }
    if (optionsRef.current.definition.HasProtectedFields) {
      loader.onRevealChanged(options.revealed);
    }
  }, [loader, options.revealed]);

  useEffect(() => {
    loader.reschedule();
  }, [loader, options.busStatus]);

  const topicsKey = liveTopics(options.definition.RealtimeTopics).join(',');
  useEffect(() => {
    const bus = options.bus;
    if (!bus || !topicsKey) {
      return undefined;
    }
    return bus.subscribe([...topicsKey.split(','), RESUBSCRIBED], () => loader.onTopic());
  }, [loader, options.bus, topicsKey]);

  return { ...state, refresh: () => loader.refresh() };
}
