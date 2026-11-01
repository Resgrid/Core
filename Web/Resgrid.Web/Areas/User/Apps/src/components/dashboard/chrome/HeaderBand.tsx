import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { dashboardApi } from '../api';
import { RESUBSCRIBED, type DashboardBus } from '../data/dashboardBus';
import { safeTimeZone, t, uiLocale } from '../i18n';
import type { Density, HeaderInfo, PulseItem } from '../types';
import { MenuButton, MenuDivider, MenuItem, MenuLabel } from './overlays';

// Header band and pulse strip (plan §6.3). Not widgets: always present on a member's board, hidden in template mode.

function useDepartmentClock(timeZone: string | null | undefined) {
  const zone = safeTimeZone(timeZone);
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), 15000);
    return () => clearInterval(timer);
  }, []);

  return useMemo(() => {
    const locale = uiLocale();
    const hour = Number(new Intl.DateTimeFormat('en-US', { timeZone: zone, hour: 'numeric', hourCycle: 'h23' }).format(now));
    const time = new Intl.DateTimeFormat(locale, { timeZone: zone, hour: '2-digit', minute: '2-digit' }).format(now);
    const zoneName =
      new Intl.DateTimeFormat(locale, { timeZone: zone, timeZoneName: 'short' }).formatToParts(now).find((part) => part.type === 'timeZoneName')?.value ?? '';
    return { hour, time, zoneName };
  }, [now, zone]);
}

function greeting(hour: number, firstName: string | null | undefined): string {
  const part = hour < 12 ? 'Morning' : hour < 18 ? 'Afternoon' : 'Evening';
  return firstName && firstName.trim() ? t(`Dash.Greet${part}`, firstName.trim()) : t(`Dash.Greet${part}NoName`);
}

export function AdpChip(props: { revealed: boolean; onVerify: () => void; onConceal: () => void }) {
  if (props.revealed) {
    return (
      <span className="rg-adp-chip rg-adp-revealed" role="status">
        <i className="fa fa-eye" aria-hidden="true" /> {t('Dash.AdpRevealed')}
        <button type="button" onClick={props.onConceal}>
          {t('Dash.AdpConceal')}
        </button>
      </span>
    );
  }
  return (
    <span className="rg-adp-chip" role="status">
      <i className="fa fa-shield" aria-hidden="true" /> {t('Dash.AdpHidden')}
      <button type="button" onClick={props.onVerify}>
        {t('Dash.AdpVerifyToReveal')}
      </button>
    </span>
  );
}

export interface HeaderBandProps {
  header: HeaderInfo | null;
  editing: boolean;
  wall: boolean;
  density: Density;
  adpChip: ReactNode;
  onCustomize: () => void;
  onDensity: (density: Density) => void;
  onWall: (wall: boolean) => void;
  onClassic: () => void;
  onAddBoard: (() => void) | null;
}

export function HeaderBand(props: HeaderBandProps) {
  const header = props.header;
  const clock = useDepartmentClock(header?.TimeZone);

  return (
    <header className="rg-head">
      <div className="rg-head-main">
        <h1 className="rg-greet">{greeting(clock.hour, header?.FirstName)}</h1>
        <div className="rg-head-meta">
          <i className="fa fa-building-o" aria-hidden="true" />
          {header?.DepartmentName ? <span>{header.DepartmentName}</span> : null}
          <span className="rg-head-sep" aria-hidden="true">
            {' · '}
          </span>
          <time className="rg-head-clock" aria-label={t('Dash.DepartmentTime')}>
            {clock.time}
          </time>{' '}
          <span>{clock.zoneName}</span>
          {header?.DepartmentCode ? (
            <>
              <span className="rg-head-sep" aria-hidden="true">
                {' · '}
              </span>
              <span>{t('Dash.DepartmentCodeMeta', header.DepartmentCode)}</span>
            </>
          ) : null}
        </div>
      </div>
      <div className="rg-head-actions">
        {props.wall ? (
          <button type="button" className="btn btn-default" onClick={() => props.onWall(false)}>
            <i className="fa fa-compress" aria-hidden="true" /> {t('Dash.ExitWall')}
          </button>
        ) : (
          <>
            {header?.CanCreateCall && header.NewCallUrl ? (
              <a href={header.NewCallUrl} className="btn btn-primary">
                <i className="fa fa-plus" aria-hidden="true" /> {t('Dash.NewCall')}
              </a>
            ) : null}
            {header?.CanSendMessage && header.MessageUrl ? (
              <a href={header.MessageUrl} className="btn btn-default">
                <i className="fa fa-envelope-o" aria-hidden="true" /> {t('Dash.Message')}
              </a>
            ) : null}
            {props.adpChip}
            {!props.editing ? (
              <button type="button" className="btn btn-default" onClick={props.onCustomize}>
                <i className="fa fa-th-large" aria-hidden="true" /> {t('Dash.Customize')}
              </button>
            ) : null}
          </>
        )}
        <MenuButton label={t('Dash.MoreOptions')} className="btn btn-default rg-head-more" content={<i className="fa fa-ellipsis-h" aria-hidden="true" />}>
          <MenuLabel>{t('Dash.Density')}</MenuLabel>
          <MenuItem checked={props.density === 'comfortable'} onSelect={() => props.onDensity('comfortable')}>
            {t('Dash.DensityComfortable')}
          </MenuItem>
          <MenuItem checked={props.density === 'compact'} onSelect={() => props.onDensity('compact')}>
            {t('Dash.DensityCompact')}
          </MenuItem>
          <MenuDivider />
          <MenuItem icon={props.wall ? 'fa-compress' : 'fa-television'} onSelect={() => props.onWall(!props.wall)}>
            {props.wall ? t('Dash.ExitWall') : t('Dash.WallDisplay')}
          </MenuItem>
          {props.onAddBoard ? (
            <MenuItem icon="fa-plus-square-o" onSelect={props.onAddBoard}>
              {t('Dash.AddBoard')}
            </MenuItem>
          ) : null}
          {header?.CanEditTemplates && header.TemplatesUrl ? (
            <MenuItem icon="fa-clone" href={header.TemplatesUrl}>
              {t('TemplatesTitle')}
            </MenuItem>
          ) : null}
          <MenuDivider />
          <MenuItem icon="fa-history" onSelect={props.onClassic}>
            {t('UseClassicDashboard')}
          </MenuItem>
        </MenuButton>
      </div>
    </header>
  );
}

const PULSE_POLL_MS = 60000;
const PULSE_TOPICS = ['calls', 'requirements', RESUBSCRIBED];

export function PulseStrip({ bus }: { bus: DashboardBus | null }) {
  const [items, setItems] = useState<PulseItem[] | null>(null);

  useEffect(() => {
    let disposed = false;
    let controller: AbortController | null = null;
    let debounce: ReturnType<typeof setTimeout> | null = null;

    const load = () => {
      controller?.abort();
      controller = new AbortController();
      dashboardApi
        .pulse(controller.signal)
        .then((result) => {
          if (!disposed) setItems(Array.isArray(result) ? result : []);
        })
        .catch(() => {
          // The strip is a convenience; leave the last counters in place.
        });
    };
    load();

    const timer = setInterval(() => {
      if (document.visibilityState === 'visible') load();
    }, PULSE_POLL_MS);
    const unsubscribe = bus?.subscribe(PULSE_TOPICS, () => {
      if (debounce) clearTimeout(debounce);
      debounce = setTimeout(load, 1500);
    });

    return () => {
      disposed = true;
      controller?.abort();
      clearInterval(timer);
      if (debounce) clearTimeout(debounce);
      unsubscribe?.();
    };
  }, [bus]);

  if (!items || items.length === 0) {
    return null;
  }

  return (
    <nav className="rg-pulse" aria-label={t('Dash.PulseLabel')}>
      {items.map((item) => (
        <a key={item.Key} href={item.Url || '#'} className={item.Tone ? `rg-tone-${item.Tone}` : undefined}>
          <i className={`fa ${item.Icon}`} aria-hidden="true" />
          <b>{new Intl.NumberFormat(uiLocale()).format(item.Value)}</b> {item.Label}
        </a>
      ))}
    </nav>
  );
}
