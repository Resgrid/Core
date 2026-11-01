import { EmptyState, ItemList, Note } from '../../primitives';
import { useWidgetBadge } from '../hooks';
import type { WidgetProps } from '../types';
import { expiryText, joinMeta, rowsFor, toTone } from './readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Readiness/CertificationsMineWidgetData.cs */
export interface CertificationsMineItem {
  CertificationId: number;
  Name?: string | null;
  Status: number;
  ExpiresOn?: string | null;
  DaysUntilExpiry?: number | null;
  Tone?: string | null;
}

export interface CertificationsMineWidgetData {
  HorizonDays: number;
  Total: number;
  Expired: number;
  Expiring: number;
  Items: CertificationsMineItem[];
}

const MINE_URL = '/User/Profile/Certifications';

/** certifications.mine (plan §7.7): the viewer's certifications that have expired or expire within the horizon. */
export default function CertificationsMineWidget({ data, size, t, ctx }: WidgetProps<CertificationsMineWidgetData>) {
  const k = 'W.certifications.mine.';
  const attention = (data.Expired ?? 0) + (data.Expiring ?? 0);
  useWidgetBadge(ctx, attention > 0 ? attention : null);

  if (!data.Items?.length) {
    return <EmptyState icon="fa-certificate" text={t(`${k}Empty`, data.HorizonDays)} linkText={t(`${k}OpenMine`)} href={MINE_URL} />;
  }

  return (
    <>
      <ItemList
        items={rowsFor(data.Items, size).map((item) => ({
          key: String(item.CertificationId),
          title: item.Name || t(`${k}Unnamed`),
          href: MINE_URL,
          meta: joinMeta(expiryText(t, k, item.DaysUntilExpiry), item.ExpiresOn ? ctx.format.date(item.ExpiresOn) : null),
          tone: toTone(item.Tone),
          icon: toTone(item.Tone) === 'bad' ? undefined : 'fa-certificate',
        }))}
      />
      {data.Items.length > rowsFor(data.Items, size).length ? (
        <Note>
          <a href={MINE_URL}>{t(`${k}SeeAll`)}</a>
        </Note>
      ) : null}
    </>
  );
}
