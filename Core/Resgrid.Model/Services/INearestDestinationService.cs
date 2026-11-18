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

		/// <summary>Travel distance along the route (straight line for air travel); -1 when no travel time was looked up.</summary>
		public double TravelDistanceMeters { get; set; } = -1;

		/// <summary>The destination's current availability (Open when it has no current report).</summary>
		public PoiAvailabilityStatuses Status { get; set; }

		/// <summary>
		/// True when it can take the patient now: not closed and, with a capability asked for, that capability not reported
		/// unavailable (<see cref="PoiAvailabilityRules.CanProvide"/>). A destination on diversion still can.
		/// </summary>
		public bool CanProvide { get; set; }

		/// <summary>True when the destination offers every required capability (always true without requirements).</summary>
		public bool MeetsRequirements { get; set; } = true;

		/// <summary>Required capabilities the destination does not offer.</summary>
		public List<string> MissingCapabilityIds { get; set; } = new List<string>();

		/// <summary>Preferred capabilities the destination offers and has not reported unavailable.</summary>
		public List<string> PreferredCapabilityIds { get; set; } = new List<string>();

		/// <summary>
		/// The ranking group (<see cref="DestinationTiers"/>): 0 meets the requirements and can take the patient, 1 the same
		/// but on diversion, 2 misses or has a required capability down, 3 closed.
		/// </summary>
		public int Tier { get; set; }
	}

	/// <summary>Ranking groups of <see cref="NearestDestination.Tier"/>, best first.</summary>
	public static class DestinationTiers
	{
		public const int Ready = 0;
		public const int Diversion = 1;
		public const int Lacking = 2;
		public const int Closed = 3;
	}

	/// <summary>What to suggest a destination for (<see cref="INearestDestinationService.SuggestDestinationsAsync"/>).</summary>
	public class DestinationSuggestionRequest
	{
		public int DepartmentId { get; set; }

		/// <summary>The origin: the incident location, or the unit's position.</summary>
		public double Latitude { get; set; }

		public double Longitude { get; set; }

		/// <summary>The call's type id, when known; else <see cref="CallType"/> is matched by name.</summary>
		public int? CallTypeId { get; set; }

		/// <summary>The call's type name (as <see cref="Call.Type"/> stores it).</summary>
		public string CallType { get; set; }

		/// <summary>The call's priority (<see cref="Call.Priority"/>).</summary>
		public int? CallPriority { get; set; }

		/// <summary>The travelling unit: its type's rules, travel mode and vehicle limits apply. Null for the call's own destination.</summary>
		public int? UnitId { get; set; }

		/// <summary>How many destinations to return (1 to <see cref="INearestDestinationService.MaximumResults"/>).</summary>
		public int MaxResults { get; set; } = 10;
	}

	/// <summary>The suggested destinations for a call or unit.</summary>
	public class DestinationSuggestions
	{
		/// <summary>The department's setting; Off returns no destinations (nothing was routed).</summary>
		public DestinationSuggestionModes Mode { get; set; }

		public int PreferredWithinMinutes { get; set; }

		/// <summary>The context the rules were matched against.</summary>
		public DestinationContext Context { get; set; } = new DestinationContext();

		public DestinationRequirements Requirements { get; set; } = DestinationRequirements.None;

		/// <summary>Ranked best first (tier, then drive time, with the preferred near-tie rule).</summary>
		public List<NearestDestination> Destinations { get; set; } = new List<NearestDestination>();

		/// <summary>The best destination that meets the requirements and is not closed; null when none does.</summary>
		public int? SuggestedPoiId { get; set; }
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

		/// <summary>
		/// Suggests destinations from the department's <see cref="DestinationRule"/>s for the call (and unit): destinations of the
		/// allowed types, those meeting the requirements first (on diversion after them, missing a capability next, closed last),
		/// then by drive time; within <see cref="DestinationSuggestionConfig.PreferredWithinMinutes"/> of a group's fastest, more
		/// preferred capabilities win. Nothing is routed while the department's setting is Off.
		/// </summary>
		Task<DestinationSuggestions> SuggestDestinationsAsync(DestinationSuggestionRequest request, CancellationToken cancellationToken = default(CancellationToken));
	}
}
