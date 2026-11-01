import { useCallback, useId, useMemo, useRef, useState, type ReactNode } from 'react';
import {
  closestCenter,
  DndContext,
  DragOverlay,
  PointerSensor,
  pointerWithin,
  TouchSensor,
  useSensor,
  useSensors,
  type CollisionDetection,
  type DragEndEvent,
  type DragMoveEvent,
  type DragStartEvent,
} from '@dnd-kit/core';
import { SortableContext, useSortable, type SortingStrategy } from '@dnd-kit/sortable';
import { DashLayer } from '../chrome/overlays';
import { DEFAULT_ACCENT, DragGhost, WidgetFrame, type SortableBinding } from '../chrome/WidgetFrame';
import { t } from '../i18n';
import { useDashboardRuntime } from '../runtime';
import type { LayoutWidget, WidgetDefinition } from '../types';
import { effectiveSize } from './sizes';

// 12-column CSS grid with dense row flow (plan §5.1, dashboard.css). Dragging uses @dnd-kit: the frame stays in place
// (dimmed), a ghost follows the pointer, and the frame under it shows a before/after marker — mirrored under RTL. The
// order changes once, on drop, so a mixed-size grid never oscillates while reflowing under the pointer.

export interface GridItem {
  instance: LayoutWidget;
  definition: WidgetDefinition;
}

interface DropTarget {
  id: string;
  side: 'before' | 'after';
}

// Items keep their places while dragging; only the drop marker moves.
const noSorting: SortingStrategy = () => null;

const collision: CollisionDetection = (args) => {
  const within = pointerWithin(args);
  return within.length > 0 ? within : closestCenter(args);
};

function SortableFrame(props: { item: GridItem; position: number; total: number; dropSide: 'before' | 'after' | null; keyboardHelpId: string; editing: boolean }) {
  const { setNodeRef, listeners, isDragging } = useSortable({ id: props.item.instance.Id, disabled: !props.editing });
  const sortable = useMemo<SortableBinding>(() => ({ setNodeRef, listeners, isDragging }), [setNodeRef, listeners, isDragging]);
  return (
    <WidgetFrame
      instance={props.item.instance}
      definition={props.item.definition}
      position={props.position}
      total={props.total}
      dropSide={props.dropSide}
      sortable={sortable}
      keyboardHelpId={props.keyboardHelpId}
    />
  );
}

export function DashboardGrid(props: { items: GridItem[]; onDrop: (id: string, targetId: string, before: boolean) => void; empty?: ReactNode }) {
  const runtime = useDashboardRuntime();
  const keyboardHelpId = useId();
  const [activeId, setActiveId] = useState<string | null>(null);
  const [drop, setDropState] = useState<DropTarget | null>(null);
  const dropRef = useRef<DropTarget | null>(null);
  const setDrop = useCallback((next: DropTarget | null | ((current: DropTarget | null) => DropTarget | null)) => {
    const value = typeof next === 'function' ? next(dropRef.current) : next;
    dropRef.current = value;
    setDropState(value);
  }, []);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    // Long-press to drag on touch screens, so a swipe still scrolls the page.
    useSensor(TouchSensor, { activationConstraint: { delay: 250, tolerance: 6 } }),
  );

  const ids = useMemo(() => props.items.map((item) => item.instance.Id), [props.items]);
  const active = activeId ? props.items.find((item) => item.instance.Id === activeId) : undefined;

  const updateDrop = useCallback(
    (event: DragMoveEvent) => {
      const over = event.over;
      const rect = event.active.rect.current.translated;
      if (!over || over.id === event.active.id || !rect) {
        setDrop((current) => (current ? null : current));
        return;
      }
      const centerX = rect.left + rect.width / 2;
      const centerY = rect.top + rect.height / 2;
      const target = over.rect;
      // Beside the target: horizontal half decides (mirrored under RTL); above/below it: vertical half decides.
      const sameRow = centerY >= target.top && centerY <= target.top + target.height;
      const before = sameRow
        ? runtime.rtl
          ? centerX > target.left + target.width / 2
          : centerX < target.left + target.width / 2
        : centerY < target.top + target.height / 2;
      const side = before ? 'before' : 'after';
      setDrop((current) => (current && current.id === over.id && current.side === side ? current : { id: String(over.id), side }));
    },
    [runtime.rtl, setDrop],
  );

  const onDragStart = (event: DragStartEvent) => {
    setActiveId(String(event.active.id));
    setDrop(null);
  };

  const onDragEnd = (event: DragEndEvent) => {
    const target = dropRef.current;
    setActiveId(null);
    setDrop(null);
    if (event.over && target && target.id === String(event.over.id) && target.id !== String(event.active.id)) {
      props.onDrop(String(event.active.id), target.id, target.side === 'before');
    }
  };

  const onDragCancel = () => {
    setActiveId(null);
    setDrop(null);
  };

  const accent = active ? runtime.categories.get(active.definition.Category)?.Color || DEFAULT_ACCENT : DEFAULT_ACCENT;

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={collision}
      onDragStart={onDragStart}
      onDragMove={updateDrop}
      onDragOver={updateDrop}
      onDragEnd={onDragEnd}
      onDragCancel={onDragCancel}
      accessibility={{
        screenReaderInstructions: { draggable: t('Dash.KeyboardHelp') },
        announcements: {
          onDragStart: () => t('Dash.DragStarted'),
          onDragOver: () => undefined,
          onDragEnd: () => t('Dash.DragEnded'),
          onDragCancel: () => t('Dash.DragCancelled'),
        },
      }}
    >
      <SortableContext items={ids} strategy={noSorting}>
        <div className="rg-grid">
          {props.items.length === 0 ? props.empty : null}
          {props.items.map((item, index) => (
            <SortableFrame
              key={item.instance.Id}
              item={item}
              position={index + 1}
              total={props.items.length}
              dropSide={drop && drop.id === item.instance.Id ? drop.side : null}
              keyboardHelpId={keyboardHelpId}
              editing={runtime.editing}
            />
          ))}
        </div>
      </SortableContext>
      <span id={keyboardHelpId} className="sr-only">
        {t('Dash.KeyboardHelp')}
      </span>
      <DashLayer>
        <DragOverlay dropAnimation={null}>
          {active ? <DragGhost definition={active.definition} accent={accent} size={effectiveSize(active.instance.Size, active.definition)} /> : null}
        </DragOverlay>
      </DashLayer>
    </DndContext>
  );
}
