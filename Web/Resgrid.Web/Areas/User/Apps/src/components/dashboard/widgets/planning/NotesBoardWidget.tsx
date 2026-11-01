import { EmptyState, ItemList } from '../../primitives';
import type { WidgetProps } from '../types';
import './planningWidgets.css';

/** Core/Resgrid.Model/Dashboard/Widgets/Planning/NotesBoardWidgetData.cs */
export interface NotesBoardWidgetData {
  Items: Array<{
    NoteId: number;
    Title?: string | null;
    /** Plain text (HTML reduced server-side), ≤ 200 characters. */
    Preview?: string | null;
    Category?: string | null;
    Color?: string | null;
    IsAdminOnly: boolean;
    AddedOnUtc: string;
    ExpiresOnUtc?: string | null;
    Url: string;
  }>;
}

/** notes.board (plan §7.6): the newest department notes with plain-text previews. */
export default function NotesBoardWidget({ data, size, t, ctx }: WidgetProps<NotesBoardWidgetData>) {
  if (data.Items.length === 0) {
    return <EmptyState icon="fa-clipboard" text={t('W.notes.board.Empty')} linkText={t('W.notes.board.OpenNotes')} href="/User/Notes" />;
  }

  return (
    <ItemList
      className={size === 'l' ? 'rg-notes-wide' : undefined}
      items={data.Items.map((note) => ({
        key: String(note.NoteId),
        icon: 'fa-sticky-note-o',
        iconColor: note.Color || undefined,
        title: note.Title || t('W.notes.board.Untitled'),
        href: note.Url,
        meta: (
          <>
            {note.Preview ? <span className="rg-notes-preview">{note.Preview}</span> : null}
            <span className="rg-notes-meta">
              {[note.Category, ctx.format.date(note.AddedOnUtc), note.ExpiresOnUtc ? t('W.notes.board.Expires', ctx.format.date(note.ExpiresOnUtc)) : null]
                .filter(Boolean)
                .join(' · ')}
            </span>
          </>
        ),
        right: note.IsAdminOnly ? <span className="rg-tag">{t('W.notes.board.AdminOnly')}</span> : undefined,
      }))}
    />
  );
}
