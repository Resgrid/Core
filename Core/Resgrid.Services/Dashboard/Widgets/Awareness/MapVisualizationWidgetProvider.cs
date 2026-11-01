using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Awareness;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Awareness
{
	/// <summary>
	/// map.visualization (plan §7.4): one saved visualization, re-checked against the department and the member on every
	/// call (the setting is a posted id), then run through the module's own RunAsync — which reads under the viewer's
	/// dispatch scope and visibility matrix. Nothing is cached: the run is per viewer.
	/// </summary>
	[DashboardWidget("map.visualization")]
	public class MapVisualizationWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IMapVisualizationService _mapVisualizationService;

		public MapVisualizationWidgetProvider(IMapVisualizationService mapVisualizationService)
		{
			_mapVisualizationService = mapVisualizationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var id = context.GetString("visualization");
			if (string.IsNullOrWhiteSpace(id))
				return new MapVisualizationWidgetData { Configured = false };

			var visualization = await _mapVisualizationService.GetVisibleByIdAsync(context.DepartmentId, context.UserId, id);

			// GetVisibleByIdAsync already scopes to the department and the member; the second check keeps a loose or future
			// implementation from widening it.
			if (visualization == null || visualization.DepartmentId != context.DepartmentId || !visualization.IsVisibleTo(context.UserId))
				return new MapVisualizationWidgetData { Configured = true, Found = false };

			var definition = visualization.Definition ?? MapVisualizationDefinitionValidator.Normalize(null);
			var result = await _mapVisualizationService.RunAsync(context.DepartmentId, context.UserId, definition, cancellationToken);

			return new MapVisualizationWidgetData
			{
				Configured = true,
				Found = true,
				MapVisualizationId = visualization.MapVisualizationId,
				Name = visualization.Name,
				Description = visualization.Description,
				TemplateKey = visualization.TemplateKey,
				IsShared = visualization.IsShared,
				UpdatedOnUtc = visualization.UpdatedOn,
				Url = "/User/MapVisualizations?id=" + Uri.EscapeDataString(visualization.MapVisualizationId),
				Result = result
			};
		}
	}
}
