import { useMemo, useState } from 'react';
import { t } from './vizI18n';
import type { VizSaved, VizTemplate } from './vizTypes';

interface VizLibraryProps {
  templates: VizTemplate[];
  saved: VizSaved[];
  activeKey: string | null;
  onOpenTemplate: (template: VizTemplate) => void;
  onOpenSaved: (saved: VizSaved) => void;
  onDeleteSaved: (saved: VizSaved) => void;
}

const categoryOrder = ['CallDemand', 'ResponsePerformance', 'Coverage', 'Resources', 'CommunityRisk', 'Tracking'];

export default function VizLibrary(props: VizLibraryProps) {
  const [showAll, setShowAll] = useState(false);
  const [filter, setFilter] = useState('');

  const recommended = props.templates.filter((template) => template.Recommended);
  const needle = filter.trim().toLowerCase();
  const matches = (template: VizTemplate) =>
    needle.length === 0 || template.Name.toLowerCase().includes(needle) || template.Description.toLowerCase().includes(needle);

  const grouped = useMemo(() => {
    const groups = new Map<string, VizTemplate[]>();
    props.templates.forEach((template) => {
      const list = groups.get(template.Category) ?? [];
      list.push(template);
      groups.set(template.Category, list);
    });
    return categoryOrder
      .filter((category) => groups.has(category))
      .map((category) => ({ category, name: groups.get(category)?.[0]?.CategoryName ?? category, templates: groups.get(category) ?? [] }));
  }, [props.templates]);

  const shared = props.saved.filter((saved) => saved.IsShared);
  const mine = props.saved.filter((saved) => !saved.IsShared && saved.IsOwner);

  const renderSaved = (list: VizSaved[]) => (
    <ul className="rg-viz__cards">
      {list.map((saved) => {
        const active = props.activeKey === `saved:${saved.MapVisualizationId}`;
        return (
          <li key={saved.MapVisualizationId} className={`rg-viz__card${active ? ' rg-viz__card--active' : ''}`}>
            <button type="button" className="rg-viz__card-main" onClick={() => props.onOpenSaved(saved)} aria-current={active ? 'true' : undefined}>
              <span className="rg-viz__card-title">{saved.Name}</span>
              {saved.Description && <span className="rg-viz__card-text">{saved.Description}</span>}
              <span className="rg-viz__card-meta">
                {saved.IsShared ? t('SavedSharedBy', saved.CreatedByName ?? '—') : t('SavedPersonal')}
              </span>
            </button>
            {saved.CanEdit && (
              <button type="button" className="rg-viz__icon-button" onClick={() => props.onDeleteSaved(saved)} title={t('Delete')} aria-label={t('DeleteNamed', saved.Name)}>
                ✕
              </button>
            )}
          </li>
        );
      })}
    </ul>
  );

  const renderTemplates = (list: VizTemplate[]) => (
    <ul className="rg-viz__cards">
      {list.filter(matches).map((template) => {
        const active = props.activeKey === `template:${template.Key}`;
        return (
          <li key={template.Key} className={`rg-viz__card${active ? ' rg-viz__card--active' : ''}`}>
            <button type="button" className="rg-viz__card-main" onClick={() => props.onOpenTemplate(template)} aria-current={active ? 'true' : undefined}>
              <span className="rg-viz__card-title">
                {template.Name}
                {template.Recommended && <span className="rg-viz__badge">{t('LibraryRecommendedBadge')}</span>}
              </span>
              <span className="rg-viz__card-text">{template.Description}</span>
              {template.NeedsCall && <span className="rg-viz__card-meta">{t('LibraryNeedsCall')}</span>}
            </button>
          </li>
        );
      })}
    </ul>
  );

  return (
    <div className="rg-viz__library">
      {(shared.length > 0 || mine.length > 0) && (
        <section className="rg-viz__section">
          <h3 className="rg-viz__section-title">{t('SavedTitle')}</h3>
          {shared.length > 0 && (
            <>
              <h4 className="rg-viz__subtitle">{t('SavedShared')}</h4>
              {renderSaved(shared)}
            </>
          )}
          {mine.length > 0 && (
            <>
              <h4 className="rg-viz__subtitle">{t('SavedMine')}</h4>
              {renderSaved(mine)}
            </>
          )}
        </section>
      )}

      <section className="rg-viz__section">
        <h3 className="rg-viz__section-title">{t('LibraryTitle')}</h3>
        <p className="rg-viz__hint">{t('LibraryHint')}</p>
        <input
          className="rg-viz__input"
          type="search"
          value={filter}
          placeholder={t('LibrarySearch')}
          aria-label={t('LibrarySearch')}
          onChange={(event) => setFilter(event.target.value)}
        />
        {!showAll && needle.length === 0 && recommended.length > 0 ? (
          <>
            <h4 className="rg-viz__subtitle">{t('LibraryRecommended')}</h4>
            {renderTemplates(recommended)}
            <button type="button" className="rg-viz__link" onClick={() => setShowAll(true)}>{t('LibraryShowAll', props.templates.length)}</button>
          </>
        ) : (
          grouped.map((group) => (
            group.templates.some(matches) && (
              <div key={group.category}>
                <h4 className="rg-viz__subtitle">{group.name}</h4>
                {renderTemplates(group.templates)}
              </div>
            )
          ))
        )}
      </section>
    </div>
  );
}
