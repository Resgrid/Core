import { getRamp } from './palettes';
import { categoryLabel, formatMetric, t } from './vizI18n';
import type { LayerRender } from './vizRender';
import type { VizDefinition, VizLayerResult } from './vizTypes';

interface VizLegendProps {
  definition: VizDefinition;
  renders: LayerRender[];
  results: VizLayerResult[];
  onToggle: (layerId: string, visible: boolean) => void;
}

export default function VizLegend(props: VizLegendProps) {
  const { definition } = props;

  return (
    <div className="rg-viz__legend" aria-label={t('LegendTitle')}>
      {[...definition.Layers].reverse().map((layer) => {
        const render = props.renders.find((r) => r.layerId === layer.Id);
        const result = props.results.find((r) => r.LayerId === layer.Id);
        const legend = render?.legend ?? { kind: 'none' as const };

        return (
          <div key={layer.Id} className="rg-viz__legend-layer">
            <label className="rg-viz__check rg-viz__legend-head">
              <input type="checkbox" checked={layer.Visible} onChange={(event) => props.onToggle(layer.Id, event.target.checked)} />
              <span>{layer.Name || t(`Source_${layer.Source}`)}</span>
            </label>

            {layer.Visible && legend.kind === 'ramp' && (
              <div className="rg-viz__legend-ramp">
                <span>{t(layer.Metric === 'count' ? 'LegendFewer' : 'LegendLower')}</span>
                <span
                  className="rg-viz__ramp"
                  style={{ background: `linear-gradient(to right, ${getRamp(layer.Style.Palette).join(', ')})` }}
                  aria-hidden="true"
                />
                <span>{t(layer.Metric === 'count' ? 'LegendMore' : 'LegendHigher')}</span>
              </div>
            )}

            {layer.Visible && legend.kind === 'classes' && (
              <ul className="rg-viz__legend-list">
                {legend.items.map((item, index) => (
                  <li key={index}>
                    <span className="rg-viz__swatch" style={{ backgroundColor: item.color }} aria-hidden="true" />
                    <span>
                      {item.from === null
                        ? t('LegendUpTo', formatMetric(item.to, layer.Metric, definition.Options))
                        : t('LegendRange', formatMetric(item.from, layer.Metric, definition.Options), formatMetric(item.to, layer.Metric, definition.Options))}
                    </span>
                  </li>
                ))}
              </ul>
            )}

            {layer.Visible && legend.kind === 'categories' && (
              <>
                {legend.note === 'dominant' && <p className="rg-viz__hint">{t('LegendDominant')}</p>}
                <ul className="rg-viz__legend-list">
                  {legend.items.map((item, index) => (
                    <li key={index}>
                      <span className="rg-viz__swatch" style={{ backgroundColor: item.color }} aria-hidden="true" />
                      <span className="rg-viz__legend-label">
                        {categoryLabel({ Key: result?.Categories[index]?.Key ?? '', Label: item.label }, layer.ColorBy, definition.Options)}
                      </span>
                      <span className="rg-viz__legend-count">{item.count.toLocaleString()}</span>
                    </li>
                  ))}
                </ul>
              </>
            )}

            {layer.Visible && result && result.Points.length === 0 && result.Tracks.length === 0 && (
              <p className="rg-viz__hint">{t('LegendEmpty')}</p>
            )}
          </div>
        );
      })}
    </div>
  );
}
