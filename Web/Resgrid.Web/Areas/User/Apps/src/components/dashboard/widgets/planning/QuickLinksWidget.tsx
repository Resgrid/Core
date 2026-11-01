import { EmptyState, ItemList, Note, SubHeading } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/QuickLinksWidgetData.cs */
export interface QuickLinksWidgetData {
  DepartmentLinksAvailable: boolean;
  DepartmentLinks: Array<{
    DepartmentLinkId: number;
    DepartmentName?: string | null;
    Color?: string | null;
    SharesCalls: boolean;
    SharesUnits: boolean;
    SharesPersonnel: boolean;
    Url: string;
  }>;
  /** Absolute http(s) URLs only (validated server-side). */
  UserLinks: Array<{ Label: string; Url: string }>;
  InvalidUserLinks: number;
}

function isWebUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch {
    return false;
  }
}

/** links.quick (plan §7.6): departments sharing data with this one, and the member's own links (edited in the settings). */
export default function QuickLinksWidget({ data, t, ctx }: WidgetProps<QuickLinksWidgetData>) {
  // The server already dropped anything but http(s); checking again keeps a stale or forged payload from rendering a script URL.
  const links = data.UserLinks.filter((link) => isWebUrl(link.Url));

  if (data.DepartmentLinks.length === 0 && links.length === 0) {
    return <EmptyState icon="fa-link" text={t('W.links.quick.Empty')} linkText={t('W.links.quick.AddLinks')} onAction={ctx.openSettings} />;
  }

  return (
    <>
      {data.DepartmentLinks.length > 0 ? (
        <>
          <SubHeading>{t('W.links.quick.DepartmentLinks')}</SubHeading>
          <ItemList
            items={data.DepartmentLinks.map((link) => ({
              key: `d${link.DepartmentLinkId}`,
              icon: 'fa-link',
              iconColor: link.Color || undefined,
              title: link.DepartmentName || t('W.links.quick.LinkedDepartment'),
              href: link.Url,
              meta: [link.SharesCalls ? t('W.links.quick.Shares.calls') : null, link.SharesUnits ? t('W.links.quick.Shares.units') : null, link.SharesPersonnel ? t('W.links.quick.Shares.personnel') : null]
                .filter(Boolean)
                .join(' · ') || undefined,
            }))}
          />
        </>
      ) : null}
      {links.length > 0 ? (
        <>
          <SubHeading>{t('W.links.quick.MyLinks')}</SubHeading>
          <ItemList
            items={links.map((link, index) => ({
              key: `u${index}`,
              icon: 'fa-external-link',
              title: (
                <a href={link.Url} target="_blank" rel="noopener noreferrer">
                  {link.Label}
                </a>
              ),
            }))}
          />
        </>
      ) : null}
      {data.InvalidUserLinks > 0 ? <Note style={{ marginTop: 6 }}>{t('W.links.quick.InvalidLinks', data.InvalidUserLinks)}</Note> : null}
    </>
  );
}
