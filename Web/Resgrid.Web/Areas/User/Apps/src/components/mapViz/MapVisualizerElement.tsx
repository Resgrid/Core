import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import LoadingIndicator from '../shared/LoadingIndicator';
import {
  deleteVisualization,
  getBootstrap,
  getPointDetail,
  isForbidden,
  runVisualization,
  saveVisualization,
} from './vizApi';
import { cloneDefinition, dataSignature, defaultDefinition } from './vizDefaults';
import { categoryLabel, formatMetric, formatNumber, setStrings, t } from './vizI18n';
import { buildLayerRender, type DotRender, type HexRender, type LayerRender, type TrackRender } from './vizRender';
import type { VizBootstrap, VizDefinition, VizLayer, VizResult, VizSaved, VizTemplate } from './vizTypes';
import VizEditor from './VizEditor';
import VizLegend from './VizLegend';
import VizLibrary from './VizLibrary';
import VizMap from './VizMap';
import VizSummary from './VizSummary';
import './mapViz.css';

export interface MapVisualizerElementProps {
  strings?: Record<string, string> | null;
  tileUrl?: string;
  attribution?: string;
  initialId?: string;
  initialTemplate?: string;
  initialCallId?: number;
  mapHeight?: string;
  hostElement?: HTMLElement;
}

type Tab = 'library' | 'edit';

interface SaveDraft {
  name: string;
  description: string;
  isShared: boolean;
  asNew: boolean;
}

const RUN_DEBOUNCE_MS = 600;

function errorMessage(error: unknown): string {
  return isForbidden(error) ? t('ErrorForbidden') : t('ErrorLoad');
}

function element(tag: string, className: string, text?: string): HTMLElement {
  const node = document.createElement(tag);
  node.className = className;
  if (text !== undefined) {
    node.textContent = text;
  }
  return node;
}

function setUrl(params: Record<string, string | null>): void {
  try {
    const url = new URL(window.location.href);
    ['id', 'template', 'callId'].forEach((key) => url.searchParams.delete(key));
    Object.entries(params).forEach(([key, value]) => {
      if (value) {
        url.searchParams.set(key, value);
      }
    });
    window.history.replaceState(null, '', url.toString());
  } catch {
    // The address bar is a convenience; the page works without it.
  }
}

export default function MapVisualizerElement(props: MapVisualizerElementProps) {
  setStrings(props.strings);

  const rootRef = useRef<HTMLDivElement | null>(null);
  const [bootstrap, setBootstrap] = useState<VizBootstrap | null>(null);
  const [bootError, setBootError] = useState<string | null>(null);
  const [definition, setDefinition] = useState<VizDefinition>(() => defaultDefinition());
  const [activeKey, setActiveKey] = useState<string | null>(null);
  const [activeSaved, setActiveSaved] = useState<VizSaved | null>(null);
  const [activeTemplate, setActiveTemplate] = useState<VizTemplate | null>(null);
  const [dirty, setDirty] = useState(false);
  const [tab, setTab] = useState<Tab>('library');
  const [result, setResult] = useState<VizResult | null>(null);
  const [running, setRunning] = useState(false);
  const [runError, setRunError] = useState<string | null>(null);
  const [fitKey, setFitKey] = useState('');
  const [saveDraft, setSaveDraft] = useState<SaveDraft | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<string | null>(null);
  const [panelOpen, setPanelOpen] = useState(true);
  const runCounter = useRef(0);

  const signature = useMemo(() => dataSignature(definition), [definition]);
  const ready = bootstrap !== null;

  const flash = (message: string) => {
    setToast(message);
    window.setTimeout(() => setToast(null), 3500);
  };

  const openTemplate = useCallback((template: VizTemplate, callId?: number) => {
    const next = cloneDefinition(template.Definition);
    if (callId) {
      next.Filters.CallId = callId;
    }
    setDefinition(next);
    setActiveTemplate(template);
    setActiveSaved(null);
    setActiveKey(`template:${template.Key}`);
    setDirty(false);
    setUrl({ template: template.Key, callId: callId ? String(callId) : null });
    if (template.NeedsCall && !next.Filters.CallId) {
      setTab('edit');
    }
  }, []);

  const openSaved = useCallback((saved: VizSaved) => {
    setDefinition(cloneDefinition(saved.Definition));
    setActiveSaved(saved);
    setActiveTemplate(null);
    setActiveKey(`saved:${saved.MapVisualizationId}`);
    setDirty(false);
    setUrl({ id: saved.MapVisualizationId });
  }, []);

  // Library, saved list and editor choices.
  useEffect(() => {
    const controller = new AbortController();
    getBootstrap(controller.signal)
      .then((data) => {
        setBootstrap(data);

        const saved = props.initialId ? data.Saved.find((s) => s.MapVisualizationId === props.initialId) : undefined;
        const template = data.Templates.find((tpl) => tpl.Key === props.initialTemplate)
          ?? (props.initialCallId ? data.Templates.find((tpl) => tpl.Key === 'incident-footprint') : undefined);

        if (saved) {
          openSaved(saved);
        } else if (template) {
          openTemplate(template, props.initialCallId || undefined);
        } else if (data.Templates.length > 0) {
          openTemplate(data.Templates.find((tpl) => tpl.Key === 'call-hot-zones') ?? data.Templates[0]);
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setBootError(errorMessage(error));
        }
      });
    return () => controller.abort();
  }, [openSaved, openTemplate, props.initialCallId, props.initialId, props.initialTemplate]);

  // Fetch data whenever what the server computes changes (filters, sources, colors, metrics), debounced while editing.
  useEffect(() => {
    if (!ready) {
      return undefined;
    }

    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      const run = ++runCounter.current;
      setRunning(true);
      setRunError(null);
      runVisualization(definition, controller.signal)
        .then((data) => {
          if (run === runCounter.current) {
            setResult(data);
            setFitKey(`${signature}#${run}`);
          }
        })
        .catch((error: unknown) => {
          if (!controller.signal.aborted && run === runCounter.current) {
            setRunError(errorMessage(error));
          }
        })
        .finally(() => {
          if (run === runCounter.current) {
            setRunning(false);
          }
        });
    }, RUN_DEBOUNCE_MS);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
    // `definition` is read through the signature on purpose: style edits (and saving) must not refetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ready, signature]);

  const renders = useMemo<LayerRender[]>(() => {
    if (!result) {
      return [];
    }
    return definition.Layers
      .map((layer) => {
        const layerResult = result.Layers.find((r) => r.LayerId === layer.Id);
        return layerResult ? buildLayerRender(layer, layerResult) : null;
      })
      .filter((render): render is LayerRender => render !== null);
  }, [definition.Layers, result]);

  const changeDefinition = (next: VizDefinition) => {
    setDefinition(next);
    setDirty(true);
  };

  const toggleLayer = (layerId: string, visible: boolean) =>
    changeDefinition({ ...definition, Layers: definition.Layers.map((l) => (l.Id === layerId ? { ...l, Visible: visible } : l)) });

  const title = activeSaved?.Name ?? activeTemplate?.Name ?? t('Untitled');
  const canEditActive = !!activeSaved?.CanEdit;
  const focusLabel = result?.Call ? [result.Call.Number, result.Call.Name].filter(Boolean).join(' · ') : null;

  const formatHex = (layer: VizLayer, hex: HexRender): string => {
    const layerResult = result?.Layers.find((r) => r.LayerId === layer.Id);
    const parts = [t('TooltipRows', formatNumber(hex.cell.count, true))];
    if (layer.Metric !== 'count' && hex.cell.value !== null) {
      parts.push(`${t(`Aggregate_${layer.Aggregate}`)}: ${formatMetric(hex.cell.value, layer.Metric, definition.Options)}`);
    }
    if (layer.ColorBy !== 'none' && layerResult && hex.cell.dominantCategory >= 0) {
      const category = layerResult.Categories[hex.cell.dominantCategory];
      if (category) {
        parts.push(t('TooltipDominant', categoryLabel(category, layer.ColorBy, definition.Options), Math.round(hex.cell.dominantShare * 100)));
      }
    }
    return parts.map((part) => part.replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[c] as string)).join('<br/>');
  };

  const formatTrack = (layer: VizLayer, track: TrackRender): string => {
    const escape = (value: string) => value.replace(/[&<>]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[c] as string);
    const range = track.first !== null && track.last !== null
      ? `${new Date(track.first * 1000).toLocaleString()} – ${new Date(track.last * 1000).toLocaleTimeString()}`
      : '';
    return `<strong>${escape(track.label)}</strong>${range ? `<br/>${escape(range)}` : ''}`;
  };

  const renderPointPopup = async (layer: VizLayer, dot: DotRender): Promise<HTMLElement> => {
    const container = element('div', 'rg-viz__popup');
    const layerResult = result?.Layers.find((r) => r.LayerId === layer.Id);
    const category = layerResult && dot.categoryIndex >= 0 ? layerResult.Categories[dot.categoryIndex] : null;

    if (category) {
      container.appendChild(element('div', 'rg-viz__popup-category', categoryLabel(category, layer.ColorBy, definition.Options)));
    }
    if (layer.Metric !== 'count') {
      container.appendChild(element('div', 'rg-viz__popup-metric', `${t(`Metric_${layer.Metric}`)}: ${formatMetric(dot.metric, layer.Metric, definition.Options)}`));
    }

    if (dot.callId > 0) {
      try {
        const detail = await getPointDetail(dot.callId);
        container.appendChild(element('div', 'rg-viz__popup-title', [detail.Number, detail.Name].filter(Boolean).join(' · ')));
        [detail.Type, detail.Priority, detail.State, detail.Address, detail.LoggedOn]
          .filter((value) => !!value)
          .forEach((value) => container.appendChild(element('div', 'rg-viz__popup-line', value)));
        if (detail.Url) {
          const link = element('a', 'rg-viz__popup-link', t('PopupOpenCall')) as HTMLAnchorElement;
          link.href = detail.Url;
          container.appendChild(link);
        }
        const focus = element('button', 'rg-viz__link', t('PopupFocusCall')) as HTMLButtonElement;
        focus.type = 'button';
        focus.addEventListener('click', () => {
          const footprint = bootstrap?.Templates.find((tpl) => tpl.Key === 'incident-footprint');
          if (footprint) {
            openTemplate(footprint, dot.callId);
          }
        });
        container.appendChild(focus);
      } catch {
        container.appendChild(element('div', 'rg-viz__popup-line', t('PopupUnavailable')));
      }
    }

    return container;
  };

  const startSave = (asNew: boolean) => {
    setSaveError(null);
    setSaveDraft({
      name: asNew || !activeSaved ? (activeSaved ? t('CopyOf', activeSaved.Name) : activeTemplate?.Name ?? '') : activeSaved.Name,
      description: asNew || !activeSaved ? activeTemplate?.Description ?? activeSaved?.Description ?? '' : activeSaved.Description ?? '',
      isShared: !asNew && !!activeSaved?.IsShared,
      asNew: asNew || !activeSaved || !activeSaved.CanEdit,
    });
  };

  const submitSave = async () => {
    if (!saveDraft || !bootstrap) {
      return;
    }
    if (saveDraft.name.trim().length === 0) {
      setSaveError(t('SaveErrorNameRequired'));
      return;
    }

    setSaving(true);
    setSaveError(null);
    try {
      const response = await saveVisualization({
        MapVisualizationId: saveDraft.asNew ? null : activeSaved?.MapVisualizationId ?? null,
        Name: saveDraft.name.trim(),
        Description: saveDraft.description.trim() || null,
        IsShared: saveDraft.isShared,
        TemplateKey: activeTemplate?.Key ?? activeSaved?.TemplateKey ?? null,
        Definition: definition,
      });

      if (!response.Saved) {
        setSaveError(t(`SaveError${response.Error ?? 'Unknown'}`));
        return;
      }

      const saved = response.Saved;
      setBootstrap({
        ...bootstrap,
        Saved: [...bootstrap.Saved.filter((s) => s.MapVisualizationId !== saved.MapVisualizationId), saved]
          .sort((a, b) => a.Name.localeCompare(b.Name)),
      });
      setActiveSaved(saved);
      setActiveTemplate(null);
      setActiveKey(`saved:${saved.MapVisualizationId}`);
      setDirty(false);
      setSaveDraft(null);
      setUrl({ id: saved.MapVisualizationId });
      flash(t('Saved'));
    } catch (error) {
      setSaveError(errorMessage(error));
    } finally {
      setSaving(false);
    }
  };

  const removeSaved = async (saved: VizSaved) => {
    if (!bootstrap || !window.confirm(t('DeleteConfirm', saved.Name))) {
      return;
    }
    try {
      const response = await deleteVisualization(saved.MapVisualizationId);
      if (!response.Deleted) {
        flash(t('DeleteFailed'));
        return;
      }
      setBootstrap({ ...bootstrap, Saved: bootstrap.Saved.filter((s) => s.MapVisualizationId !== saved.MapVisualizationId) });
      if (activeSaved?.MapVisualizationId === saved.MapVisualizationId) {
        setActiveSaved(null);
        setActiveKey(null);
        setUrl({});
      }
      flash(t('Deleted'));
    } catch (error) {
      flash(errorMessage(error));
    }
  };

  const exportCsv = () => {
    if (!result) {
      return;
    }
    const quote = (value: string | number | null) => {
      let text = value === null ? '' : String(value);
      // Names come from department data; a leading = + - @ would run as a formula in a spreadsheet.
      if (typeof value === 'string' && /^[=+\-@\t\r]/.test(text)) {
        text = `'${text}`;
      }
      return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
    };
    const lines = [['layer', 'latitude', 'longitude', 'category', 'metric', 'value', 'call_id', 'track', 'time_utc'].join(',')];
    definition.Layers.filter((layer) => layer.Visible).forEach((layer) => {
      const layerResult = result.Layers.find((r) => r.LayerId === layer.Id);
      if (!layerResult) {
        return;
      }
      const name = layer.Name || t(`Source_${layer.Source}`);
      layerResult.Points.forEach((point) => {
        const category = point[2] >= 0 ? layerResult.Categories[point[2]] : null;
        lines.push([
          quote(name), point[0], point[1],
          quote(category ? categoryLabel(category, layer.ColorBy, definition.Options) : ''),
          layer.Metric, point[3] ?? '', point[4] || '', '', '',
        ].join(','));
      });
      layerResult.Tracks.forEach((track) => {
        track.Coordinates.forEach((coordinate) => {
          lines.push([
            quote(name), coordinate[0], coordinate[1], '', '', '', '', quote(track.Label), new Date(coordinate[2] * 1000).toISOString(),
          ].join(','));
        });
      });
    });

    const blob = new Blob([`﻿${lines.join('\n')}`], { type: 'text/csv;charset=utf-8' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `${title.replace(/[^\w\- ]+/g, '').trim() || 'map-visualization'}.csv`;
    link.click();
    window.setTimeout(() => URL.revokeObjectURL(link.href), 1000);
  };

  const copyLink = async () => {
    try {
      await navigator.clipboard.writeText(window.location.href);
      flash(t('LinkCopied'));
    } catch {
      flash(window.location.href);
    }
  };

  const toggleFullscreen = () => {
    const root = rootRef.current;
    if (!root) {
      return;
    }
    if (document.fullscreenElement) {
      void document.exitFullscreen();
    } else {
      void root.requestFullscreen?.();
    }
  };

  if (bootError) {
    return <div className="rg-error" role="alert">{bootError}</div>;
  }

  if (!bootstrap) {
    return <LoadingIndicator label={t('Loading')} />;
  }

  const mapHeight = props.mapHeight || 'calc(100vh - 260px)';

  return (
    <div ref={rootRef} className={`rg-viz${panelOpen ? '' : ' rg-viz--collapsed'}`}>
      <div className="rg-viz__toolbar">
        <div className="rg-viz__toolbar-title">
          <button
            type="button"
            className="rg-viz__icon-button"
            onClick={() => setPanelOpen(!panelOpen)}
            aria-expanded={panelOpen}
            aria-controls="rg-viz-panel"
            title={t(panelOpen ? 'PanelHide' : 'PanelShow')}
            aria-label={t(panelOpen ? 'PanelHide' : 'PanelShow')}
          >
            ☰
          </button>
          <h3>
            {title}
            {dirty && <span className="rg-viz__dirty" title={t('Unsaved')}> *</span>}
          </h3>
          {activeSaved?.IsShared && <span className="rg-viz__badge">{t('SavedSharedBadge')}</span>}
          {running && <span className="rg-viz__running" role="status">{t('Running')}</span>}
        </div>
        <div className="rg-viz__toolbar-actions">
          {canEditActive && (
            <button type="button" className="btn btn-primary btn-sm" onClick={() => startSave(false)} disabled={!dirty}>{t('Save')}</button>
          )}
          <button type="button" className={`btn btn-sm ${canEditActive ? 'btn-default' : 'btn-primary'}`} onClick={() => startSave(true)}>{t('SaveAs')}</button>
          <button type="button" className="btn btn-default btn-sm" onClick={exportCsv} disabled={!result}>{t('ExportCsv')}</button>
          <button type="button" className="btn btn-default btn-sm" onClick={() => void copyLink()} disabled={dirty || !activeKey}>{t('CopyLink')}</button>
          <button type="button" className="btn btn-default btn-sm" onClick={toggleFullscreen}>{t('Fullscreen')}</button>
        </div>
      </div>

      {saveDraft && (
        <div className="rg-viz__save rg-card" role="dialog" aria-labelledby="rg-viz-save-title">
          <h4 id="rg-viz-save-title">{saveDraft.asNew ? t('SaveAsTitle') : t('SaveTitle')}</h4>
          <div className="rg-viz__field">
            <label className="rg-viz__label" htmlFor="rg-viz-save-name">{t('SaveName')}</label>
            <input
              id="rg-viz-save-name"
              className="rg-viz__input"
              type="text"
              maxLength={bootstrap.Limits.MaxNameLength}
              value={saveDraft.name}
              onChange={(event) => setSaveDraft({ ...saveDraft, name: event.target.value })}
              autoFocus
            />
          </div>
          <div className="rg-viz__field">
            <label className="rg-viz__label" htmlFor="rg-viz-save-description">{t('SaveDescription')}</label>
            <textarea
              id="rg-viz-save-description"
              className="rg-viz__input"
              rows={2}
              maxLength={bootstrap.Limits.MaxDescriptionLength}
              value={saveDraft.description}
              onChange={(event) => setSaveDraft({ ...saveDraft, description: event.target.value })}
            />
          </div>
          {bootstrap.CanShare ? (
            <label className="rg-viz__check">
              <input type="checkbox" checked={saveDraft.isShared} onChange={(event) => setSaveDraft({ ...saveDraft, isShared: event.target.checked })} />
              <span>{t('SaveShare')}</span>
            </label>
          ) : (
            <p className="rg-viz__hint">{t('SavePersonalHint')}</p>
          )}
          {saveDraft.isShared && <p className="rg-viz__hint">{t('SaveShareHint')}</p>}
          {saveError && <div className="rg-error" role="alert">{saveError}</div>}
          <div className="rg-viz__save-actions">
            <button type="button" className="btn btn-primary btn-sm" onClick={() => void submitSave()} disabled={saving}>{t('Save')}</button>
            <button type="button" className="btn btn-default btn-sm" onClick={() => setSaveDraft(null)} disabled={saving}>{t('Cancel')}</button>
          </div>
        </div>
      )}

      {toast && <div className="rg-viz__toast" role="status">{toast}</div>}

      <div className="rg-viz__body">
        {panelOpen && (
          <aside id="rg-viz-panel" className="rg-viz__panel">
            <div className="rg-viz__tabs" role="tablist">
              <button type="button" role="tab" aria-selected={tab === 'library'} className={`rg-viz__tab${tab === 'library' ? ' rg-viz__tab--active' : ''}`} onClick={() => setTab('library')}>
                {t('TabLibrary')}
              </button>
              <button type="button" role="tab" aria-selected={tab === 'edit'} className={`rg-viz__tab${tab === 'edit' ? ' rg-viz__tab--active' : ''}`} onClick={() => setTab('edit')}>
                {t('TabCustomize')}
              </button>
            </div>
            <div className="rg-viz__panel-body" role="tabpanel">
              {tab === 'library' ? (
                <VizLibrary
                  templates={bootstrap.Templates}
                  saved={bootstrap.Saved}
                  activeKey={activeKey}
                  onOpenTemplate={(template) => openTemplate(template)}
                  onOpenSaved={openSaved}
                  onDeleteSaved={(saved) => void removeSaved(saved)}
                />
              ) : (
                <VizEditor definition={definition} bootstrap={bootstrap} focusLabel={focusLabel} onChange={changeDefinition} />
              )}
            </div>
          </aside>
        )}

        <div className="rg-viz__main">
          {runError && <div className="rg-error rg-viz__message" role="alert">{runError}</div>}
          <div className="rg-viz__map-wrap">
            {props.tileUrl ? (
              <VizMap
                tileUrl={props.tileUrl}
                attribution={props.attribution ?? ''}
                definition={definition}
                result={result}
                renders={renders}
                fitKey={fitKey}
                formatHex={formatHex}
                formatTrack={formatTrack}
                renderPointPopup={renderPointPopup}
                stationLabel={t('Station')}
                height={mapHeight}
              />
            ) : (
              <div className="rg-error">{t('ErrorNoTiles')}</div>
            )}
            {result && (
              <VizLegend definition={definition} renders={renders} results={result.Layers} onToggle={toggleLayer} />
            )}
            {running && !result && (
              <div className="rg-viz__overlay">
                <LoadingIndicator label={t('Running')} />
              </div>
            )}
          </div>
          {result && <VizSummary definition={definition} result={result} />}
        </div>
      </div>
    </div>
  );
}
