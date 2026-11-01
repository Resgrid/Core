import type { HubConnection } from '@microsoft/signalr';
import { connectEventingHub } from '../../../runtime/signalr';

// One eventing hub connection per page (plan §10). Hub events become dashboard topics (DashboardTopics in
// Core/Resgrid.Model/Dashboard/DashboardWidgetDefinition.cs); widgets subscribe to the topics their definition lists.
// The chat topic has its own hub and is not connected here: chat widgets poll.

export type BusStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'offline';

/** Emitted after a reconnect (or a late first connection): events may have been missed, so live widgets refetch. */
export const RESUBSCRIBED = 'resubscribed';

/** Topics this bus can deliver. */
export const BUS_TOPICS = [
  'calls',
  'unitStatus',
  'personnelStatus',
  'personnelStaffing',
  'unitRoles',
  'unitAlerts',
  'checklists',
  'requirements',
  'incidentCommand',
] as const;

const RETRY_DELAYS_MS = [2000, 5000, 10000, 30000, 60000];

/** The topics of a definition this bus delivers (anything else, such as chat, falls back to polling). */
export function liveTopics(topics: readonly string[] | null | undefined): string[] {
  return (topics ?? []).filter((topic) => (BUS_TOPICS as readonly string[]).includes(topic));
}

export function isStale(status: BusStatus): boolean {
  return status === 'reconnecting' || status === 'offline';
}

type TopicListener = (topic: string) => void;
type StatusListener = (status: BusStatus) => void;

export class DashboardBus {
  private readonly listeners = new Map<string, Set<TopicListener>>();
  private readonly statusListeners = new Set<StatusListener>();
  private connection: HubConnection | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private disposed = false;
  private started = false;

  public status: BusStatus = 'idle';

  /** Listens for any of `topics`; returns the unsubscribe function. */
  public subscribe(topics: readonly string[], listener: TopicListener): () => void {
    for (const topic of topics) {
      let set = this.listeners.get(topic);
      if (!set) {
        set = new Set();
        this.listeners.set(topic, set);
      }
      set.add(listener);
    }

    return () => {
      for (const topic of topics) {
        this.listeners.get(topic)?.delete(listener);
      }
    };
  }

  public onStatus(listener: StatusListener): () => void {
    this.statusListeners.add(listener);
    return () => this.statusListeners.delete(listener);
  }

  public emit(topic: string): void {
    const set = this.listeners.get(topic);
    if (!set) {
      return;
    }
    for (const listener of Array.from(set)) {
      try {
        listener(topic);
      } catch (error) {
        console.error('Dashboard: a topic listener failed.', error);
      }
    }
  }

  public start(departmentId: number): void {
    if (this.started || this.disposed || !(departmentId > 0)) {
      return;
    }
    this.started = true;
    void this.connect(departmentId, 0);
  }

  public stop(): void {
    this.disposed = true;
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
    const connection = this.connection;
    this.connection = null;
    if (connection) {
      void connection.stop().catch(() => undefined);
    }
    this.listeners.clear();
    this.statusListeners.clear();
  }

  private setStatus(status: BusStatus): void {
    if (this.status === status) {
      return;
    }
    this.status = status;
    for (const listener of Array.from(this.statusListeners)) {
      listener(status);
    }
  }

  private scheduleRetry(departmentId: number, attempt: number): void {
    if (this.disposed) {
      return;
    }
    const delay = RETRY_DELAYS_MS[Math.min(attempt - 1, RETRY_DELAYS_MS.length - 1)];
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      void this.connect(departmentId, attempt);
    }, delay);
  }

  private async connect(departmentId: number, attempt: number): Promise<void> {
    if (this.disposed) {
      return;
    }
    if (attempt === 0) {
      this.setStatus('connecting');
    }

    try {
      const connection = await connectEventingHub(departmentId, {
        onCallsChanged: () => this.emit('calls'),
        onUnitStatusUpdated: () => this.emit('unitStatus'),
        onPersonnelStatusUpdated: () => this.emit('personnelStatus'),
        onPersonnelStaffingUpdated: () => this.emit('personnelStaffing'),
        onUnitActiveRolesUpdated: () => this.emit('unitRoles'),
        onUnitStatusAlertUpdated: () => this.emit('unitAlerts'),
        onChecklistUpdated: () => this.emit('checklists'),
        onOutstandingRequirementsUpdated: () => this.emit('requirements'),
        onIncidentCommandUpdated: () => this.emit('incidentCommand'),
        onResubscribed: () => {
          this.setStatus('connected');
          this.emit(RESUBSCRIBED);
        },
      });

      if (this.disposed) {
        await connection.stop().catch(() => undefined);
        return;
      }

      this.connection = connection;
      connection.onreconnecting(() => this.setStatus('reconnecting'));
      connection.onclose(() => {
        if (this.connection === connection) {
          this.connection = null;
        }
        if (!this.disposed) {
          this.setStatus('offline');
          this.scheduleRetry(departmentId, 1);
        }
      });

      this.setStatus('connected');

      // Changes made before a late first connection were missed.
      if (attempt > 0) {
        this.emit(RESUBSCRIBED);
      }
    } catch (error) {
      if (this.disposed) {
        return;
      }
      if (attempt === 0) {
        console.warn('Dashboard: realtime updates are unavailable; widgets refresh by polling until the connection returns.', error);
      }
      this.setStatus('offline');
      this.scheduleRetry(departmentId, attempt + 1);
    }
  }
}
