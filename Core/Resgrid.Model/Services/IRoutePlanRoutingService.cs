using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Server-side routing for route plans (maplibre-mapping-routing-navigation-plan.md §10.4): generates and stores a
	/// plan's route through the install's routing provider, proposes and applies optimized stop orders, and routes a
	/// running instance from the unit's position through its remaining stops.
	/// </summary>
	public interface IRoutePlanRoutingService
	{
		/// <summary>
		/// Routes the plan (resolved start, ordered stops, end) and stores geometry, totals, per-leg values, planned
		/// offsets and directions. A straight-line estimate is returned but never stored.
		/// </summary>
		Task<RoutePlanRouteResult> GenerateAsync(string routePlanId, CancellationToken cancellationToken = default);

		/// <summary>The stored route when it is current for the plan's inputs; otherwise regenerates it.</summary>
		Task<RoutePlanRouteResult> GetCurrentRouteAsync(string routePlanId, CancellationToken cancellationToken = default);

		/// <summary>The best stop order with the start and end fixed, and the totals before and after; writes nothing.</summary>
		Task<RoutePlanOptimizationPreview> PreviewOptimizationAsync(string routePlanId, CancellationToken cancellationToken = default);

		/// <summary>Renumbers the plan's stops 1..n in the given order, then regenerates the route.</summary>
		Task<RoutePlanRouteResult> ApplyOrderAsync(string routePlanId, IList<string> orderedStopIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// Route from the unit's position through the instance's remaining (pending or checked-in) stops to the plan end,
		/// with an ETA per stop (now + legs + dwell). Only the instance's LastRoutedOn is written.
		/// </summary>
		Task<RoutePlanRouteResult> GetLiveDirectionsAsync(string routeInstanceId, double latitude, double longitude, double? heading,
			bool navigation, string language, bool metric, CancellationToken cancellationToken = default);
	}
}
