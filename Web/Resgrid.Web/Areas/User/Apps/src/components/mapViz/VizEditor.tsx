import { useEffect, useMemo, useRef, useState } from 'react';
import { searchCalls, type CallSearchHit } from './vizApi';
import { fitLayerToSource, newLayer, newLayerId, parseNumberList, windowChoices } from './vizDefaults';
import { t } from './vizI18n';
import type {
  VizAggregate,
  VizBootstrap,
  VizColorBy,
  VizDefinition,
  VizDisplay,
  VizFilters,
  VizLayer,
  VizMetric,
  VizOption,
  VizOptions,
  VizPalette,
  VizSource,
} from './vizTypes';

interface VizEditorProps {
  definition: VizDefinition;
  bootstrap: VizBootstrap;
  focusLabel: string | null;
  onChange: (definition: VizDefinition) => void;
}

const sources: VizSource[] = ['calls', 'unitResponses', 'personnelResponses', 'responderOrigins', 'unitStatusLocations', 'unitTracks', 'personnelTracks'];
const aggregates: VizAggregate[] = ['count', 'sum', 'mean', 'median', 'p90', 'max'];
const palettes: VizPalette[] = ['heat', 'fire', 'viridis', 'blues', 'traffic'];
const hours = Array.from({ length: 24 }, (_, hour) => hour);
// Monday first; values are .NET DayOfWeek (0 = Sunday).
const days = [1, 2, 3, 4, 5, 6, 0];

function MultiPick(props: {
  label: string;
  options: VizOption[];
  selected: string[];
  onChange: (values: string[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const visible = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    return needle.length === 0 ? props.options : props.options.filter((o) => o.Label.toLowerCase().includes(needle));
  }, [filter, props.options]);

  const summary = props.selected.length === 0
    ? t('FilterAll')
    : props.selected.length === 1
      ? props.options.find((o) => o.Value === props.selected[0])?.Label ?? t('FilterSelected', 1)
      : t('FilterSelected', props.selected.length);

  const toggle = (value: string) =>
    props.onChange(props.selected.includes(value) ? props.selected.filter((v) => v !== value) : [...props.selected, value]);

  return (
    <div className="rg-viz__field">
      <span className="rg-viz__label">{props.label}</span>
      <button type="button" className="rg-viz__pick" aria-expanded={open} onClick={() => setOpen(!open)} disabled={props.options.length === 0}>
        <span>{props.options.length === 0 ? t('FilterNoChoices') : summary}</span>
        <span aria-hidden="true">{open ? '▴' : '▾'}</span>
      </button>
      {open && (
        <div className="rg-viz__pick-list">
          {props.options.length > 8 && (
            <input
              className="rg-viz__input"
              type="search"
              value={filter}
              placeholder={t('FilterSearch')}
              aria-label={t('FilterSearch')}
              onChange={(event) => setFilter(event.target.value)}
            />
          )}
          <div className="rg-viz__pick-actions">
            <button type="button" className="rg-viz__link" onClick={() => props.onChange([])}>{t('FilterClear')}</button>
          </div>
          {visible.map((option) => (
            <label key={option.Value} className="rg-viz__check">
              <input type="checkbox" checked={props.selected.includes(option.Value)} onChange={() => toggle(option.Value)} />
              <span>{option.Label}</span>
            </label>
          ))}
        </div>
      )}
    </div>
  );
}

function CallPicker(props: { callId: number | null; label: string | null; onChange: (callId: number | null) => void }) {
  const [term, setTerm] = useState('');
  const [hits, setHits] = useState<CallSearchHit[]>([]);
  const [searching, setSearching] = useState(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (timer.current) {
      clearTimeout(timer.current);
    }

    if (term.trim().length < 2) {
      setHits([]);
      return undefined;
    }

    const controller = new AbortController();
    timer.current = setTimeout(() => {
      setSearching(true);
      searchCalls(term.trim(), controller.signal)
        .then((result) => setHits(result))
        .catch(() => setHits([]))
        .finally(() => setSearching(false));
    }, 300);

    return () => controller.abort();
  }, [term]);

  if (props.callId) {
    return (
      <div className="rg-viz__field">
        <span className="rg-viz__label">{t('FilterCall')}</span>
        <div className="rg-viz__chip-row">
          <span className="rg-viz__chip rg-viz__chip--active">{props.label ?? `#${props.callId}`}</span>
          <button type="button" className="rg-viz__link" onClick={() => props.onChange(null)}>{t('FilterCallClear')}</button>
        </div>
        <p className="rg-viz__hint">{t('FilterCallActiveHint')}</p>
      </div>
    );
  }

  return (
    <div className="rg-viz__field">
      <label className="rg-viz__label" htmlFor="rg-viz-call-search">{t('FilterCall')}</label>
      <input
        id="rg-viz-call-search"
        className="rg-viz__input"
        type="search"
        value={term}
        placeholder={t('FilterCallPlaceholder')}
        onChange={(event) => setTerm(event.target.value)}
      />
      {searching && <p className="rg-viz__hint">{t('Searching')}</p>}
      {hits.length > 0 && (
        <ul className="rg-viz__hits">
          {hits.map((hit) => (
            <li key={hit.CallId}>
              <button type="button" className="rg-viz__hit" onClick={() => { props.onChange(hit.CallId); setTerm(''); setHits([]); }}>
                {hit.Label}
              </button>
            </li>
          ))}
        </ul>
      )}
      {!searching && term.trim().length >= 2 && hits.length === 0 && <p className="rg-viz__hint">{t('FilterCallNoMatches')}</p>}
      <p className="rg-viz__hint">{t('FilterCallHint')}</p>
    </div>
  );
}

function NumberListInput(props: { id: string; label: string; hint: string; values: number[]; onChange: (values: number[]) => void }) {
  const [text, setText] = useState(props.values.join(', '));
  useEffect(() => setText(props.values.join(', ')), [props.values]);

  return (
    <div className="rg-viz__field">
      <label className="rg-viz__label" htmlFor={props.id}>{props.label}</label>
      <input
        id={props.id}
        className="rg-viz__input"
        type="text"
        inputMode="decimal"
        value={text}
        onChange={(event) => setText(event.target.value)}
        onBlur={() => props.onChange(parseNumberList(text))}
      />
      <p className="rg-viz__hint">{props.hint}</p>
    </div>
  );
}

function LayerCard(props: {
  layer: VizLayer;
  index: number;
  count: number;
  bootstrap: VizBootstrap;
  onChange: (layer: VizLayer) => void;
  onRemove: () => void;
  onMove: (delta: number) => void;
}) {
  const { layer, bootstrap } = props;
  const options = bootstrap.SourceOptions[layer.Source] ?? { Displays: ['heat'], ColorBys: ['none'], Metrics: ['count'] };
  const set = (patch: Partial<VizLayer>) => props.onChange(fitLayerToSource({ ...layer, ...patch }, bootstrap));
  const setStyle = (patch: Partial<VizLayer['Style']>) => props.onChange({ ...layer, Style: { ...layer.Style, ...patch } });
  const idBase = `rg-viz-${layer.Id}`;
  const showAggregate = layer.Display === 'hexbins' && layer.Metric !== 'count';
  const continuous = layer.ColorBy === 'none';

  return (
    <fieldset className="rg-viz__layer">
      <legend className="rg-viz__layer-head">
        <label className="rg-viz__check">
          <input type="checkbox" checked={layer.Visible} onChange={(event) => props.onChange({ ...layer, Visible: event.target.checked })} />
          <span className="rg-viz__layer-title">{layer.Name || t(`Source_${layer.Source}`)}</span>
        </label>
      </legend>
      <div className="rg-viz__layer-tools">
        <button type="button" className="rg-viz__icon-button" onClick={() => props.onMove(-1)} disabled={props.index === 0} title={t('LayerDown')} aria-label={t('LayerDown')}>↓</button>
        <button type="button" className="rg-viz__icon-button" onClick={() => props.onMove(1)} disabled={props.index === props.count - 1} title={t('LayerUp')} aria-label={t('LayerUp')}>↑</button>
        <button type="button" className="rg-viz__icon-button" onClick={props.onRemove} disabled={props.count <= 1} title={t('LayerRemove')} aria-label={t('LayerRemove')}>✕</button>
      </div>

      <div className="rg-viz__grid">
        <div className="rg-viz__field rg-viz__field--wide">
          <label className="rg-viz__label" htmlFor={`${idBase}-name`}>{t('LayerName')}</label>
          <input
            id={`${idBase}-name`}
            className="rg-viz__input"
            type="text"
            maxLength={100}
            value={layer.Name ?? ''}
            placeholder={t(`Source_${layer.Source}`)}
            onChange={(event) => props.onChange({ ...layer, Name: event.target.value || null })}
          />
        </div>
        <div className="rg-viz__field rg-viz__field--wide">
          <label className="rg-viz__label" htmlFor={`${idBase}-source`}>{t('LayerSource')}</label>
          <select id={`${idBase}-source`} className="rg-viz__input" value={layer.Source} onChange={(event) => set({ Source: event.target.value as VizSource })}>
            {sources.map((source) => <option key={source} value={source}>{t(`Source_${source}`)}</option>)}
          </select>
          <p className="rg-viz__hint">{t(`SourceHint_${layer.Source}`)}</p>
        </div>
        <div className="rg-viz__field">
          <label className="rg-viz__label" htmlFor={`${idBase}-display`}>{t('LayerDisplay')}</label>
          <select id={`${idBase}-display`} className="rg-viz__input" value={layer.Display} onChange={(event) => set({ Display: event.target.value as VizDisplay })}>
            {options.Displays.map((display) => <option key={display} value={display}>{t(`Display_${display}`)}</option>)}
          </select>
        </div>
        {layer.Display !== 'heat' && (
          <div className="rg-viz__field">
            <label className="rg-viz__label" htmlFor={`${idBase}-color`}>{t('LayerColorBy')}</label>
            <select id={`${idBase}-color`} className="rg-viz__input" value={layer.ColorBy} onChange={(event) => set({ ColorBy: event.target.value as VizColorBy })}>
              {options.ColorBys.map((colorBy) => <option key={colorBy} value={colorBy}>{t(`ColorBy_${colorBy}`)}</option>)}
            </select>
          </div>
        )}
        {layer.Display !== 'tracks' && (
          <div className="rg-viz__field">
            <label className="rg-viz__label" htmlFor={`${idBase}-metric`}>{t(layer.Display === 'heat' ? 'LayerWeight' : layer.Display === 'points' ? 'LayerSize' : 'LayerMetric')}</label>
            <select id={`${idBase}-metric`} className="rg-viz__input" value={layer.Metric} onChange={(event) => set({ Metric: event.target.value as VizMetric })}>
              {options.Metrics.map((metric) => <option key={metric} value={metric}>{t(`Metric_${metric}`)}</option>)}
            </select>
          </div>
        )}
        {showAggregate && (
          <div className="rg-viz__field">
            <label className="rg-viz__label" htmlFor={`${idBase}-aggregate`}>{t('LayerAggregate')}</label>
            <select id={`${idBase}-aggregate`} className="rg-viz__input" value={layer.Aggregate} onChange={(event) => props.onChange({ ...layer, Aggregate: event.target.value as VizAggregate })}>
              {aggregates.filter((a) => a !== 'count').map((aggregate) => <option key={aggregate} value={aggregate}>{t(`Aggregate_${aggregate}`)}</option>)}
            </select>
          </div>
        )}
        {(layer.Display === 'heat' || (continuous && layer.Display !== 'tracks')) && (
          <div className="rg-viz__field">
            <label className="rg-viz__label" htmlFor={`${idBase}-palette`}>{t('LayerPalette')}</label>
            <select id={`${idBase}-palette`} className="rg-viz__input" value={layer.Style.Palette} onChange={(event) => setStyle({ Palette: event.target.value as VizPalette })}>
              {palettes.map((palette) => <option key={palette} value={palette}>{t(`Palette_${palette}`)}</option>)}
            </select>
          </div>
        )}
        <div className="rg-viz__field">
          <label className="rg-viz__label" htmlFor={`${idBase}-opacity`}>{t('LayerOpacity')}</label>
          <input id={`${idBase}-opacity`} type="range" min={0.1} max={1} step={0.05} value={layer.Style.Opacity} onChange={(event) => setStyle({ Opacity: Number(event.target.value) })} />
        </div>
        {layer.Display === 'heat' && (
          <>
            <div className="rg-viz__field">
              <label className="rg-viz__label" htmlFor={`${idBase}-radius`}>{t('LayerRadius')}</label>
              <input id={`${idBase}-radius`} type="range" min={5} max={60} step={1} value={layer.Style.Radius} onChange={(event) => setStyle({ Radius: Number(event.target.value) })} />
            </div>
            <div className="rg-viz__field">
              <label className="rg-viz__label" htmlFor={`${idBase}-intensity`}>{t('LayerIntensity')}</label>
              <input id={`${idBase}-intensity`} type="range" min={0.2} max={5} step={0.1} value={layer.Style.Intensity} onChange={(event) => setStyle({ Intensity: Number(event.target.value) })} />
            </div>
          </>
        )}
        {layer.Display === 'hexbins' && (
          <div className="rg-viz__field">
            <label className="rg-viz__label" htmlFor={`${idBase}-cell`}>{t('LayerCellSize')}</label>
            <select id={`${idBase}-cell`} className="rg-viz__input" value={layer.Style.CellSizeMeters} onChange={(event) => setStyle({ CellSizeMeters: Number(event.target.value) })}>
              {[150, 250, 500, 800, 1000, 1600, 2500, 5000, 10000].map((size) => (
                <option key={size} value={size}>{size >= 1000 ? `${size / 1000} km` : `${size} m`}</option>
              ))}
            </select>
          </div>
        )}
        {layer.Display === 'points' && (
          <>
            <div className="rg-viz__field">
              <label className="rg-viz__label" htmlFor={`${idBase}-size`}>{t('LayerPointSize')}</label>
              <input id={`${idBase}-size`} type="range" min={2} max={20} step={1} value={layer.Style.PointSize} onChange={(event) => setStyle({ PointSize: Number(event.target.value) })} />
            </div>
            {layer.Metric !== 'count' && (
              <label className="rg-viz__check rg-viz__field--wide">
                <input type="checkbox" checked={layer.Style.SizeByMetric} onChange={(event) => setStyle({ SizeByMetric: event.target.checked })} />
                <span>{t('LayerSizeByMetric')}</span>
              </label>
            )}
          </>
        )}
      </div>
    </fieldset>
  );
}

export default function VizEditor(props: VizEditorProps) {
  const { definition, bootstrap } = props;
  const filters = definition.Filters;
  const options = definition.Options;

  const setFilters = (patch: Partial<VizFilters>) => props.onChange({ ...definition, Filters: { ...filters, ...patch } });
  const setOptions = (patch: Partial<VizOptions>) => props.onChange({ ...definition, Options: { ...options, ...patch } });
  const setLayers = (layers: VizLayer[]) => props.onChange({ ...definition, Layers: layers });

  const windowValue = filters.LastDays === null ? 'custom' : String(filters.LastDays);
  const knownWindow = windowChoices.some((choice) => choice.days !== null && String(choice.days) === windowValue);
  const toggleNumber = (list: number[], value: number) => (list.includes(value) ? list.filter((v) => v !== value) : [...list, value]);
  const today = new Date().toISOString().substring(0, 10);

  return (
    <div className="rg-viz__editor">
      <section className="rg-viz__section">
        <h3 className="rg-viz__section-title">{t('SectionWhen')}</h3>
        <div className="rg-viz__field">
          <label className="rg-viz__label" htmlFor="rg-viz-window">{t('FilterWindow')}</label>
          <select
            id="rg-viz-window"
            className="rg-viz__input"
            value={knownWindow || windowValue === 'custom' ? windowValue : String(filters.LastDays)}
            onChange={(event) => {
              if (event.target.value === 'custom') {
                const end = today;
                const start = new Date(Date.now() - 30 * 86400000).toISOString().substring(0, 10);
                setFilters({ LastDays: null, Start: filters.Start ?? start, End: filters.End ?? end });
              } else {
                setFilters({ LastDays: Number(event.target.value), Start: null, End: null });
              }
            }}
          >
            {!knownWindow && windowValue !== 'custom' && <option value={windowValue}>{t('WindowDays', windowValue)}</option>}
            {windowChoices.map((choice) => (
              <option key={choice.key} value={choice.days === null ? 'custom' : String(choice.days)}>{t(choice.key)}</option>
            ))}
          </select>
        </div>
        {filters.LastDays === null && (
          <div className="rg-viz__grid">
            <div className="rg-viz__field">
              <label className="rg-viz__label" htmlFor="rg-viz-start">{t('FilterStart')}</label>
              <input id="rg-viz-start" className="rg-viz__input" type="date" max={today} value={(filters.Start ?? '').substring(0, 10)} onChange={(event) => setFilters({ Start: event.target.value || null })} />
            </div>
            <div className="rg-viz__field">
              <label className="rg-viz__label" htmlFor="rg-viz-end">{t('FilterEnd')}</label>
              <input id="rg-viz-end" className="rg-viz__input" type="date" max={today} value={(filters.End ?? '').substring(0, 10)} onChange={(event) => setFilters({ End: event.target.value || null })} />
            </div>
          </div>
        )}
        <div className="rg-viz__field">
          <span className="rg-viz__label">{t('FilterDays')}</span>
          <div className="rg-viz__chip-row" role="group" aria-label={t('FilterDays')}>
            {days.map((day) => (
              <button
                key={day}
                type="button"
                className={`rg-viz__chip${filters.DaysOfWeek.includes(day) ? ' rg-viz__chip--active' : ''}`}
                aria-pressed={filters.DaysOfWeek.includes(day)}
                onClick={() => setFilters({ DaysOfWeek: toggleNumber(filters.DaysOfWeek, day) })}
              >
                {t(`DayShort_${day}`)}
              </button>
            ))}
          </div>
        </div>
        <div className="rg-viz__field">
          <span className="rg-viz__label">{t('FilterHours')}</span>
          <div className="rg-viz__chip-row rg-viz__chip-row--hours" role="group" aria-label={t('FilterHours')}>
            {hours.map((hour) => (
              <button
                key={hour}
                type="button"
                className={`rg-viz__chip rg-viz__chip--small${filters.Hours.includes(hour) ? ' rg-viz__chip--active' : ''}`}
                aria-pressed={filters.Hours.includes(hour)}
                onClick={() => setFilters({ Hours: toggleNumber(filters.Hours, hour) })}
              >
                {String(hour).padStart(2, '0')}
              </button>
            ))}
          </div>
          <p className="rg-viz__hint">{t('FilterHoursHint')}</p>
        </div>
      </section>

      <section className="rg-viz__section">
        <h3 className="rg-viz__section-title">{t('SectionWhich')}</h3>
        <CallPicker callId={filters.CallId} label={props.focusLabel} onChange={(callId) => setFilters({ CallId: callId })} />
        {!filters.CallId && (
          <>
            <MultiPick label={t('FilterCallTypes')} options={bootstrap.CallTypes} selected={filters.CallTypes} onChange={(values) => setFilters({ CallTypes: values })} />
            <MultiPick
              label={t('FilterPriorities')}
              options={bootstrap.Priorities}
              selected={filters.Priorities.map(String)}
              onChange={(values) => setFilters({ Priorities: values.map(Number) })}
            />
            <MultiPick
              label={t('FilterOutcomes')}
              options={bootstrap.Outcomes}
              selected={filters.Outcomes.map(String)}
              onChange={(values) => setFilters({ Outcomes: values.map(Number) })}
            />
            <MultiPick
              label={t('FilterGroups')}
              options={bootstrap.Groups}
              selected={filters.GroupIds.map(String)}
              onChange={(values) => setFilters({ GroupIds: values.map(Number) })}
            />
            <MultiPick
              label={t('FilterUnits')}
              options={bootstrap.Units}
              selected={filters.UnitIds.map(String)}
              onChange={(values) => setFilters({ UnitIds: values.map(Number) })}
            />
            <div className="rg-viz__field">
              <label className="rg-viz__label" htmlFor="rg-viz-repeat">{t('FilterMinCalls')}</label>
              <input
                id="rg-viz-repeat"
                className="rg-viz__input rg-viz__input--short"
                type="number"
                min={0}
                max={1000}
                value={filters.MinCallsAtLocation}
                onChange={(event) => setFilters({ MinCallsAtLocation: Math.min(1000, Math.max(0, Math.round(Number(event.target.value)) || 0)) })}
              />
              <p className="rg-viz__hint">{t('FilterMinCallsHint')}</p>
            </div>
          </>
        )}
      </section>

      <section className="rg-viz__section">
        <h3 className="rg-viz__section-title">{t('SectionLayers')}</h3>
        <p className="rg-viz__hint">{t('LayersHint')}</p>
        {[...definition.Layers].map((layer, index) => ({ layer, index })).reverse().map(({ layer, index }) => (
          <LayerCard
            key={layer.Id}
            layer={layer}
            index={index}
            count={definition.Layers.length}
            bootstrap={bootstrap}
            onChange={(next) => setLayers(definition.Layers.map((l) => (l.Id === layer.Id ? next : l)))}
            onRemove={() => setLayers(definition.Layers.filter((l) => l.Id !== layer.Id))}
            onMove={(delta) => {
              const target = index + delta;
              if (target < 0 || target >= definition.Layers.length) {
                return;
              }
              const layers = [...definition.Layers];
              [layers[index], layers[target]] = [layers[target], layers[index]];
              setLayers(layers);
            }}
          />
        ))}
        <button
          type="button"
          className="btn btn-default btn-sm"
          disabled={definition.Layers.length >= bootstrap.Limits.MaxLayers}
          onClick={() => setLayers([...definition.Layers, newLayer(newLayerId(definition))])}
        >
          {t('LayerAdd')}
        </button>
      </section>

      <section className="rg-viz__section">
        <h3 className="rg-viz__section-title">{t('SectionStations')}</h3>
        <label className="rg-viz__check">
          <input type="checkbox" checked={options.ShowStations} onChange={(event) => setOptions({ ShowStations: event.target.checked })} />
          <span>{t('OptionShowStations')}</span>
        </label>
        <div className="rg-viz__field">
          <label className="rg-viz__label" htmlFor="rg-viz-unit">{t('OptionDistanceUnit')}</label>
          <select id="rg-viz-unit" className="rg-viz__input rg-viz__input--short" value={options.DistanceUnit} onChange={(event) => setOptions({ DistanceUnit: event.target.value as 'km' | 'mi' })}>
            <option value="km">{t('UnitKilometers')}</option>
            <option value="mi">{t('UnitMiles')}</option>
          </select>
        </div>
        <NumberListInput id="rg-viz-rings" label={t('OptionRings')} hint={t('OptionRingsHint')} values={options.StationRings} onChange={(values) => setOptions({ StationRings: values.slice(0, 4) })} />
        <NumberListInput id="rg-viz-distance-bands" label={t('OptionDistanceBands')} hint={t('OptionDistanceBandsHint')} values={options.DistanceBands} onChange={(values) => setOptions({ DistanceBands: values.slice(0, 6) })} />
        <NumberListInput id="rg-viz-minute-bands" label={t('OptionMinuteBands')} hint={t('OptionMinuteBandsHint')} values={options.MinuteBands} onChange={(values) => setOptions({ MinuteBands: values.slice(0, 6) })} />
      </section>
    </div>
  );
}
