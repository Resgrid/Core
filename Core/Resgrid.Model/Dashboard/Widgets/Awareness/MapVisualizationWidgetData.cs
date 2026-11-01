using System;

namespace Resgrid.Model.Dashboard.Widgets.Awareness
{
	/// <summary>
	/// map.visualization (plan §7.4): one saved map visualization run under the viewer's permissions. The result is the
	/// Mapping → Visualizations run shape (rg-map-visualizer's VizMap renders it; category/band keys are localized
	/// client-side from the MapVisualizations strings).
	/// </summary>
	public sealed class MapVisualizationWidgetData
	{
		/// <summary>False until the member picks a visualization in the widget settings.</summary>
		public bool Configured { get; set; }

		/// <summary>False when the picked visualization was deleted or is no longer visible to the member.</summary>
		public bool Found { get; set; }

		public string MapVisualizationId { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public string TemplateKey { get; set; }

		public bool IsShared { get; set; }

		public DateTime UpdatedOnUtc { get; set; }

		/// <summary>Opens the visualization on the Mapping → Visualizations page.</summary>
		public string Url { get; set; }

		public MapVisualizationResult Result { get; set; }
	}
}
