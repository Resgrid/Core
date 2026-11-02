using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// One routing engine (Valhalla, Mapbox, Google, OSRM, or the straight-line estimate). Implementations live in
	/// Resgrid.Providers.Geo and are chosen per install by <c>MappingConfig.RoutingProvider</c> through
	/// <see cref="Services.IRoutingService"/>; nothing else calls them directly.
	/// </summary>
	public interface IRoutingProvider
	{
		/// <summary>A <see cref="RoutingProviderNames"/> value.</summary>
		string Name { get; }

		RoutingCapabilities Capabilities { get; }

		/// <summary>True when the install has what this provider needs (URL, key).</summary>
		bool IsConfigured { get; }

		Task<RouteResult> GetRouteAsync(RouteRequest request, CancellationToken cancellationToken);

		Task<RouteMatrixResult> GetMatrixAsync(RouteMatrixRequest request, CancellationToken cancellationToken);

		Task<RouteOptimizationResult> OptimizeAsync(RouteOptimizationRequest request, CancellationToken cancellationToken);
	}
}
