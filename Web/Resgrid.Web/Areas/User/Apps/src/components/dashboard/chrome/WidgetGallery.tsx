import { useMemo, useRef, useState, type CSSProperties, type KeyboardEvent } from 'react';
import { liveTopics } from '../data/dashboardBus';
import { allowedSizes, sizeLabel } from '../grid/sizes';
import { t } from '../i18n';
import type { CategoryInfo, LayoutWidget, LockedWidget, WidgetDefinition } from '../types';
import { DashLayer, useFocusReturn } from './overlays';
import { DEFAULT_ACCENT, widgetTitle } from './WidgetFrame';

// "Add widget" drawer (plan §6.5, mockup gallery): search, category tabs, Add / Added, and — for department admins
// only, the server already filters — locked add-on cards linking to the add-on's billing card.

interface GalleryProps {
  definitions: WidgetDefinition[];
  locked: LockedWidget[];
  categories: CategoryInfo[];
  widgets: LayoutWidget[];
  atLimit: boolean;
  maxWidgets: number;
  onAdd: (definition: WidgetDefinition) => void;
  onClose: () => void;
}

type GalleryEntry = { kind: 'widget'; definition: WidgetDefinition } | { kind: 'locked'; locked: LockedWidget };

function matches(query: string, ...texts: Array<string | null | undefined>): boolean {
  if (!query) {
    return true;
  }
  const haystack = texts.filter(Boolean).join(' ').toLocaleLowerCase();
  return query
    .toLocaleLowerCase()
    .split(/\s+/)
    .filter(Boolean)
    .every((word) => haystack.includes(word));
}

export function WidgetGallery(props: GalleryProps) {
  const drawerRef = useRef<HTMLElement | null>(null);
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState<string>('all');
  useFocusReturn(drawerRef, 'input[type="search"]');

  const onBoard = useMemo(() => new Set(props.widgets.map((widget) => widget.Key)), [props.widgets]);

  const categories = useMemo(
    () =>
      props.categories.filter(
        (item) => props.definitions.some((definition) => definition.Category === item.Key) || props.locked.some((locked) => locked.Category === item.Key),
      ),
    [props.categories, props.definitions, props.locked],
  );

  const sections = useMemo(() => {
    const order = new Map(categories.map((item, index) => [item.Key, index]));
    const entries: GalleryEntry[] = [
      ...props.definitions.map((definition) => ({ kind: 'widget' as const, definition })),
      ...props.locked.map((locked) => ({ kind: 'locked' as const, locked })),
    ];
    const filtered = entries.filter((entry) => {
      const key = entry.kind === 'widget' ? entry.definition.Category : entry.locked.Category;
      if (category !== 'all' && key !== category) {
        return false;
      }
      return entry.kind === 'widget'
        ? matches(query, widgetTitle(entry.definition), entry.definition.Description)
        : matches(query, entry.locked.Title, entry.locked.Description, entry.locked.AddonName);
    });

    const grouped = new Map<string, GalleryEntry[]>();
    for (const entry of filtered) {
      const key = entry.kind === 'widget' ? entry.definition.Category : entry.locked.Category;
      const list = grouped.get(key) ?? [];
      list.push(entry);
      grouped.set(key, list);
    }

    return Array.from(grouped.entries())
      .sort((a, b) => (order.get(a[0]) ?? 99) - (order.get(b[0]) ?? 99))
      .map(([key, list]) => ({
        category: categories.find((item) => item.Key === key),
        key,
        entries: list.sort((a, b) => {
          // Available widgets first, locked cards after, each alphabetical.
          if (a.kind !== b.kind) return a.kind === 'widget' ? -1 : 1;
          const ta = a.kind === 'widget' ? widgetTitle(a.definition) : a.locked.Title;
          const tb = b.kind === 'widget' ? widgetTitle(b.definition) : b.locked.Title;
          return ta.localeCompare(tb);
        }),
      }));
  }, [props.definitions, props.locked, categories, category, query]);

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      props.onClose();
    }
  };

  const total = sections.reduce((sum, section) => sum + section.entries.length, 0);

  return (
    <DashLayer>
      <div className="rg-scrim" onMouseDown={props.onClose} aria-hidden="true" />
      <aside className="rg-drawer" role="dialog" aria-modal="true" aria-label={t('Dash.AddWidget')} ref={drawerRef} onKeyDown={onKeyDown}>
        <div className="rg-drawer-head">
          <h3>
            {t('Dash.AddWidget')}
            <button type="button" className="rg-iconbtn" aria-label={t('Dash.Close')} onClick={props.onClose}>
              <i className="fa fa-times" aria-hidden="true" />
            </button>
          </h3>
          <input
            className="rg-input rg-gallery-search"
            type="search"
            value={query}
            placeholder={t('Dash.SearchWidgets')}
            aria-label={t('Dash.SearchWidgets')}
            onChange={(event) => setQuery(event.target.value)}
          />
          <div className="rg-cats" role="group" aria-label={t('Dash.Categories')}>
            <button type="button" className={category === 'all' ? 'rg-on' : undefined} aria-pressed={category === 'all'} onClick={() => setCategory('all')}>
              {t('Dash.AllCategories')}
            </button>
            {categories.map((item) => (
              <button
                key={item.Key}
                type="button"
                className={category === item.Key ? 'rg-on' : undefined}
                aria-pressed={category === item.Key}
                onClick={() => setCategory(item.Key)}
              >
                {item.Name}
              </button>
            ))}
          </div>
          {props.atLimit ? <div className="rg-gallery-limit">{t('Dash.MaxWidgets', props.maxWidgets)}</div> : null}
        </div>
        <div className="rg-gallery" aria-live="polite">
          {total === 0 ? (
            <div className="rg-empty">
              <i className="fa fa-search" aria-hidden="true" />
              <span>{t('Dash.NoWidgetsMatch')}</span>
            </div>
          ) : null}
          {sections.map((section) => (
            <div key={section.key} className="rg-gallery-section">
              {category === 'all' ? <div className="rg-gsec">{section.category?.Name ?? section.key}</div> : null}
              {section.entries.map((entry) =>
                entry.kind === 'widget' ? (
                  <GalleryCard
                    key={entry.definition.Key}
                    definition={entry.definition}
                    accent={section.category?.Color || DEFAULT_ACCENT}
                    added={onBoard.has(entry.definition.Key) && !entry.definition.MultipleInstances}
                    disabled={props.atLimit}
                    onAdd={() => props.onAdd(entry.definition)}
                  />
                ) : (
                  <LockedCard key={`locked-${entry.locked.Key}`} locked={entry.locked} accent={section.category?.Color || DEFAULT_ACCENT} />
                ),
              )}
            </div>
          ))}
        </div>
      </aside>
    </DashLayer>
  );
}

function GalleryCard(props: { definition: WidgetDefinition; accent: string; added: boolean; disabled: boolean; onAdd: () => void }) {
  const { definition } = props;
  const title = widgetTitle(definition);
  const live = liveTopics(definition.RealtimeTopics).length > 0;
  const style = { '--accent': props.accent } as CSSProperties;
  return (
    <div className="rg-gcard" style={style} data-key={definition.Key}>
      <span className="rg-w-icon" aria-hidden="true">
        <i className={`fa ${definition.Icon || 'fa-th-large'}`} />
      </span>
      <div className="rg-gcard-main">
        <h4>{title}</h4>
        {definition.Description ? <p>{definition.Description}</p> : null}
        <div className="rg-gcard-meta">
          {allowedSizes(definition).map(sizeLabel).join(' · ')}
          {live ? (
            <>
              {' · '}
              <i className="fa fa-circle rg-gcard-live" aria-hidden="true" /> {t('Dash.Live')}
            </>
          ) : null}
          {definition.MultipleInstances ? ` · ${t('Dash.MultipleInstances')}` : ''}
        </div>
      </div>
      {props.added ? (
        <span className="rg-gcard-meta rg-gcard-added">
          <i className="fa fa-check" aria-hidden="true" /> {t('Dash.Added')}
        </span>
      ) : (
        <button type="button" className="btn btn-xs btn-primary" disabled={props.disabled} aria-label={t('Dash.AddNamed', title)} onClick={props.onAdd}>
          {t('Dash.Add')}
        </button>
      )}
    </div>
  );
}

function LockedCard(props: { locked: LockedWidget; accent: string }) {
  const { locked } = props;
  const style = { '--accent': props.accent } as CSSProperties;
  return (
    <div className="rg-gcard rg-locked" style={style} data-key={locked.Key}>
      <span className="rg-w-icon" aria-hidden="true">
        <i className={`fa ${locked.Icon || 'fa-th-large'}`} />
      </span>
      <div className="rg-gcard-main">
        <h4>{locked.Title}</h4>
        {locked.Description ? <p>{locked.Description}</p> : null}
        <div className="rg-lock">
          <i className="fa fa-lock" aria-hidden="true" /> {t('Dash.AvailableWith', locked.AddonName)}
          {locked.AddonUrl ? (
            <>
              {' · '}
              <a href={locked.AddonUrl}>{t('Dash.ViewAddon')}</a>
            </>
          ) : null}
        </div>
      </div>
      <span className="rg-gcard-meta">
        <i className="fa fa-lock" aria-hidden="true" />
        <span className="sr-only">{t('Dash.Locked')}</span>
      </span>
    </div>
  );
}
