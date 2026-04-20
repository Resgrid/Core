import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { getAccessToken } from './auth';
import { getBrowserConfig } from './browserConfig';

export interface PersonnelLocationUpdate {
  userId: string;
  departmentId: number;
  latitude: number;
  longitude: number;
}

export interface UnitLocationUpdate {
  unitId: string;
  departmentId: number;
  latitude: number;
  longitude: number;
}

export interface GeolocationHandlers {
  onPersonnelLocationUpdated: (update: PersonnelLocationUpdate) => void;
  onUnitLocationUpdated: (update: UnitLocationUpdate) => void;
}

export async function connectGeolocationHub(handlers: GeolocationHandlers): Promise<HubConnection | null> {
  const accessToken = getAccessToken();

  if (accessToken.length === 0) {
    return null;
  }

  const { channelUrl } = getBrowserConfig();
  const connection = new HubConnectionBuilder()
    .withUrl(`${channelUrl}/geolocationHub?access_token=${encodeURIComponent(accessToken)}`)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();

  connection.on('onPersonnelLocationUpdated', handlers.onPersonnelLocationUpdated);
  connection.on('onUnitLocationUpdated', handlers.onUnitLocationUpdated);

  await connection.start();
  await connection.invoke('geolocationConnect');

  return connection;
}
