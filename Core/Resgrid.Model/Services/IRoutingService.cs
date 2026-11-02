using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Routes, travel-time matrices and stop-order optimization through the install's configured routing provider
	/// (<c>MappingConfig.RoutingProvider</c>), with the fallback provider, timeouts and caching applied. Every server
	/// path that needs a drive time or a route goes through here.
	/// </summary>
	public interface IRoutingService
	{
		/// <summary>The provider requests go to first (<see cref="RoutingProviderNames"/>).</summary>
		string ProviderName { get; }

		RoutingCapabilities Capabilities { get; }

		/// <summary>Attribution text for routes from the active provider.</summary>
		string Attribution { get; }

		/// <summary>In-app navigation is allowed: enabled in config and the provider is Valhalla or Mapbox (plan D26).</summary>
		bool IsNavigationAvailable { get; }

		Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken = default);

		Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken = default);

		/// <summary>Best order for the waypoints; uses the provider's optimizer when it can, else a matrix-based heuristic.</summary>
		Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken = default);
	}
}
