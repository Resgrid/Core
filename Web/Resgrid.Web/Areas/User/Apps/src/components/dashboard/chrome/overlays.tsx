import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
  type KeyboardEvent,
  type MouseEvent as ReactMouseEvent,
  type ReactNode,
  type RefObject,
} from 'react';
import { createPortal } from 'react-dom';
import { t } from '../i18n';
import type { ToastAction } from '../runtime';

// Overlays render into document.body: .rg-dash is a size container (container-type), which also makes it the
// containing block for position: fixed descendants, so a drawer or dialog inside it would be positioned wrongly.

export function DashLayer({ children }: { children: ReactNode }) {
  return createPortal(<div className="rg-dash-layer">{children}</div>, document.body);
}

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function trapTab(event: KeyboardEvent, container: HTMLElement | null): void {
  if (event.key !== 'Tab' || !container) {
    return;
  }
  const items = Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((item) => item.offsetParent !== null);
  if (items.length === 0) {
    return;
  }
  const first = items[0];
  const last = items[items.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

/** Keeps focus inside `container` while mounted and returns it to whatever had it before. */
export function useFocusReturn(container: RefObject<HTMLElement | null>, initialSelector?: string): void {
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const box = container.current;
    const initial =
      (initialSelector ? box?.querySelector<HTMLElement>(initialSelector) : null) ??
      box?.querySelector<HTMLElement>('[data-autofocus]') ??
      box?.querySelector<HTMLElement>(FOCUSABLE);
    initial?.focus();
    return () => {
      if (previous && previous.isConnected && typeof previous.focus === 'function') {
        previous.focus();
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
}

export interface DialogProps {
  title: ReactNode;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}

export function Dialog({ title, onClose, children, footer, wide }: DialogProps) {
  const boxRef = useRef<HTMLDivElement | null>(null);
  const titleId = useId();
  useFocusReturn(boxRef);

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      onClose();
      return;
    }
    trapTab(event, boxRef.current);
  };

  const onBackdrop = (event: ReactMouseEvent) => {
    if (event.target === event.currentTarget) {
      onClose();
    }
  };

  return (
    <DashLayer>
      <div className="rg-scrim" aria-hidden="true" />
      <div className="rg-modal" role="dialog" aria-modal="true" aria-labelledby={titleId} onKeyDown={onKeyDown} onMouseDown={onBackdrop}>
        <div className={`rg-modal-box${wide ? ' rg-modal-wide' : ''}`} ref={boxRef}>
          <header id={titleId}>{title}</header>
          <div className="rg-modal-body">{children}</div>
          {footer ? <footer>{footer}</footer> : null}
        </div>
      </div>
    </DashLayer>
  );
}

export function PromptDialog(props: {
  title: string;
  label: string;
  initialValue: string;
  maxLength: number;
  confirmLabel: string;
  onConfirm: (value: string) => void;
  onClose: () => void;
}) {
  const [value, setValue] = useState(props.initialValue);
  const trimmed = value.trim();
  const submit = () => {
    if (trimmed) {
      props.onConfirm(trimmed);
    }
  };

  return (
    <Dialog
      title={props.title}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="btn btn-default btn-sm" onClick={props.onClose}>
            {t('Dash.Cancel')}
          </button>
          <button type="button" className="btn btn-primary btn-sm" disabled={!trimmed} onClick={submit}>
            {props.confirmLabel}
          </button>
        </>
      }
    >
      <label>
        {props.label}
        <input
          className="rg-input"
          data-autofocus
          value={value}
          maxLength={props.maxLength}
          onChange={(event) => setValue(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              event.preventDefault();
              submit();
            }
          }}
        />
      </label>
    </Dialog>
  );
}

export function ConfirmDialog(props: { title: string; message: string; confirmLabel: string; danger?: boolean; onConfirm: () => void; onClose: () => void }) {
  return (
    <Dialog
      title={props.title}
      onClose={props.onClose}
      footer={
        <>
          <button type="button" className="btn btn-default btn-sm" onClick={props.onClose} data-autofocus>
            {t('Dash.Cancel')}
          </button>
          <button type="button" className={`btn btn-sm ${props.danger ? 'btn-danger' : 'btn-primary'}`} onClick={props.onConfirm}>
            {props.confirmLabel}
          </button>
        </>
      }
    >
      <p className="rg-modal-text">{props.message}</p>
    </Dialog>
  );
}

export interface ToastState {
  id: number;
  text: string;
  action?: ToastAction;
}

export function ToastView({ toast, onDismiss }: { toast: ToastState | null; onDismiss: () => void }) {
  if (!toast) {
    return null;
  }
  return (
    <DashLayer>
      <div className="rg-toast" role="status" key={toast.id}>
        <span>{toast.text}</span>
        {toast.action ? (
          <button
            type="button"
            onClick={() => {
              toast.action?.run();
              onDismiss();
            }}
          >
            {toast.action.label}
          </button>
        ) : null}
        <button type="button" className="rg-toast-close" aria-label={t('Dash.Dismiss')} onClick={onDismiss}>
          <i className="fa fa-times" aria-hidden="true" />
        </button>
      </div>
    </DashLayer>
  );
}

// ---------- Menus ----------

const MenuCloseContext = createContext<() => void>(() => undefined);

export function MenuButton(props: {
  label: string;
  className: string;
  content: ReactNode;
  children: ReactNode;
  menuClassName?: string;
}) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLSpanElement | null>(null);
  const buttonRef = useRef<HTMLButtonElement | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);

  const close = useCallback(() => {
    setOpen(false);
    buttonRef.current?.focus();
  }, []);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    menuRef.current?.querySelector<HTMLElement>('[role="menuitem"]:not([disabled]), [role="menuitemradio"]:not([disabled])')?.focus();
    const onDown = (event: MouseEvent) => {
      if (!wrapRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open]);

  const onMenuKeyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      close();
      return;
    }
    if (event.key === 'Tab') {
      setOpen(false);
      return;
    }
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp' && event.key !== 'Home' && event.key !== 'End') {
      return;
    }
    event.preventDefault();
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLElement>('[role^="menuitem"]:not([disabled])') ?? []);
    if (items.length === 0) {
      return;
    }
    const index = items.indexOf(document.activeElement as HTMLElement);
    const next =
      event.key === 'Home' ? 0 : event.key === 'End' ? items.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + items.length) % items.length;
    items[next].focus();
  };

  return (
    <span className="rg-menu-wrap" ref={wrapRef}>
      <button
        ref={buttonRef}
        type="button"
        className={props.className}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={props.label}
        title={props.label}
        onClick={() => setOpen((value) => !value)}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown' && !open) {
            event.preventDefault();
            setOpen(true);
          }
        }}
      >
        {props.content}
      </button>
      {open ? (
        <div className={`rg-menu ${props.menuClassName ?? ''}`} role="menu" aria-label={props.label} ref={menuRef} onKeyDown={onMenuKeyDown}>
          <MenuCloseContext.Provider value={close}>{props.children}</MenuCloseContext.Provider>
        </div>
      ) : null}
    </span>
  );
}

export function MenuItem(props: {
  icon?: string;
  children: ReactNode;
  onSelect?: () => void;
  href?: string | null;
  danger?: boolean;
  checked?: boolean;
  disabled?: boolean;
  keepOpen?: boolean;
}) {
  const close = useContext(MenuCloseContext);
  const icon = props.icon ? <i className={`fa ${props.icon} rg-fw`} aria-hidden="true" /> : null;
  const role = props.checked === undefined ? 'menuitem' : 'menuitemradio';
  if (props.href) {
    return (
      <a role={role} href={props.href} className={props.danger ? 'rg-danger' : undefined} onClick={() => close()}>
        {icon}
        {props.children}
      </a>
    );
  }
  return (
    <button
      type="button"
      role={role}
      aria-checked={props.checked === undefined ? undefined : props.checked}
      disabled={props.disabled}
      className={props.danger ? 'rg-danger' : undefined}
      onClick={() => {
        if (!props.keepOpen) {
          close();
        }
        props.onSelect?.();
      }}
    >
      {icon}
      <span className="rg-menu-text">{props.children}</span>
      {props.checked ? <i className="fa fa-check rg-menu-check" aria-hidden="true" /> : null}
    </button>
  );
}

/** Closes the enclosing MenuButton's menu (for custom menu content such as size chips). */
export function useMenuClose(): () => void {
  return useContext(MenuCloseContext);
}

export function MenuLabel({ children }: { children: ReactNode }) {
  return <div className="rg-menu-label">{children}</div>;
}

export function MenuDivider() {
  return <hr role="separator" />;
}

// ---------- Ages ----------

const tickSubscribers = new Set<() => void>();
let tickTimer: ReturnType<typeof setInterval> | null = null;

/** Re-renders the caller every `5 s` (one shared timer for every frame). */
export function useTick(): void {
  const [, setTick] = useState(0);
  useEffect(() => {
    const listener = () => setTick((value) => value + 1);
    tickSubscribers.add(listener);
    if (!tickTimer) {
      tickTimer = setInterval(() => tickSubscribers.forEach((subscriber) => subscriber()), 5000);
    }
    return () => {
      tickSubscribers.delete(listener);
      if (tickSubscribers.size === 0 && tickTimer) {
        clearInterval(tickTimer);
        tickTimer = null;
      }
    };
  }, []);
}

export function ageText(fetchedAt: number | null): string {
  if (!fetchedAt) {
    return t('Dash.AgeLoading');
  }
  const seconds = Math.max(0, Math.round((Date.now() - fetchedAt) / 1000));
  if (seconds < 5) return t('Dash.AgeJustNow');
  if (seconds < 60) return t('Dash.AgeSeconds', seconds);
  if (seconds < 3600) return t('Dash.AgeMinutes', Math.round(seconds / 60));
  return t('Dash.AgeHours', Math.round(seconds / 3600));
}

export function AgeLabel(props: { fetchedAt: number | null; live: boolean; stale: boolean; failed: boolean }) {
  useTick();
  const text = props.stale ? t('Dash.AgeReconnecting') : props.failed ? t('Dash.AgeUpdateFailed') : ageText(props.fetchedAt);
  const title = props.stale ? t('Dash.StaleTooltip') : props.failed ? t('Dash.UpdateFailedTooltip') : props.live ? t('Dash.LiveTooltip') : undefined;
  return (
    <span className="rg-w-age" title={title}>
      {props.live || props.stale || props.failed ? <span className="rg-live" aria-hidden="true" /> : null}
      <span className="rg-w-age-text">{text}</span>
    </span>
  );
}
