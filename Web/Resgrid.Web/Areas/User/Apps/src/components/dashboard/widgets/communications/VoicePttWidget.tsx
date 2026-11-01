import { EmptyState, ItemList, Note } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Communications/VoicePttWidgetData.cs */
export interface VoicePttWidgetData {
  UtilizationAvailable: boolean;
  SeatLimit: number;
  CurrentlyActive: number;
  Channels: Array<{
    DepartmentVoiceChannelId: string;
    Name?: string | null;
    IsDefault: boolean;
    IsOnDemand: boolean;
    CallId?: number | null;
    ConferenceNumber: number;
  }>;
}

const VOICE_URL = '/User/Voice';

/** voice.ptt (plan §7.5): the department's Push-To-Talk channels and how many seats are in use. */
export default function VoicePttWidget({ data, size, t, ctx }: WidgetProps<VoicePttWidgetData>) {
  if (data.Channels.length === 0) {
    return <EmptyState icon="fa-microphone-slash" text={t('W.voice.ptt.Empty')} linkText={t('W.voice.ptt.Open')} href={VOICE_URL} />;
  }

  const channels = size === 's' ? data.Channels.slice(0, 3) : data.Channels;

  return (
    <>
      <div className="rg-m" style={{ marginBottom: 6 }}>
        {data.UtilizationAvailable
          ? t('W.voice.ptt.Seats', ctx.format.number(data.CurrentlyActive, 0), ctx.format.number(data.SeatLimit, 0))
          : t('W.voice.ptt.UtilizationUnavailable')}
      </div>
      <ItemList
        items={channels.map((channel) => ({
          key: channel.DepartmentVoiceChannelId,
          icon: channel.IsOnDemand ? 'fa-fire' : 'fa-microphone',
          title: channel.Name || t('W.voice.ptt.Channel'),
          href: VOICE_URL,
          meta: [channel.IsDefault ? t('W.voice.ptt.Default') : null, channel.IsOnDemand ? t('W.voice.ptt.Incident') : null].filter(Boolean).join(' · ') || undefined,
          right: channel.ConferenceNumber > 0 ? <span className="rg-mu rg-num">#{channel.ConferenceNumber}</span> : undefined,
        }))}
      />
      {data.Channels.length > channels.length ? <Note>{t('W.voice.ptt.MoreChannels', data.Channels.length - channels.length)}</Note> : null}
    </>
  );
}
