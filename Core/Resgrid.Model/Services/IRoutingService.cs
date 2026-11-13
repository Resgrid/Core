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

		/// <summary>
		/// The provider that answers first for a department (its own routing account when it has one, else the system
		/// primary): name, capabilities, attribution and whether in-app navigation is available. 0 = the system's.
		/// </summary>
		Task<RoutingProviderInfo> GetProviderInfoAsync(int departmentId);

		/// <summary>
		/// What a provider can do, by <see cref="RoutingProviderNames"/> name (e.g. the one that produced a stored route, which
		/// may be a fallback rather than the department's first provider). Empty capabilities for an unknown name.
		/// </summary>
		RoutingCapabilities CapabilitiesOf(string providerName);
	}
}
