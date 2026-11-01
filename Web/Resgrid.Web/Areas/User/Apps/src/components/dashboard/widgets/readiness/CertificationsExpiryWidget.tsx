import { EmptyState, ItemList, KpiGrid } from '../../primitives';
import type { WidgetProps } from '../types';
import { enc, expiryText, joinMeta, rowsFor, toneIf, toTone } from './readinessShared';

/** Core/Resgrid.Model/Dashboard/Widgets/Readiness/CertificationsExpiryWidgetData.cs */
export interface CertificationsExpiryItem {
  /** "person" (SubjectId is a user id) or "unit" (a unit id). */
  Scope: string;
  SubjectId?: string | null;
  SubjectName?: string | null;
  TypeCode?: string | null;
  TypeName?: string | null;
  Status: number;
  ExpiresOn?: string | null;
  DaysUntilExpiry?: number | null;
  Tone?: string | null;
}

export interface CertificationsExpiryWidgetData {
  HorizonDays: number;
  Expired: number;
  Expiring: number;
  Suspended: number;
  PendingVerification: number;
  Items: CertificationsExpiryItem[];
}

const MODULE_URL = '/User/Certifications';

/** certifications.expiry (plan §7.7): the Certifications dashboard totals and the most urgent person and unit records. */
export default function CertificationsExpiryWidget({ data, size, t }: WidgetProps<CertificationsExpiryWidgetData>) {
  const k = 'W.certifications.expiry.';
  const nothing = !data.Expired && !data.Expiring && !data.Suspended && !data.PendingVerification && !data.Items?.length;
  if (nothing) {
    return <EmptyState icon="fa-check-circle-o" text={t(`${k}Empty`, data.HorizonDays)} linkText={t(`${k}OpenCertifications`)} href={MODULE_URL} />;
  }

  return (
    <>
      <KpiGrid
        columns={4}
        items={[
          { value: data.Expired, label: t(`${k}Expired`), tone: toneIf(data.Expired, 'bad'), href: MODULE_URL },
          { value: data.Expiring, label: t(`${k}ExpiringWithin`, data.HorizonDays), tone: toneIf(data.Expiring, 'warn'), href: MODULE_URL },
          { value: data.Suspended, label: t(`${k}Suspended`), href: MODULE_URL },
          { value: data.PendingVerification, label: t(`${k}ToVerify`), href: MODULE_URL },
        ]}
      />
      {data.Items?.length ? (
        <ItemList
          items={rowsFor(data.Items, size, 2).map((item, index) => ({
            key: `${item.Scope}-${item.SubjectId ?? index}-${item.TypeCode ?? index}`,
            title: joinMeta(item.TypeName || item.TypeCode || t(`${k}UnknownType`), item.SubjectName || t(`${k}UnknownSubject`)),
            href:
              item.Scope === 'unit'
                ? item.SubjectId ? `/User/Certifications/Unit?unitId=${enc(item.SubjectId)}` : undefined
                : item.SubjectId ? `/User/Certifications/Person?userId=${enc(item.SubjectId)}` : undefined,
            meta: expiryText(t, k, item.DaysUntilExpiry) ?? (item.ExpiresOn ? undefined : t(`${k}NoExpiry`)),
            tone: toTone(item.Tone),
            icon: item.Scope === 'unit' ? 'fa-truck' : undefined,
          }))}
        />
      ) : null}
    </>
  );
}
