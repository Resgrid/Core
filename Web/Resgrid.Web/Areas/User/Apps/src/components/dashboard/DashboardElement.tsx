import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { dashboardApi } from './api';
import { BoardTabs, ConflictBanner, EditBar, FirstRunCoach, TemplateEditBar } from './chrome/Bars';
import { AdpChip, HeaderBand, PulseStrip } from './chrome/HeaderBand';
import { ConfirmDialog, PromptDialog, ToastView, type ToastState } from './chrome/overlays';
import { draftFromTemplate, NAME_MAX, TemplatePanel, validateDraft, type TemplateDraft } from './chrome/TemplatePanel';
import { WidgetGallery } from './chrome/WidgetGallery';
import { Skeleton, widgetTitle } from './chrome/WidgetFrame';
import { WidgetSettingsDialog } from './chrome/WidgetSettingsDialog';
import { DashboardBus, type BusStatus } from './data/dashboardBus';
import { DashboardGrid, type GridItem } from './grid/DashboardGrid';
import { effectiveSize, sizeLabel } from './grid/sizes';
import { createFormat, has, setCulture, setStrings, t, uiLocale } from './i18n';
import { densityOf, layoutsEqual, moveWidget, newInstanceId, normalizeLayout, stepWidget } from './layoutUtils';
import { DashboardRuntimeContext, type DashboardRuntime, type FrameActions, type ToastAction } from './runtime';
import { adpWindow, type BoardResult, type BoardSummary, type Bootstrap, type DashboardLayout, type DashboardMode, type Density, type LayoutWidget, type WidgetDefinition } from './types';
import type { WidgetAdpState } from './widgets/types';
import './dashboard.css';

// <rg-dashboard> (home-dashboard-redesign-plan.md §4.3): loads the bootstrap, owns the layout, edit mode, boards and
// the save queue — debounced 800 ms, optimistic, RowVersion conflicts shown as a banner (plan §5.4) — and renders the
// header band, pulse strip, grid and overlays. In mode="template" the same element edits a department template.

export interface DashboardElementProps {
  mode?: string;
  templateId?: string;
  firstRun?: boolean;
  adpReveal?: boolean;
  strings?: Record<string, string> | null;
  /** UI culture for Intl (CultureInfo.CurrentUICulture.Name); empty falls back to the browser's language. */
  culture?: string;
  hostElement?: HTMLElement;
}

type Timer = ReturnType<typeof setTimeout>;

const SAVE_DEBOUNCE_MS = 800;
const BOOTSTRAP_STALE_MS = 60000;
const FLASH_MS = 1700;
const COACH_STORAGE_KEY = 'rg-dashboard-coach-dismissed';
const CLASSIC_FALLBACK_URL = '/User/Home/Dashboard?classic=true';
const TEMPLATES_FALLBACK_URL = '/User/Dashboard/Templates';

const emptyLayout: DashboardLayout = { SchemaVersion: 1, Density: 'comfortable', Widgets: [] };

type DialogState = { kind: 'addBoard' } | { kind: 'renameBoard' } | { kind: 'deleteBoard' } | null;

function coachDismissed(): boolean {
  try {
    return window.localStorage.getItem(COACH_STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

function rememberCoachDismissed(): void {
  try {
    window.localStorage.setItem(COACH_STORAGE_KEY, '1');
  } catch {
    // Storage unavailable: the coach mark returns next visit, which is harmless.
  }
}

function readDirection(element: HTMLElement | undefined): boolean {
  try {
    return getComputedStyle(element ?? document.documentElement).direction === 'rtl';
  } catch {
    return document.documentElement.dir === 'rtl';
  }
}

function saveErrorText(error: string | null | undefined): string {
  return error && has(`SaveError${error}`) ? t(`SaveError${error}`) : t('Dash.SaveFailed');
}

function prefersReducedMotion(): boolean {
  try {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch {
    return false;
  }
}

function boardIndexFor(boards: BoardSummary[], boardId: string | null): number {
  const index = boardId ? boards.findIndex((board) => board.BoardId === boardId) : boards.findIndex((board) => board.IsHome);
  return index >= 0 ? index : 0;
}

export default function DashboardElement(props: DashboardElementProps) {
  setStrings(props.strings);
  setCulture(props.culture);
  const mode: DashboardMode = props.mode === 'template' ? 'template' : 'board';

  // ---------- State ----------
  const [boot, setBoot] = useState<Bootstrap | null>(null);
  const [bootError, setBootError] = useState(false);
  const [busy, setBusy] = useState(false);
  const [layout, setLayoutState] = useState<DashboardLayout>(emptyLayout);
  const layoutRef = useRef(layout);
  const [stored, setStoredState] = useState<DashboardLayout | null>(null);
  const storedRef = useRef<DashboardLayout | null>(null);
  const boardRef = useRef<{ boardId: string | null; rowVersion: number }>({ boardId: null, rowVersion: 0 });
  const [boardId, setBoardId] = useState<string | null>(null);
  const [boards, setBoards] = useState<BoardSummary[]>([]);
  const [currentIndex, setCurrentIndexState] = useState(0);
  const currentIndexRef = useRef(0);
  const [boardKey, setBoardKey] = useState(0);
  const [editing, setEditingState] = useState(mode === 'template');
  const editingRef = useRef(mode === 'template');
  const snapshotRef = useRef<DashboardLayout | null>(null);
  const [conflict, setConflictState] = useState<BoardResult | null>(null);
  const conflictRef = useRef<BoardResult | null>(null);
  const [saving, setSaving] = useState(false);
  const dirtyRef = useRef(false);
  const saveTimer = useRef<Timer | null>(null);
  const saveChain = useRef<Promise<unknown>>(Promise.resolve());
  const lastBootAt = useRef(0);
  const templateIdRef = useRef<string | null>(props.templateId || null);
  const templateDirtyRef = useRef(false);

  const [galleryOpen, setGalleryOpen] = useState(false);
  const [settingsFor, setSettingsFor] = useState<string | null>(null);
  const [dialog, setDialog] = useState<DialogState>(null);
  const [toast, setToast] = useState<ToastState | null>(null);
  const toastTimer = useRef<Timer | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const [flashId, setFlashId] = useState<string | null>(null);
  const [wall, setWall] = useState(false);
  const [revealed, setRevealed] = useState(false);
  const revealedRef = useRef(false);
  const [busStatus, setBusStatus] = useState<BusStatus>('idle');
  const [coach, setCoach] = useState(() => mode === 'board' && !!props.firstRun && !coachDismissed());
  const [rtl, setRtl] = useState(() => readDirection(props.hostElement));
  const pendingFocus = useRef<string | null>(null);
  const [templateDraft, setTemplateDraftState] = useState<TemplateDraft>(() => draftFromTemplate(null));
  const [templateError, setTemplateError] = useState<string | null>(null);
  const [templateIsNew, setTemplateIsNew] = useState(!props.templateId);

  const bus = useMemo(() => (mode === 'board' ? new DashboardBus() : null), [mode]);

  const setLayout = useCallback((next: DashboardLayout) => {
    layoutRef.current = next;
    setLayoutState(next);
  }, []);

  const setStored = useCallback((next: DashboardLayout | null) => {
    storedRef.current = next;
    setStoredState(next);
  }, []);

  const setEditing = useCallback((value: boolean) => {
    editingRef.current = value;
    setEditingState(value);
  }, []);

  const setConflict = useCallback((value: BoardResult | null) => {
    conflictRef.current = value;
    setConflictState(value);
  }, []);

  const setCurrentIndex = useCallback((value: number) => {
    currentIndexRef.current = value;
    setCurrentIndexState(value);
  }, []);

  const showToast = useCallback((text: string, action?: ToastAction) => {
    if (toastTimer.current) {
      clearTimeout(toastTimer.current);
    }
    const id = Date.now() + Math.random();
    setToast({ id, text, action });
    toastTimer.current = setTimeout(() => setToast((current) => (current && current.id === id ? null : current)), action ? 6000 : 3500);
  }, []);

  const announce = useCallback((text: string) => {
    setAnnouncement('');
    setTimeout(() => setAnnouncement(text), 30);
  }, []);

  // ---------- Bootstrap ----------
  const applyBoard = useCallback(
    (board: BoardResult) => {
      const normalized = normalizeLayout(board.Layout);
      boardRef.current = { boardId: board.BoardId ?? null, rowVersion: board.RowVersion ?? 0 };
      setBoardId(board.BoardId ?? null);
      setLayout(normalized);
      setStored(normalized);
      dirtyRef.current = false;
    },
    [setLayout, setStored],
  );

  const loadBootstrap = useCallback(
    async (board: string | null, reason: 'initial' | 'switch' | 'refresh') => {
      const result = await dashboardApi.bootstrap({ board, templateId: mode === 'template' ? templateIdRef.current : null });
      lastBootAt.current = Date.now();
      setBoot(result);
      setBootError(false);

      const server = result.Board ?? { BoardId: null, Name: null, RowVersion: 0, Source: 'default', Layout: emptyLayout };
      if (mode === 'board') {
        const list = result.Boards ?? [];
        setBoards(list);
        setCurrentIndex(boardIndexFor(list, server.BoardId ?? null));
      }

      if (reason === 'refresh') {
        // Cross-browser sync (plan §5.4): adopt silently, unless local edits are waiting — then ask.
        const changed =
          (server.RowVersion ?? 0) !== boardRef.current.rowVersion ||
          (server.BoardId ?? null) !== boardRef.current.boardId ||
          !layoutsEqual(server.Layout, storedRef.current);
        if (changed) {
          if (editingRef.current || dirtyRef.current) {
            setConflict(server);
          } else {
            applyBoard(server);
          }
        }
        return;
      }

      applyBoard(server);
      setBoardKey((value) => value + 1);
      if (mode === 'template' && reason === 'initial') {
        setTemplateDraftState(draftFromTemplate(result.Template));
        setTemplateIsNew(!result.Template?.TemplateId);
      }
    },
    [mode, applyBoard, setConflict, setCurrentIndex],
  );

  const loadInitial = useCallback(() => {
    setBootError(false);
    loadBootstrap(null, 'initial').catch((error: unknown) => {
      console.error('Dashboard: the bootstrap failed to load.', error);
      setBootError(true);
    });
  }, [loadBootstrap]);

  useEffect(() => {
    loadInitial();
  }, [loadInitial]);

  // ---------- Save queue ----------
  const flushSave = useCallback(
    (name?: string | null): Promise<boolean> => {
      if (mode !== 'board') {
        return Promise.resolve(false);
      }
      if (saveTimer.current) {
        clearTimeout(saveTimer.current);
        saveTimer.current = null;
      }

      const run = async (): Promise<boolean> => {
        if (conflictRef.current) {
          return false;
        }
        if (!dirtyRef.current && !name) {
          return true;
        }

        const sent = layoutRef.current;
        const sentIndex = currentIndexRef.current;
        dirtyRef.current = false;
        setSaving(true);
        try {
          const response = await dashboardApi.saveLayout({
            BoardId: boardRef.current.boardId,
            RowVersion: boardRef.current.rowVersion,
            Name: name ?? null,
            Layout: sent,
          });

          if (response.Error || !response.Board) {
            dirtyRef.current = true;
            if (response.Error === 'Conflict' && response.Current) {
              setConflict(response.Current);
            } else {
              showToast(saveErrorText(response.Error));
            }
            return false;
          }

          const board = response.Board;
          boardRef.current = { boardId: board.BoardId ?? null, rowVersion: board.RowVersion ?? 0 };
          setBoardId(board.BoardId ?? null);
          const serverLayout = normalizeLayout(board.Layout ?? sent);
          setStored(serverLayout);
          setBoards((list) => list.map((item, index) => (index === sentIndex ? { ...item, BoardId: board.BoardId ?? null, Name: name || item.Name } : item)));

          // Adopt the server's normalized form when nothing changed while the save was in flight.
          if (!dirtyRef.current && !editingRef.current && layoutRef.current === sent && !layoutsEqual(serverLayout, sent)) {
            setLayout(serverLayout);
          }
          return true;
        } catch (error) {
          console.error('Dashboard: saving the layout failed.', error);
          dirtyRef.current = true;
          showToast(t('Dash.SaveFailed'));
          return false;
        } finally {
          setSaving(false);
        }
      };

      const next = saveChain.current.then(run, run);
      saveChain.current = next.catch(() => undefined);
      return next;
    },
    [mode, setConflict, setLayout, setStored, showToast],
  );

  const scheduleSave = useCallback(() => {
    if (mode !== 'board') {
      templateDirtyRef.current = true;
      return;
    }
    dirtyRef.current = true;
    if (editingRef.current) {
      return; // saved on Done
    }
    if (saveTimer.current) {
      clearTimeout(saveTimer.current);
    }
    saveTimer.current = setTimeout(() => {
      saveTimer.current = null;
      void flushSave();
    }, SAVE_DEBOUNCE_MS);
  }, [mode, flushSave]);

  const mutate = useCallback(
    (change: (current: DashboardLayout) => DashboardLayout) => {
      const next = change(layoutRef.current);
      if (next === layoutRef.current) {
        return;
      }
      setLayout(next);
      scheduleSave();
    },
    [setLayout, scheduleSave],
  );

  // Re-read the board when the tab comes back after a while (another browser may have changed it), and push a
  // pending save out before the page is hidden.
  useEffect(() => {
    if (mode !== 'board') {
      return undefined;
    }
    const onVisibility = () => {
      if (document.visibilityState === 'hidden') {
        if (saveTimer.current) {
          void flushSave();
        }
        return;
      }
      if (Date.now() - lastBootAt.current > BOOTSTRAP_STALE_MS && !conflictRef.current) {
        loadBootstrap(boardRef.current.boardId, 'refresh').catch(() => undefined);
      }
    };
    document.addEventListener('visibilitychange', onVisibility);
    return () => document.removeEventListener('visibilitychange', onVisibility);
  }, [mode, flushSave, loadBootstrap]);

  useEffect(() => {
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      const unsaved =
        mode === 'template'
          ? templateDirtyRef.current
          : (editingRef.current && snapshotRef.current && !layoutsEqual(layoutRef.current, snapshotRef.current)) || !!conflictRef.current;
      if (unsaved) {
        event.preventDefault();
        event.returnValue = '';
      }
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [mode]);

  // ---------- Definitions and visible items ----------
  const definitions = useMemo(() => new Map((boot?.Widgets ?? []).map((definition) => [definition.Key, definition])), [boot?.Widgets]);
  const categories = useMemo(() => new Map((boot?.Categories ?? []).map((category) => [category.Key, category])), [boot?.Categories]);
  const storedMap = useMemo(() => new Map((stored?.Widgets ?? []).map((widget) => [widget.Id, widget])), [stored]);
  const locale = uiLocale();
  const format = useMemo(() => createFormat(boot?.Header?.TimeZone), [boot?.Header?.TimeZone, locale]);
  const density: Density = densityOf(layout);
  const maxWidgets = boot?.MaxWidgets || 40;

  // Instances whose key the viewer cannot see (or that a later release removed) stay in the layout, unrendered.
  const items = useMemo<GridItem[]>(
    () =>
      layout.Widgets.flatMap((instance) => {
        const definition = definitions.get(instance.Key);
        return definition ? [{ instance, definition }] : [];
      }),
    [layout.Widgets, definitions],
  );

  // ---------- Edit mode ----------
  const startEditing = useCallback(async () => {
    if (editingRef.current || mode !== 'board') {
      return;
    }
    if (saveTimer.current) {
      await flushSave();
    }
    snapshotRef.current = layoutRef.current;
    setEditing(true);
    setCoach(false);
    announce(t('Dash.EditModeAnnounce'));
  }, [mode, flushSave, setEditing, announce]);

  const finishEditing = useCallback(
    async (save: boolean) => {
      if (!editingRef.current || mode !== 'board') {
        return;
      }
      const snapshot = snapshotRef.current;
      snapshotRef.current = null;
      setGalleryOpen(false);

      if (!save) {
        if (snapshot) {
          setLayout(snapshot);
        }
        dirtyRef.current = false;
        setEditing(false);
        showToast(t('Dash.ChangesDiscarded'));
        return;
      }

      const changed = !snapshot || !layoutsEqual(layoutRef.current, snapshot) || dirtyRef.current;
      setEditing(false);
      if (!changed) {
        return;
      }
      dirtyRef.current = true;
      if (await flushSave()) {
        showToast(t('Dash.SavedToast'));
      }
    },
    [mode, flushSave, setEditing, setLayout, showToast],
  );

  // ---------- Frame actions ----------
  const isRendered = useCallback((widget: LayoutWidget) => definitions.has(widget.Key), [definitions]);

  const titleOf = useCallback(
    (id: string) => {
      const widget = layoutRef.current.Widgets.find((item) => item.Id === id);
      const definition = widget ? definitions.get(widget.Key) : undefined;
      return definition ? widgetTitle(definition) : '';
    },
    [definitions],
  );

  const actionsImpl: FrameActions = {
    remove: (id) => {
      const current = layoutRef.current.Widgets;
      const index = current.findIndex((widget) => widget.Id === id);
      if (index < 0) {
        return;
      }
      const removed = current[index];
      const rendered = current.filter(isRendered);
      const renderedIndex = rendered.findIndex((widget) => widget.Id === id);
      const neighbour = rendered[renderedIndex + 1] ?? rendered[renderedIndex - 1];
      if (editingRef.current && neighbour) {
        pendingFocus.current = neighbour.Id;
      }
      const title = titleOf(id);
      mutate((layoutNow) => ({ ...layoutNow, Widgets: layoutNow.Widgets.filter((widget) => widget.Id !== id) }));
      announce(t('Dash.WidgetRemoved', title));
      showToast(t('Dash.WidgetRemoved', title), {
        label: t('Dash.Undo'),
        run: () =>
          mutate((layoutNow) => {
            if (layoutNow.Widgets.some((widget) => widget.Id === removed.Id)) {
              return layoutNow;
            }
            const widgets = [...layoutNow.Widgets];
            widgets.splice(Math.min(index, widgets.length), 0, removed);
            return { ...layoutNow, Widgets: widgets };
          }),
      });
    },
    setSize: (id, size) => {
      mutate((layoutNow) => ({ ...layoutNow, Widgets: layoutNow.Widgets.map((widget) => (widget.Id === id ? { ...widget, Size: size } : widget)) }));
      announce(t('Dash.SizeChanged', titleOf(id), sizeLabel(size)));
    },
    move: (id, delta) => {
      const before = layoutRef.current.Widgets;
      const after = stepWidget(before, id, delta, isRendered);
      if (after === before) {
        return;
      }
      pendingFocus.current = id;
      mutate((layoutNow) => ({ ...layoutNow, Widgets: after }));
      const position = after.filter(isRendered).findIndex((widget) => widget.Id === id) + 1;
      announce(t('Dash.WidgetMoved', titleOf(id), position));
    },
    openSettings: (id) => setSettingsFor(id),
    updateSettings: (id, patch) =>
      mutate((layoutNow) => ({
        ...layoutNow,
        Widgets: layoutNow.Widgets.map((widget) => (widget.Id === id ? { ...widget, Settings: { ...(widget.Settings ?? {}), ...patch } } : widget)),
      })),
    toast: (text, action) => showToast(text, action),
    announce: (text) => announce(text),
  };
  const actionsRef = useRef(actionsImpl);
  actionsRef.current = actionsImpl;
  const actions = useMemo<FrameActions>(
    () => ({
      remove: (id) => actionsRef.current.remove(id),
      setSize: (id, size) => actionsRef.current.setSize(id, size),
      move: (id, delta) => actionsRef.current.move(id, delta),
      openSettings: (id) => actionsRef.current.openSettings(id),
      updateSettings: (id, patch) => actionsRef.current.updateSettings(id, patch),
      toast: (text, action) => actionsRef.current.toast(text, action),
      announce: (text) => actionsRef.current.announce(text),
    }),
    [],
  );

  const onDrop = useCallback(
    (id: string, targetId: string, before: boolean) => {
      mutate((layoutNow) => ({ ...layoutNow, Widgets: moveWidget(layoutNow.Widgets, id, targetId, before) }));
      const position = layoutRef.current.Widgets.filter(isRendered).findIndex((widget) => widget.Id === id) + 1;
      announce(t('Dash.WidgetMoved', titleOf(id), position));
    },
    [mutate, isRendered, titleOf, announce],
  );

  const addWidget = useCallback(
    (definition: WidgetDefinition) => {
      if (layoutRef.current.Widgets.length >= maxWidgets) {
        showToast(t('Dash.MaxWidgets', maxWidgets));
        return;
      }
      const instance: LayoutWidget = {
        Id: newInstanceId(),
        Key: definition.Key,
        Size: effectiveSize(definition.DefaultSize, definition),
        Settings: {},
        Collapsed: false,
      };
      mutate((layoutNow) => ({ ...layoutNow, Widgets: [...layoutNow.Widgets, instance] }));
      setFlashId(instance.Id);
      setTimeout(() => setFlashId((current) => (current === instance.Id ? null : current)), FLASH_MS);
      showToast(t('Dash.WidgetAdded', widgetTitle(definition)));
      setTimeout(() => {
        document.querySelector(`.rg-w[data-id="${instance.Id}"]`)?.scrollIntoView({ block: 'nearest', behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
      }, 60);
    },
    [maxWidgets, mutate, showToast],
  );

  // ---------- Boards ----------
  const prepareLeave = useCallback(async () => {
    if (editingRef.current) {
      await finishEditing(true);
    } else if (saveTimer.current || dirtyRef.current) {
      await flushSave();
    }
  }, [finishEditing, flushSave]);

  const switchBoard = useCallback(
    async (index: number) => {
      const target = boards[index];
      if (!target) {
        return;
      }
      await prepareLeave();
      setBusy(true);
      try {
        await loadBootstrap(target.BoardId, 'switch');
      } catch {
        showToast(t('Dash.BoardLoadFailed'));
      } finally {
        setBusy(false);
      }
    },
    [boards, prepareLeave, loadBootstrap, showToast],
  );

  const addBoard = useCallback(
    async (name: string) => {
      setDialog(null);
      await prepareLeave();
      setBusy(true);
      try {
        const response = await dashboardApi.addBoard(name);
        if (response.Error || !response.Board) {
          showToast(saveErrorText(response.Error));
          return;
        }
        await loadBootstrap(response.Board.BoardId, 'switch');
        showToast(t('Dash.BoardAdded', name));
        snapshotRef.current = layoutRef.current;
        setEditing(true);
      } catch {
        showToast(t('Dash.SaveFailed'));
      } finally {
        setBusy(false);
      }
    },
    [prepareLeave, loadBootstrap, showToast, setEditing],
  );

  const renameBoard = useCallback(
    async (name: string) => {
      setDialog(null);
      if (await flushSave(name)) {
        showToast(t('Dash.BoardRenamed', name));
      }
    },
    [flushSave, showToast],
  );

  const deleteBoard = useCallback(async () => {
    setDialog(null);
    const target = boards[currentIndexRef.current];
    if (!target || target.IsHome || !target.BoardId) {
      return;
    }
    if (saveTimer.current) {
      clearTimeout(saveTimer.current);
      saveTimer.current = null;
    }
    dirtyRef.current = false;
    snapshotRef.current = null;
    setEditing(false);
    setBusy(true);
    try {
      await dashboardApi.deleteBoard(target.BoardId);
      await loadBootstrap(null, 'switch');
      showToast(t('Dash.BoardDeleted', target.Name));
    } catch {
      showToast(t('Dash.SaveFailed'));
    } finally {
      setBusy(false);
    }
  }, [boards, loadBootstrap, showToast, setEditing]);

  const resetBoard = useCallback(async () => {
    const previous = layoutRef.current;
    setBusy(true);
    try {
      const response = await dashboardApi.resetLayout(boardRef.current.boardId);
      if (response.Error || !response.Board) {
        showToast(saveErrorText(response.Error));
        return;
      }
      const resetIndex = currentIndexRef.current;
      applyBoard(response.Board);
      setBoards((list) => list.map((item, index) => (index === resetIndex ? { ...item, BoardId: response.Board?.BoardId ?? null } : item)));
      // The reset is already stored, so Cancel must not bring the old layout back; Undo does, explicitly.
      if (editingRef.current) {
        snapshotRef.current = layoutRef.current;
      }
      showToast(t('Dash.ResetDone'), {
        label: t('Dash.Undo'),
        run: () => {
          setLayout(previous);
          scheduleSave();
        },
      });
    } catch {
      showToast(t('Dash.SaveFailed'));
    } finally {
      setBusy(false);
    }
  }, [applyBoard, scheduleSave, setLayout, showToast]);

  // ---------- Conflict ----------
  const keepMine = useCallback(() => {
    const current = conflictRef.current;
    if (!current) {
      return;
    }
    boardRef.current = { boardId: current.BoardId ?? null, rowVersion: current.RowVersion ?? 0 };
    setBoardId(current.BoardId ?? null);
    setStored(normalizeLayout(current.Layout));
    setConflict(null);
    dirtyRef.current = true;
    if (!editingRef.current) {
      void flushSave().then((saved) => {
        if (saved) showToast(t('Dash.SavedToast'));
      });
    }
  }, [flushSave, setConflict, setStored, showToast]);

  const loadLatest = useCallback(() => {
    const current = conflictRef.current;
    if (!current) {
      return;
    }
    setConflict(null);
    snapshotRef.current = null;
    setGalleryOpen(false);
    if (editingRef.current) {
      setEditing(false);
    }
    applyBoard(current);
    showToast(t('Dash.LatestLoaded'));
  }, [applyBoard, setConflict, setEditing, showToast]);

  // ---------- Realtime ----------
  useEffect(() => {
    if (!bus) {
      return undefined;
    }
    return bus.onStatus(setBusStatus);
  }, [bus]);

  const departmentId = boot?.DepartmentId ?? 0;
  useEffect(() => {
    if (bus && departmentId > 0) {
      bus.start(departmentId);
    }
  }, [bus, departmentId]);

  useEffect(() => () => bus?.stop(), [bus]);

  // ---------- ADP (plan §9A.3) ----------
  const adpHost = mode === 'board' && !!props.adpReveal;
  const adpInfo = boot?.Adp ?? null;
  const enforced = !!adpInfo?.Enforced;
  const canRevealAny = !!adpInfo && (adpInfo.CanRevealCall || adpInfo.CanRevealOperational || adpInfo.CanRevealPersonnel || adpInfo.CanRevealContact);
  const canReveal = adpHost && enforced && canRevealAny && !wall;
  // The chip appears only when a widget on the board holds a protected field this viewer may reveal (plan §9A.3).
  const revealableOnBoard = items.some((item) => item.definition.CanRevealProtected === true);
  const showAdpChip = canReveal && revealableOnBoard;
  const canRevealRef = useRef(canReveal);
  canRevealRef.current = canReveal;

  const requestReveal = useCallback(() => {
    if (canRevealRef.current) {
      document.getElementById('adpRevealButton')?.click();
    }
  }, []);

  const conceal = useCallback(() => {
    document.getElementById('adpConcealButton')?.click();
  }, []);

  useEffect(() => {
    if (!adpHost) {
      return undefined;
    }
    const target = adpWindow();
    const previous = {
      revealed: target.resgridAdpPageRevealed,
      concealed: target.resgridAdpPageConcealed,
      renewed: target.resgridAdpPageRenewed,
    };
    target.resgridAdpPageRevealed = (values?: unknown) => {
      previous.revealed?.(values);
      revealedRef.current = true;
      setRevealed(true);
      actionsRef.current.toast(t('Dash.AdpRevealedToast'));
      actionsRef.current.announce(t('Dash.AdpRevealedToast'));
    };
    target.resgridAdpPageConcealed = () => {
      previous.concealed?.();
      const was = revealedRef.current;
      revealedRef.current = false;
      setRevealed(false);
      if (was) {
        actionsRef.current.toast(t('Dash.AdpConcealedToast'));
        actionsRef.current.announce(t('Dash.AdpConcealedToast'));
      }
    };
    target.resgridAdpPageRenewed = () => {
      previous.renewed?.();
    };
    return () => {
      target.resgridAdpPageRevealed = previous.revealed;
      target.resgridAdpPageConcealed = previous.concealed;
      target.resgridAdpPageRenewed = previous.renewed;
    };
  }, [adpHost]);

  const adpState = useMemo<WidgetAdpState>(() => ({ enforced, revealed, canReveal, requestReveal }), [enforced, revealed, canReveal, requestReveal]);

  // ---------- Wall display, direction ----------
  useEffect(() => {
    document.body.classList.toggle('rg-wall', wall);
    // Wall display never reveals (plan §9A.4).
    if (wall && revealedRef.current) {
      conceal();
    }
  }, [wall, conceal]);

  useEffect(() => () => document.body.classList.remove('rg-wall'), []);

  useEffect(() => {
    const update = () => setRtl(readDirection(props.hostElement));
    const observer = new MutationObserver(update);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['dir'] });
    observer.observe(document.body, { attributes: true, attributeFilter: ['dir'] });
    update();
    return () => observer.disconnect();
  }, [props.hostElement]);

  // ---------- Template mode ----------
  const setTemplateDraft = useCallback((draft: TemplateDraft) => {
    templateDirtyRef.current = true;
    setTemplateDraftState(draft);
    setTemplateError(null);
  }, []);

  const saveTemplate = useCallback(async () => {
    const error = validateDraft(templateDraft);
    if (error) {
      setTemplateError(error);
      showToast(t(error));
      return;
    }
    setTemplateError(null);
    setSaving(true);
    try {
      const response = await dashboardApi.saveTemplate({
        TemplateId: templateIdRef.current,
        Name: templateDraft.name.trim(),
        Description: templateDraft.description.trim() || null,
        Audience: templateDraft.audience,
        TargetId: templateDraft.audience === 0 ? null : templateDraft.targetId,
        Priority: templateDraft.priority,
        IsDefault: templateDraft.isDefault,
        Layout: layoutRef.current,
      });
      if (response.Error || !response.TemplateId) {
        showToast(saveErrorText(response.Error));
        return;
      }
      templateIdRef.current = response.TemplateId;
      templateDirtyRef.current = false;
      setTemplateIsNew(false);
      try {
        const url = new URL(window.location.href);
        url.searchParams.set('id', response.TemplateId);
        window.history.replaceState(null, '', url.toString());
      } catch {
        // The address bar is a convenience.
      }
      showToast(t('Dash.TemplateSaved'));
    } catch {
      showToast(t('Dash.SaveFailed'));
    } finally {
      setSaving(false);
    }
  }, [templateDraft, showToast]);

  // ---------- Header actions ----------
  const goClassic = useCallback(async () => {
    try {
      if (saveTimer.current || dirtyRef.current) {
        await flushSave();
      }
      const response = await dashboardApi.setClassic(true);
      window.location.assign(response.Redirect || boot?.Header?.ClassicUrl || CLASSIC_FALLBACK_URL);
    } catch {
      showToast(t('Dash.SaveFailed'));
    }
  }, [flushSave, boot?.Header?.ClassicUrl, showToast]);

  const setDensity = useCallback((value: Density) => mutate((layoutNow) => (layoutNow.Density === value ? layoutNow : { ...layoutNow, Density: value })), [mutate]);

  const applySettings = useCallback(
    async (id: string, settings: Record<string, unknown>) => {
      setSettingsFor(null);
      mutate((layoutNow) => ({ ...layoutNow, Widgets: layoutNow.Widgets.map((widget) => (widget.Id === id ? { ...widget, Settings: settings } : widget)) }));
      if (mode === 'board' && !editingRef.current) {
        if (await flushSave()) {
          showToast(t('Dash.SettingsSaved'));
        }
      }
    },
    [mode, mutate, flushSave, showToast],
  );

  const dismissCoach = useCallback(() => {
    setCoach(false);
    rememberCoachDismissed();
  }, []);

  // ---------- Runtime context ----------
  const runtime = useMemo<DashboardRuntime | null>(
    () =>
      boot
        ? {
            mode,
            editing,
            wall,
            density,
            rtl,
            revealed,
            adp: adpState,
            bus,
            busStatus,
            bootstrap: boot,
            categories,
            format,
            boardId,
            stored: storedMap,
            actions,
            pendingFocus,
            flashId,
          }
        : null,
    [mode, editing, wall, density, rtl, revealed, adpState, bus, busStatus, boot, categories, format, boardId, storedMap, actions, flashId],
  );

  // ---------- Render ----------
  const rootClass = ['rg-dash', editing ? 'rg-editing' : '', density === 'compact' ? 'rg-compact' : '', wall ? 'rg-wall-mode' : ''].filter(Boolean).join(' ');

  if (bootError && !boot) {
    return (
      <div className="rg-dash">
        <div className="rg-boot-error" role="alert">
          <i className="fa fa-exclamation-circle" aria-hidden="true" />
          <b>{t('Dash.BootstrapFailed')}</b>
          <div className="rg-boot-error-actions">
            <button type="button" className="btn btn-sm btn-primary" onClick={loadInitial}>
              {t('Dash.Retry')}
            </button>
            {mode === 'board' ? (
              <a className="btn btn-sm btn-link" href={CLASSIC_FALLBACK_URL}>
                {t('UseClassicDashboard')}
              </a>
            ) : null}
          </div>
        </div>
      </div>
    );
  }

  if (!boot || !runtime) {
    return (
      <div className="rg-dash" aria-busy="true">
        <div className="rg-grid rg-grid-loading">
          {['s', 's', 's', 's', 'l', 'l', 'full'].map((size, index) => (
            <div key={index} className="rg-w rg-w-placeholder" data-size={size}>
              <div className="rg-w-body">
                <Skeleton />
              </div>
            </div>
          ))}
        </div>
      </div>
    );
  }

  const settingsInstance = settingsFor ? layout.Widgets.find((widget) => widget.Id === settingsFor) : undefined;
  const settingsDefinition = settingsInstance ? definitions.get(settingsInstance.Key) : undefined;
  const currentBoard = boards[currentIndex];
  const maxBoards = boot.MaxBoards || 5;
  const showTabs = mode === 'board' && !wall && (boards.length > 1 || editing);
  const canAddBoard = mode === 'board' && boards.length < maxBoards;

  const emptyGrid = (
    <div className="rg-board-empty">
      <i className="fa fa-th-large" aria-hidden="true" />
      <b>{editing ? t('Dash.EmptyBoardEditing') : t('Dash.EmptyBoard')}</b>
      {editing ? (
        <button type="button" className="btn btn-sm btn-primary" onClick={() => setGalleryOpen(true)}>
          <i className="fa fa-plus" aria-hidden="true" /> {t('Dash.AddWidget')}
        </button>
      ) : mode === 'board' && !wall ? (
        <button type="button" className="btn btn-sm btn-default" onClick={() => void startEditing()}>
          <i className="fa fa-th-large" aria-hidden="true" /> {t('Dash.Customize')}
        </button>
      ) : null}
    </div>
  );

  return (
    <DashboardRuntimeContext.Provider value={runtime}>
      <div className={rootClass} data-mode={mode}>
        {mode === 'board' ? (
          <>
            <HeaderBand
              header={boot.Header}
              editing={editing}
              wall={wall}
              density={density}
              adpChip={showAdpChip ? <AdpChip revealed={revealed} onVerify={requestReveal} onConceal={conceal} /> : null}
              onCustomize={() => void startEditing()}
              onDensity={setDensity}
              onWall={setWall}
              onClassic={() => void goClassic()}
              onAddBoard={canAddBoard && !wall ? () => setDialog({ kind: 'addBoard' }) : null}
            />
            <PulseStrip bus={bus} />
          </>
        ) : (
          <TemplatePanel draft={templateDraft} onChange={setTemplateDraft} error={templateError} isNew={templateIsNew} />
        )}

        {conflict ? <ConflictBanner onKeepMine={keepMine} onLoadLatest={loadLatest} /> : null}

        {showTabs ? (
          <BoardTabs
            boards={boards}
            currentIndex={currentIndex}
            editing={editing}
            canAdd={canAddBoard}
            busy={busy}
            onSwitch={(index) => void switchBoard(index)}
            onAdd={() => setDialog({ kind: 'addBoard' })}
            onRename={() => setDialog({ kind: 'renameBoard' })}
            onDelete={() => setDialog({ kind: 'deleteBoard' })}
          />
        ) : null}

        {editing ? (
          mode === 'board' ? (
            <EditBar
              onAdd={() => setGalleryOpen(true)}
              onReset={() => void resetBoard()}
              onCancel={() => void finishEditing(false)}
              onDone={() => void finishEditing(true)}
              saving={saving || busy}
            />
          ) : (
            <TemplateEditBar
              onAdd={() => setGalleryOpen(true)}
              onSave={() => void saveTemplate()}
              saving={saving}
              backUrl={boot.Header?.TemplatesUrl || TEMPLATES_FALLBACK_URL}
            />
          )
        ) : null}

        {coach && !editing && !wall ? <FirstRunCoach onCustomize={() => void startEditing()} onDismiss={dismissCoach} /> : null}

        <div className={busy ? 'rg-grid-wrap rg-busy' : 'rg-grid-wrap'}>
          <DashboardGrid key={boardKey} items={items} onDrop={onDrop} empty={emptyGrid} />
        </div>

        <div className="sr-only" aria-live="polite" aria-atomic="true">
          {announcement}
        </div>
      </div>

      {galleryOpen ? (
        <WidgetGallery
          definitions={boot.Widgets ?? []}
          locked={boot.Locked ?? []}
          categories={boot.Categories ?? []}
          widgets={layout.Widgets}
          atLimit={layout.Widgets.length >= maxWidgets}
          maxWidgets={maxWidgets}
          onAdd={addWidget}
          onClose={() => setGalleryOpen(false)}
        />
      ) : null}

      {settingsInstance && settingsDefinition ? (
        <WidgetSettingsDialog
          instance={settingsInstance}
          definition={settingsDefinition}
          sources={boot.Options ?? {}}
          onApply={(settings) => void applySettings(settingsInstance.Id, settings)}
          onClose={() => setSettingsFor(null)}
        />
      ) : null}

      {dialog?.kind === 'addBoard' ? (
        <PromptDialog
          title={t('Dash.AddBoard')}
          label={t('Dash.BoardName')}
          initialValue=""
          maxLength={NAME_MAX}
          confirmLabel={t('Dash.Add')}
          onConfirm={(name) => void addBoard(name)}
          onClose={() => setDialog(null)}
        />
      ) : null}
      {dialog?.kind === 'renameBoard' && currentBoard ? (
        <PromptDialog
          title={t('Dash.RenameBoard')}
          label={t('Dash.BoardName')}
          initialValue={currentBoard.Name || (currentBoard.IsHome ? t('BoardHome') : '')}
          maxLength={NAME_MAX}
          confirmLabel={t('Dash.Rename')}
          onConfirm={(name) => void renameBoard(name)}
          onClose={() => setDialog(null)}
        />
      ) : null}
      {dialog?.kind === 'deleteBoard' && currentBoard ? (
        <ConfirmDialog
          title={t('Dash.DeleteBoard')}
          message={t('Dash.DeleteBoardConfirm', currentBoard.Name)}
          confirmLabel={t('Dash.Delete')}
          danger
          onConfirm={() => void deleteBoard()}
          onClose={() => setDialog(null)}
        />
      ) : null}

      <ToastView toast={toast} onDismiss={() => setToast(null)} />
    </DashboardRuntimeContext.Provider>
  );
}
