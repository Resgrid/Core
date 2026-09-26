import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
  type IRetryPolicy,
  type RetryContext,
} from '@microsoft/signalr';
import { getEventingToken } from './eventingToken';
import { getBrowserConfig } from './browserConfig';

export interface PersonnelLocationUpdate {
  userId: string;
  departmentId: number;
  latitude: number;
  longitude: number;
  recordId?: string;
  /** UTC ISO-8601 time of the fix; absent from servers that predate it. */
  timestamp?: string | null;
}

export interface UnitLocationUpdate {
  unitId: string;
  departmentId: number;
  latitude: number;
  longitude: number;
  recordId?: string;
  /** UTC ISO-8601 time of the fix; absent from servers that predate it. */
  timestamp?: string | null;
}

export interface GeolocationHandlers {
  onPersonnelLocationUpdated: (update: PersonnelLocationUpdate) => void;
  onUnitLocationUpdated: (update: UnitLocationUpdate) => void;
  /** Called after an automatic reconnect has re-joined the department group. Updates sent while offline were missed. */
  onResubscribed?: () => void;
}

// Retry forever: a live map stays open for hours, and the default policy gives up after four attempts.
class CappedRetryPolicy implements IRetryPolicy {
  private static readonly delays = [0, 2000, 5000, 10000, 30000];

  public nextRetryDelayInMilliseconds(retryContext: RetryContext): number {
    const index = Math.min(retryContext.previousRetryCount, CappedRetryPolicy.delays.length - 1);
    return CappedRetryPolicy.delays[index];
  }
}

export async function connectGeolocationHub(handlers: GeolocationHandlers): Promise<HubConnection | null> {
  const { channelUrl } = getBrowserConfig();
  const connection = new HubConnectionBuilder()
	.withUrl(`${channelUrl}/geolocationHub`, { accessTokenFactory: getEventingToken })
    .withAutomaticReconnect(new CappedRetryPolicy())
    .configureLogging(LogLevel.Information)
    .build();

  connection.on('onPersonnelLocationUpdated', handlers.onPersonnelLocationUpdated);
  connection.on('onUnitLocationUpdated', handlers.onUnitLocationUpdated);

  // Group membership belongs to the connection id and a reconnect gets a new one, so the
  // department group has to be re-joined or the map silently stops receiving updates.
  connection.onreconnected(async () => {
    try {
      await connection.invoke('geolocationConnect');
      handlers.onResubscribed?.();
    } catch (error) {
      console.error('Unable to re-join realtime geolocation updates after reconnecting.', error);
    }
  });

  try {
    await connection.start();
    await connection.invoke('geolocationConnect');
  } catch (error) {
    await connection.stop().catch(() => undefined);
    throw error;
  }

  return connection;
}
