using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>One destination in a nearest-destination ranking (<see cref="INearestDestinationService"/>).</summary>
	public class NearestDestination
	{
		/// <summary>The destination, with its capabilities and current availability filled (<see cref="Poi.Type"/> set).</summary>
		public Poi Poi { get; set; }

		/// <summary>Straight-line distance from the origin.</summary>
		public double DistanceMeters { get; set; }

		/// <summary>Travel seconds from the origin; -1 when no travel time was looked up (beyond the shortlist, or the lookup failed).</summary>
		public double TravelSeconds { get; set; } = -1;

		/// <summary>The destination's current availability (Open when it has no current report).</summary>
		public PoiAvailabilityStatuses Status { get; set; }

		/// <summary>
		/// True when it can take the patient now: not closed and, with a capability asked for, that capability not reported
		/// unavailable (<see cref="PoiAvailabilityRules.CanProvide"/>). A destination on diversion still can.
		/// </summary>
		public bool CanProvide { get; set; }
	}

	/// <summary>
	/// Ranks a department's destination POIs from a point (a unit's position or a call's location), optionally only those
	/// offering a capability: the closest by straight line get routed travel times (the dispatch shortlist size), then
	/// destinations that can take the patient come first, diversion after them, closed or capability-down last; within each,
	/// routed times before straight-line estimates.
	/// </summary>
	public interface INearestDestinationService
	{
		/// <summary>The default number of destinations routed.</summary>
		const int DefaultShortlistSize = 5;

		/// <summary>The most destinations one ranking returns.</summary>
		const int MaximumResults = 50;

		/// <param name="departmentId">The department.</param>
		/// <param name="latitude">Origin latitude.</param>
		/// <param name="longitude">Origin longitude.</param>
		/// <param name="poiCapabilityId">Only destinations offering this capability; null or blank for all.</param>
		/// <param name="unitId">The unit travelling, so its travel mode and vehicle limits apply; null routes plain driving.</param>
		/// <param name="maxResults">How many to return (1 to <see cref="MaximumResults"/>).</param>
		/// <param name="cancellationToken">Cancels the travel time lookups.</param>
		Task<List<NearestDestination>> GetNearestDestinationsAsync(int departmentId, double latitude, double longitude, string poiCapabilityId, int? unitId,
			int maxResults, CancellationToken cancellationToken = default(CancellationToken));
	}
}
