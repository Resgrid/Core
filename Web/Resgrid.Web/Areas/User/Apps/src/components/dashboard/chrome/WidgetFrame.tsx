import {
  Component,
  Suspense,
  memo,
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type ErrorInfo,
  type KeyboardEvent,
  type ReactNode,
} from 'react';
import type { DraggableSyntheticListeners } from '@dnd-kit/core';
import { apiFetchJson } from '../../../runtime/api';
import { dashboardApi } from '../api';
import { isStale, liveTopics } from '../data/dashboardBus';
import { useWidgetData, type WidgetRequest } from '../data/useWidgetData';
import { allowedSizes, effectiveSize, sizeLabel, stepSize } from '../grid/sizes';
import { t } from '../i18n';
import { sameSettings, stableStringify } from '../layoutUtils';
import { PrimitiveRuntimeContext, useDashboardRuntime, type PrimitiveRuntime } from '../runtime';
import type { LayoutWidget, SizeToken, WidgetDefinition, WidgetError } from '../types';
import { widgetComponent } from '../widgets/registry';
import type { WidgetBootstrap, WidgetContext } from '../widgets/types';
import { AgeLabel, MenuButton, MenuDivider, MenuItem, MenuLabel, useMenuClose } from './overlays';

export interface SortableBinding {
  setNodeRef: (element: HTMLElement | null) => void;
  listeners: DraggableSyntheticListeners | undefined;
  isDragging: boolean;
}

export interface WidgetFrameProps {
  instance: LayoutWidget;
  definition: WidgetDefinition;
  position: number;
  total: number;
  dropSide: 'before' | 'after' | null;
  sortable: SortableBinding;
  keyboardHelpId: string;
}

export const DEFAULT_ACCENT = '#228BCB';

export function widgetTitle(definition: Pick<WidgetDefinition, 'Key' | 'Title'>): string {
  const fallbackKey = `W.${definition.Key}.Title`;
  return definition.Title && definition.Title !== fallbackKey ? definition.Title : t(fallbackKey);
}

function signatureOf(instance: LayoutWidget, size: string, quiet: Set<string>): string {
  const settings: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(instance.Settings ?? {})) {
    if (!quiet.has(key)) {
      settings[key] = value;
    }
  }
  return `${instance.Key}|${size}|${stableStringify(settings)}`;
}

export function Skeleton() {
  return (
    <div className="rg-skel" role="status" aria-label={t('Dash.Loading')}>
      <i />
      <i />
      <i />
      <i />
      <i />
    </div>
  );
}

class WidgetBoundary extends Component<{ resetKey: unknown; fallback: ReactNode; children: ReactNode }, { failed: boolean; key: unknown }> {
  public state = { failed: false, key: this.props.resetKey };

  public static getDerivedStateFromError(): Partial<{ failed: boolean }> {
    return { failed: true };
  }

  public static getDerivedStateFromProps(props: { resetKey: unknown }, state: { failed: boolean; key: unknown }) {
    return props.resetKey !== state.key ? { failed: false, key: props.resetKey } : null;
  }

  public componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('Dashboard: a widget failed to render.', error, info.componentStack);
  }

  public render() {
    return this.state.failed ? this.props.fallback : this.props.children;
  }
}

function ErrorBody(props: { title: string; error: WidgetError; onRetry: () => void }) {
  const message = t(props.error.MessageKey || 'WidgetErrorFailed');
  return (
    <div className="rg-w-error" title={props.error.Code}>
      <i className="fa fa-exclamation-circle" aria-hidden="true" />
      <b>{t('Dash.CouldNotLoad', props.title)}</b>
      <span>{message}</span>
      <button type="button" className="rg-btn-xs" onClick={props.onRetry}>
        {t('Dash.Retry')}
      </button>
    </div>
  );
}

function MenuSizeChips(props: { sizes: SizeToken[]; size: SizeToken; onPick: (size: SizeToken) => void }) {
  const close = useMenuClose();
  return (
    <div className="rg-menu-sizes" role="group" aria-label={t('Dash.Size')}>
      {props.sizes.map((option) => (
        <button
          key={option}
          type="button"
          className={`rg-sizechip${option === props.size ? ' rg-on' : ''}`}
          aria-pressed={option === props.size}
          aria-label={t('Dash.SizeAria', sizeLabel(option))}
          onClick={() => {
            close();
            props.onPick(option);
          }}
        >
          {sizeLabel(option)}
        </button>
      ))}
    </div>
  );
}

function ProtectedBody(props: { canReveal: boolean; wall: boolean; onReveal: () => void }) {
  return (
    <div className="rg-w-protected">
      <i className="fa fa-shield" aria-hidden="true" />
      <b>{t('WidgetErrorProtected')}</b>
      {props.canReveal ? (
        <button type="button" className="rg-btn-xs rg-btn-primary" onClick={props.onReveal}>
          <i className="fa fa-shield" aria-hidden="true" /> {t('Dash.VerifyToView')}
        </button>
      ) : (
        <span>{props.wall ? t('Dash.AdpWallHidden') : t('Dash.AdpRoleCannotView')}</span>
      )}
    </div>
  );
}

function NotAvailableBody({ icon }: { icon: string }) {
  return (
    <div className="rg-empty">
      <i className={`fa ${icon || 'fa-th-large'}`} aria-hidden="true" />
      <span>{t('Dash.WidgetNotAvailable')}</span>
    </div>
  );
}

export const WidgetFrame = memo(function WidgetFrame(props: WidgetFrameProps) {
  const { instance, definition, sortable } = props;
  const runtime = useDashboardRuntime();
  const { actions, editing } = runtime;
  const elementRef = useRef<HTMLElement | null>(null);
  const bodyRef = useRef<HTMLDivElement | null>(null);
  const quietKeys = useRef(new Set<string>());
  const [badge, setBadge] = useState<number | string | null>(null);

  const size = effectiveSize(instance.Size, definition);
  const WidgetComponent = widgetComponent(instance.Key);
  const category = runtime.categories.get(definition.Category);
  const accent = category?.Color || DEFAULT_ACCENT;
  const title = widgetTitle(definition);
  const live = runtime.mode === 'board' && liveTopics(definition.RealtimeTopics).length > 0;
  const stale = live && isStale(runtime.busStatus);

  // Stored instances read WidgetData (settings from the stored layout); anything unsaved previews its local settings.
  const requestRef = useRef<() => WidgetRequest>(() => ({ kind: 'preview', key: instance.Key, size, settings: instance.Settings }));
  requestRef.current = () => {
    const stored = runtime.stored.get(instance.Id);
    const isStored =
      runtime.mode === 'board' &&
      !!stored &&
      stored.Key === instance.Key &&
      effectiveSize(stored.Size, definition) === size &&
      sameSettings(stored.Settings, instance.Settings);
    return isStored
      ? { kind: 'stored', instanceId: instance.Id, boardId: runtime.boardId }
      : { kind: 'preview', key: instance.Key, size, settings: instance.Settings ?? {} };
  };
  const getRequest = useCallback(() => requestRef.current(), []);

  const signature = signatureOf(instance, size, quietKeys.current);
  const data = useWidgetData({
    definition,
    enabled: !!WidgetComponent,
    signature,
    getRequest,
    revealed: runtime.revealed,
    bus: runtime.mode === 'board' ? runtime.bus : null,
    busStatus: runtime.busStatus,
    elementRef,
  });

  const refreshRef = useRef(data.refresh);
  refreshRef.current = data.refresh;
  const refresh = useCallback(() => {
    if (!refreshRef.current()) {
      actions.toast(t('Dash.RevealedRefreshDeferred'));
    }
  }, [actions]);

  const widgetBootstrap = useMemo<WidgetBootstrap>(
    () => ({
      departmentId: runtime.bootstrap.DepartmentId,
      map: runtime.bootstrap.Map,
      options: runtime.bootstrap.Options ?? {},
      header: runtime.bootstrap.Header,
      categories: runtime.bootstrap.Categories ?? [],
    }),
    [runtime.bootstrap],
  );

  const grant = runtime.revealed && definition.HasProtectedFields;
  const ctx = useMemo<WidgetContext>(
    () => ({
      instanceId: instance.Id,
      mode: runtime.mode,
      editing,
      density: runtime.density,
      wall: runtime.wall,
      rtl: runtime.rtl,
      refresh,
      openSettings: () => {
        if (definition.Settings.length > 0) {
          actions.openSettings(instance.Id);
        }
      },
      updateSettings: (patch, options) => {
        if (options?.refetch === false) {
          Object.keys(patch).forEach((key) => quietKeys.current.add(key));
        }
        actions.updateSettings(instance.Id, patch);
      },
      post: <TResponse,>(url: string, body: unknown) => dashboardApi.postJson<TResponse>(url, body, grant),
      submit: (url, options) => dashboardApi.submit(url, options),
      api: apiFetchJson,
      setBadge,
      toast: (text: string) => actions.toast(text),
      announce: (text: string) => actions.announce(text),
      adp: runtime.adp,
      bootstrap: widgetBootstrap,
      format: runtime.format,
    }),
    [instance.Id, runtime.mode, editing, runtime.density, runtime.wall, runtime.rtl, refresh, definition.Settings.length, actions, grant, runtime.adp, widgetBootstrap, runtime.format],
  );

  const primitiveRuntime = useMemo<PrimitiveRuntime>(
    () => ({ adp: runtime.adp, map: runtime.bootstrap.Map, t, format: runtime.format, rtl: runtime.rtl }),
    [runtime.adp, runtime.bootstrap.Map, runtime.format, runtime.rtl],
  );

  // Keyboard reorder moves this DOM node, which drops focus; take it back.
  useLayoutEffect(() => {
    if (runtime.pendingFocus.current === instance.Id && elementRef.current) {
      runtime.pendingFocus.current = null;
      elementRef.current.focus();
    }
  });

  // Widget content is inert while customizing (the overlay blocks the pointer; this blocks the keyboard).
  useEffect(() => {
    bodyRef.current?.toggleAttribute('inert', editing);
  }, [editing]);

  const setRefs = useCallback(
    (element: HTMLElement | null) => {
      elementRef.current = element;
      sortable.setNodeRef(element);
    },
    [sortable],
  );

  const onKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (!editing || event.target !== event.currentTarget) {
      return;
    }
    const rtl = runtime.rtl;
    switch (event.key) {
      case 'ArrowLeft':
        event.preventDefault();
        actions.move(instance.Id, rtl ? 1 : -1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        actions.move(instance.Id, -1);
        break;
      case 'ArrowRight':
        event.preventDefault();
        actions.move(instance.Id, rtl ? -1 : 1);
        break;
      case 'ArrowDown':
        event.preventDefault();
        actions.move(instance.Id, 1);
        break;
      case '+':
      case '=': {
        event.preventDefault();
        const next = stepSize(size, definition, 1);
        if (next) actions.setSize(instance.Id, next);
        break;
      }
      case '-':
      case '_': {
        event.preventDefault();
        const next = stepSize(size, definition, -1);
        if (next) actions.setSize(instance.Id, next);
        break;
      }
      case 'Delete':
      case 'Backspace':
        event.preventDefault();
        actions.remove(instance.Id);
        break;
      default:
        break;
    }
  };

  const sizes = allowedSizes(definition);
  const sizeChips = sizes.length > 1
    ? sizes.map((option) => (
        <button
          key={option}
          type="button"
          className={`rg-sizechip${option === size ? ' rg-on' : ''}`}
          aria-pressed={option === size}
          aria-label={t('Dash.SizeAria', sizeLabel(option))}
          onClick={() => actions.setSize(instance.Id, option)}
        >
          {sizeLabel(option)}
        </button>
      ))
    : null;

  let body: ReactNode;
  if (!WidgetComponent) {
    body = <NotAvailableBody icon={definition.Icon} />;
  } else if (data.status === 'error' && data.error) {
    body =
      data.error.Code === 'protected' ? (
        <ProtectedBody canReveal={runtime.adp.canReveal} wall={runtime.wall} onReveal={runtime.adp.requestReveal} />
      ) : (
        <ErrorBody title={title} error={data.error} onRetry={refresh} />
      );
  } else if (data.status !== 'ready') {
    body = <Skeleton />;
  } else if (data.data === null || data.data === undefined) {
    body = (
      <div className="rg-empty">
        <i className={`fa ${definition.Icon || 'fa-inbox'}`} aria-hidden="true" />
        <span>{t('Dash.EmptyDefault')}</span>
      </div>
    );
  } else {
    body = (
      <WidgetBoundary resetKey={data.fetchedAt} fallback={<ErrorBody title={title} error={{ Code: 'render', MessageKey: 'WidgetErrorFailed' }} onRetry={refresh} />}>
        <Suspense fallback={<Skeleton />}>
          <PrimitiveRuntimeContext.Provider value={primitiveRuntime}>
            <WidgetComponent data={data.data} instance={instance} definition={definition} size={size} t={t} ctx={ctx} />
          </PrimitiveRuntimeContext.Provider>
        </Suspense>
      </WidgetBoundary>
    );
  }

  const dtoBadge =
    data.status === 'ready' && data.data && typeof data.data === 'object' && 'Badge' in (data.data as Record<string, unknown>)
      ? ((data.data as Record<string, unknown>).Badge as number | string | null)
      : null;
  const shownBadge = data.status === 'ready' ? badge ?? dtoBadge : null;
  const revealedBadge = runtime.revealed && definition.HasProtectedFields && definition.CanRevealProtected !== false && data.status === 'ready';

  const classes = ['rg-w'];
  if (data.refreshing) classes.push('rg-refreshing');
  if (stale || data.refreshFailed) classes.push('rg-stale');
  if (sortable.isDragging) classes.push('rg-dragging');
  if (props.dropSide === 'before') classes.push('rg-drop-before');
  if (props.dropSide === 'after') classes.push('rg-drop-after');
  if (runtime.flashId === instance.Id) classes.push('rg-flash');

  const style = { '--accent': accent } as CSSProperties;

  return (
    <section
      ref={setRefs}
      className={classes.join(' ')}
      data-size={size}
      data-key={instance.Key}
      data-id={instance.Id}
      style={style}
      aria-label={title}
      tabIndex={editing ? 0 : undefined}
      aria-roledescription={editing ? t('Dash.MovableWidget') : undefined}
      aria-describedby={editing ? props.keyboardHelpId : undefined}
      onKeyDown={onKeyDown}
      {...(editing ? sortable.listeners : undefined)}
    >
      <header className={editing ? 'rg-w-head rg-w-head-edit' : 'rg-w-head'}>
        <span className="rg-w-icon" aria-hidden="true">
          <i className={`fa ${definition.Icon || 'fa-th-large'}`} />
        </span>
        <h2 className="rg-w-title" title={title}>
          {title}
        </h2>
        {shownBadge !== null && shownBadge !== undefined && shownBadge !== '' ? (
          <span className="rg-w-badge" aria-label={t('Dash.BadgeAria', shownBadge)}>
            {shownBadge}
          </span>
        ) : null}
        {revealedBadge ? (
          <span className="rg-w-adp" title={t('Dash.RevealedBadgeTooltip')}>
            <i className="fa fa-eye" aria-hidden="true" /> <span className="rg-w-adp-text">{t('Dash.RevealedBadge')}</span>
          </span>
        ) : null}
        {!editing && WidgetComponent ? <AgeLabel fetchedAt={data.fetchedAt} live={live} stale={stale} failed={data.refreshFailed} /> : null}
        {!editing ? (
          <MenuButton label={t('Dash.WidgetOptions', title)} className="rg-iconbtn rg-w-menu-btn" content={<i className="fa fa-ellipsis-v" aria-hidden="true" />}>
            <MenuItem icon="fa-refresh" onSelect={refresh} disabled={!WidgetComponent}>
              {t('Dash.Refresh')}
            </MenuItem>
            {definition.Settings.length > 0 ? (
              <MenuItem icon="fa-sliders" onSelect={() => actions.openSettings(instance.Id)}>
                {t('Dash.Settings')}
              </MenuItem>
            ) : null}
            {definition.ModuleUrl ? (
              <MenuItem icon="fa-external-link" href={definition.ModuleUrl}>
                {t('Dash.OpenModule')}
              </MenuItem>
            ) : null}
            {sizeChips ? (
              <>
                <MenuLabel>{t('Dash.Size')}</MenuLabel>
                <MenuSizeChips sizes={sizes} size={size} onPick={(option) => actions.setSize(instance.Id, option)} />
              </>
            ) : null}
            <MenuDivider />
            <MenuItem icon="fa-trash-o" danger onSelect={() => actions.remove(instance.Id)}>
              {t('Dash.RemoveFromDashboard')}
            </MenuItem>
          </MenuButton>
        ) : (
          <span className="rg-w-edit">
            {sizeChips}
            {definition.Settings.length > 0 ? (
              <button type="button" className="rg-iconbtn" aria-label={t('Dash.SettingsFor', title)} title={t('Dash.Settings')} onClick={() => actions.openSettings(instance.Id)}>
                <i className="fa fa-sliders" aria-hidden="true" />
              </button>
            ) : null}
            <button type="button" className="rg-iconbtn" aria-label={t('Dash.RemoveWidget', title)} title={t('Dash.Remove')} onClick={() => actions.remove(instance.Id)}>
              <i className="fa fa-times" aria-hidden="true" />
            </button>
          </span>
        )}
      </header>
      <div className="rg-w-progress" aria-hidden="true" />
      <div className="rg-w-body" ref={bodyRef} aria-busy={data.status === 'loading' || data.refreshing ? true : undefined}>
        {body}
      </div>
    </section>
  );
});

/** The ghost card that follows the pointer while dragging (the frame itself stays in place, dimmed). */
export function DragGhost({ definition, accent, size }: { definition: WidgetDefinition; accent: string; size: string }) {
  const style = { '--accent': accent } as CSSProperties;
  return (
    <div className="rg-w rg-ghost" data-size={size} style={style}>
      <header className="rg-w-head">
        <span className="rg-w-icon" aria-hidden="true">
          <i className={`fa ${definition.Icon || 'fa-th-large'}`} />
        </span>
        <h2 className="rg-w-title">{widgetTitle(definition)}</h2>
      </header>
    </div>
  );
}
