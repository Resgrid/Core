import { EmptyState, ItemList } from '../../primitives';
import type { WidgetProps } from '../types';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/DocumentsRecentWidgetData.cs */
export interface DocumentsRecentWidgetData {
  Items: Array<{
    DocumentId: number;
    Name?: string | null;
    Category?: string | null;
    Type?: string | null;
    /** "fa fa-file-pdf-o" */
    Icon?: string | null;
    AdminsOnly: boolean;
    AddedOnUtc: string;
    Url: string;
  }>;
}

function iconClass(icon: string | null | undefined): string {
  const name = (icon ?? '').split(/\s+/).find((part) => part.startsWith('fa-'));
  return name ?? 'fa-file-o';
}

/** documents.recent (plan §7.6): the newest documents in the department library. */
export default function DocumentsRecentWidget({ data, t, ctx }: WidgetProps<DocumentsRecentWidgetData>) {
  if (data.Items.length === 0) {
    return <EmptyState icon="fa-file-archive-o" text={t('W.documents.recent.Empty')} linkText={t('W.documents.recent.OpenDocuments')} href="/User/Documents" />;
  }

  return (
    <ItemList
      items={data.Items.map((document) => ({
        key: String(document.DocumentId),
        icon: iconClass(document.Icon),
        title: document.Name || t('W.documents.recent.Untitled'),
        href: document.Url,
        meta: [document.Category, ctx.format.date(document.AddedOnUtc)].filter(Boolean).join(' · '),
        right: document.AdminsOnly ? <span className="rg-tag">{t('W.documents.recent.AdminsOnly')}</span> : undefined,
      }))}
    />
  );
}
