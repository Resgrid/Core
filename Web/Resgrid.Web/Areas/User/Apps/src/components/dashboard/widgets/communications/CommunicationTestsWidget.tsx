import { BarList, EmptyState, ItemList, Note, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Communications/CommunicationTestsWidgetData.cs */
export interface CommunicationTestsWidgetData {
  Runs: Array<{
    CommunicationTestRunId: string;
    TestName?: string | null;
    StartedOnUtc: string;
    CompletedOnUtc?: string | null;
    Status: number;
    StatusKey: string;
    TotalUsersTested: number;
    TotalResponses: number;
    ResponseRate?: number | null;
    Url: string;
  }>;
  ChannelsVisible: boolean;
  LatestRunId?: string | null;
  LatestRunChannels: Array<{
    Channel: number;
    ChannelKey: string;
    Targeted: number;
    Suppressed: number;
    Attempted: number;
    Delivered: number;
    Responded: number;
    DeliveryRate?: number | null;
    ResponseRate?: number | null;
  }>;
}

const STATUS_TONE: Record<string, 'good' | 'warn' | 'bad' | 'info' | undefined> = {
  completed: 'good',
  failed: 'bad',
  awaitingResponses: 'info',
  running: 'info',
};

/** comms.tests (plan §7.5): recent runs with response rates; department admins also get the latest run by channel. */
export default function CommunicationTestsWidget({ data, size, t, ctx }: WidgetProps<CommunicationTestsWidgetData>) {
  if (data.Runs.length === 0) {
    return <EmptyState icon="fa-signal" text={t('W.comms.tests.Empty')} />;
  }

  const latest = data.Runs.find((run) => run.CommunicationTestRunId === data.LatestRunId) ?? data.Runs[0];
  const channels = data.ChannelsVisible ? data.LatestRunChannels.filter((channel) => channel.Attempted > 0 || channel.Targeted > 0) : [];
  const runs = channels.length > 0 && size === 'm' ? data.Runs.slice(0, 2) : data.Runs;

  return (
    <>
      {channels.length > 0 ? (
        <>
          <SubHeading>{t('W.comms.tests.LatestRun')}</SubHeading>
          <BarList
            max={100}
            items={channels.map((channel) => ({
              label: t(`W.comms.tests.Channel.${channel.ChannelKey}`),
              value: channel.ResponseRate ?? 0,
              display:
                channel.ResponseRate === null || channel.ResponseRate === undefined
                  ? t('W.comms.tests.NotDelivered')
                  : `${ctx.format.percent(channel.ResponseRate)} · ${t('W.comms.tests.DeliveredShort', ctx.format.percent(channel.DeliveryRate ?? 0))}`,
              href: latest.Url,
            }))}
          />
          <Note style={{ marginTop: 4 }}>
            {latest.TestName} · {ctx.format.date(latest.StartedOnUtc)}
          </Note>
          <SubHeading>{t('W.comms.tests.RecentRuns')}</SubHeading>
        </>
      ) : null}
      <ItemList
        items={runs.map((run) => ({
          key: run.CommunicationTestRunId,
          icon: 'fa-signal',
          tone: STATUS_TONE[run.StatusKey],
          title: run.TestName || t('W.comms.tests.Untitled'),
          href: run.Url,
          meta: `${ctx.format.dateTime(run.StartedOnUtc)} · ${t(`W.comms.tests.Status.${run.StatusKey}`)}`,
          right: (
            <span className="rg-num" title={t('W.comms.tests.RespondedOf', run.TotalResponses, run.TotalUsersTested)}>
              {run.ResponseRate === null || run.ResponseRate === undefined ? '—' : ctx.format.percent(run.ResponseRate)}
            </span>
          ),
        }))}
      />
    </>
  );
}
