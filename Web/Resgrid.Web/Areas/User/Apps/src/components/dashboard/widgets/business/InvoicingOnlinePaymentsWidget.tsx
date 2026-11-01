import { ItemList, Note } from '../../primitives';
import type { ListItem } from '../../primitives';
import type { WidgetProps } from '../types';
import { isCompact } from './businessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Business/InvoicingOnlinePaymentsWidgetData.cs */
export interface InvoicingOnlinePaymentsWidgetData {
  AvailableInCluster: boolean;
  FlagEnabled: boolean;
  EnabledByDepartment: boolean;
  CanCollect: boolean;
  /** payments_* code of the first failing gate, or null. */
  BlockedReason?: string | null;
  Connected: boolean;
  ConnectionName?: string | null;
  DefaultCurrency?: string | null;
  ConnectedOn?: string | null;
  LastVerifiedOn?: string | null;
  ConnectionHasError: boolean;
  ChargesEnabled: boolean;
  PayoutsEnabled: boolean;
}

const SETTINGS_URL = '/User/Invoicing/Settings?tab=online';

/** invoicing.onlinePayments (plan §7.9): can customers pay online now, and the connected account's state. */
export default function InvoicingOnlinePaymentsWidget({ data, size, t, ctx }: WidgetProps<InvoicingOnlinePaymentsWidgetData>) {
  const k = 'W.invoicing.onlinePayments.';
  const items: ListItem[] = [
    {
      key: 'state',
      title: data.CanCollect ? t(`${k}Collecting`) : t(`${k}NotCollecting`),
      meta: !data.CanCollect && data.BlockedReason ? t(`${k}Blocked.${data.BlockedReason}`) : undefined,
      tone: data.CanCollect ? 'good' : 'warn',
      href: SETTINGS_URL,
    },
  ];

  if (data.Connected || data.ConnectionName) {
    items.push({
      key: 'account',
      title: data.ConnectionName || t(`${k}Connected`),
      meta: data.LastVerifiedOn ? `${t(`${k}LastVerified`)} ${ctx.format.date(data.LastVerifiedOn)}` : data.DefaultCurrency || undefined,
      icon: 'fa-credit-card',
    });
  }

  if (!isCompact(size) && data.Connected) {
    const onOff = (value: boolean) => (value ? t(`${k}On`) : t(`${k}Off`));
    items.push({
      key: 'capabilities',
      title: `${t(`${k}Charges`)}: ${onOff(data.ChargesEnabled)} · ${t(`${k}Payouts`)}: ${onOff(data.PayoutsEnabled)}`,
      tone: data.ChargesEnabled && data.PayoutsEnabled ? 'good' : 'warn',
    });
  }

  return (
    <>
      <ItemList items={items} />
      {data.ConnectionHasError ? (
        <Note>
          <i className="fa fa-exclamation-triangle" aria-hidden="true" /> {t(`${k}ConnectionError`)}
        </Note>
      ) : null}
    </>
  );
}
