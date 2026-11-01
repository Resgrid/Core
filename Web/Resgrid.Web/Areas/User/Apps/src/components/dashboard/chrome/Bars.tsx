import { t } from '../i18n';
import type { BoardSummary } from '../types';

export function EditBar(props: { onAdd: () => void; onReset: () => void; onCancel: () => void; onDone: () => void; saving: boolean }) {
  return (
    <div className="rg-editbar" role="region" aria-label={t('Dash.CustomizeRegion')}>
      <div className="rg-editbar-text">
        <b>{t('Dash.EditBarTitle')}</b> {t('Dash.EditBarText')}
      </div>
      <button type="button" className="btn btn-sm btn-primary" onClick={props.onAdd}>
        <i className="fa fa-plus" aria-hidden="true" /> {t('Dash.AddWidget')}
      </button>
      <button type="button" className="btn btn-sm btn-link" onClick={props.onReset}>
        {t('Dash.ResetToDefault')}
      </button>
      <button type="button" className="btn btn-sm btn-link" onClick={props.onCancel}>
        {t('Dash.Cancel')}
      </button>
      <button type="button" className="btn btn-sm btn-success" onClick={props.onDone} disabled={props.saving}>
        <i className={`fa ${props.saving ? 'fa-spinner fa-spin' : 'fa-check'}`} aria-hidden="true" /> {t('Dash.Done')}
      </button>
    </div>
  );
}

export function TemplateEditBar(props: { onAdd: () => void; onSave: () => void; saving: boolean; backUrl: string }) {
  return (
    <div className="rg-editbar" role="region" aria-label={t('Dash.CustomizeRegion')}>
      <div className="rg-editbar-text">
        <b>{t('Dash.TemplateEditBarTitle')}</b> {t('Dash.TemplateEditBarText')}
      </div>
      <button type="button" className="btn btn-sm btn-primary" onClick={props.onAdd}>
        <i className="fa fa-plus" aria-hidden="true" /> {t('Dash.AddWidget')}
      </button>
      <a className="btn btn-sm btn-link" href={props.backUrl}>
        {t('Dash.BackToTemplates')}
      </a>
      <button type="button" className="btn btn-sm btn-success" onClick={props.onSave} disabled={props.saving}>
        <i className={`fa ${props.saving ? 'fa-spinner fa-spin' : 'fa-check'}`} aria-hidden="true" /> {t('Dash.SaveTemplate')}
      </button>
    </div>
  );
}

export function BoardTabs(props: {
  boards: BoardSummary[];
  currentIndex: number;
  editing: boolean;
  canAdd: boolean;
  busy: boolean;
  onSwitch: (index: number) => void;
  onAdd: () => void;
  onRename: () => void;
  onDelete: () => void;
}) {
  const current = props.boards[props.currentIndex];
  return (
    <nav className="rg-boards" aria-label={t('Dash.Boards')}>
      <div className="rg-boards-tabs">
        {props.boards.map((board, index) => (
          <button
            key={board.BoardId ?? `home-${index}`}
            type="button"
            aria-current={index === props.currentIndex ? 'true' : undefined}
            className={`rg-board-tab${index === props.currentIndex ? ' rg-on' : ''}`}
            disabled={props.busy}
            onClick={() => (index === props.currentIndex ? undefined : props.onSwitch(index))}
          >
            {board.IsHome ? <i className="fa fa-home" aria-hidden="true" /> : null} {board.Name || (board.IsHome ? t('BoardHome') : t('Dash.UntitledBoard'))}
          </button>
        ))}
      </div>
      {props.editing && current ? (
        <span className="rg-boards-actions">
          <button type="button" className="rg-iconbtn" aria-label={t('Dash.RenameBoard')} title={t('Dash.RenameBoard')} onClick={props.onRename} disabled={props.busy}>
            <i className="fa fa-pencil" aria-hidden="true" />
          </button>
          {!current.IsHome ? (
            <button type="button" className="rg-iconbtn" aria-label={t('Dash.DeleteBoard')} title={t('Dash.DeleteBoard')} onClick={props.onDelete} disabled={props.busy}>
              <i className="fa fa-trash-o" aria-hidden="true" />
            </button>
          ) : null}
        </span>
      ) : null}
      {props.canAdd ? (
        <button type="button" className="rg-board-add" onClick={props.onAdd} disabled={props.busy}>
          <i className="fa fa-plus" aria-hidden="true" /> {t('Dash.AddBoard')}
        </button>
      ) : null}
    </nav>
  );
}

export function ConflictBanner(props: { onKeepMine: () => void; onLoadLatest: () => void }) {
  return (
    <div className="rg-conflict" role="alert">
      <i className="fa fa-exclamation-triangle" aria-hidden="true" />
      <span className="rg-conflict-text">
        <b>{t('SaveErrorConflict')}</b> {t('Dash.ConflictText')}
      </span>
      <button type="button" className="btn btn-sm btn-default" onClick={props.onKeepMine}>
        {t('Dash.KeepMine')}
      </button>
      <button type="button" className="btn btn-sm btn-primary" onClick={props.onLoadLatest}>
        {t('Dash.LoadLatest')}
      </button>
    </div>
  );
}

export function FirstRunCoach(props: { onCustomize: () => void; onDismiss: () => void }) {
  return (
    <section className="rg-coach" aria-labelledby="rg-coach-title">
      <span className="rg-coach-icon" aria-hidden="true">
        <i className="fa fa-magic" />
      </span>
      <div className="rg-coach-main">
        <h2 id="rg-coach-title">{t('Dash.CoachTitle')}</h2>
        <p>{t('Dash.CoachText')}</p>
      </div>
      <div className="rg-coach-actions">
        <button type="button" className="btn btn-sm btn-primary" onClick={props.onCustomize}>
          <i className="fa fa-th-large" aria-hidden="true" /> {t('Dash.Customize')}
        </button>
        <button type="button" className="btn btn-sm btn-link" onClick={props.onDismiss}>
          {t('Dash.CoachDismiss')}
        </button>
      </div>
    </section>
  );
}
