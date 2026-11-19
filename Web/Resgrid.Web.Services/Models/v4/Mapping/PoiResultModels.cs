using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Mapping
{
	public class PoiTypeResultData
	{
		public int PoiTypeId { get; set; }
		public string Name { get; set; }
		public string Color { get; set; }
		public string ImagePath { get; set; }
		public string Marker { get; set; }
		public bool IsDestination { get; set; }

		/// <summary>
		/// The POI-specific custom icon image name.
		/// New app versions should use this field for POI type icons.
		/// ImagePath carries the bitmap-asset name resolved by PoiIconHelper for clients that ship PNGs.
		/// </summary>
		public string PoiImage { get; set; }

		/// <summary>
		/// Absolute URL of the department's uploaded icon for the POI type (a 128 px square PNG, anonymous GET,
		/// immutable); null when the type has no uploaded icon, so PoiImage / ImagePath stay the marker icon.
		/// </summary>
		public string IconUrl { get; set; }
	}

	public class PoiResultData
	{
		public int PoiId { get; set; }
		public int PoiTypeId { get; set; }
		public string PoiTypeName { get; set; }
		public string Name { get; set; }
		public string Address { get; set; }
		public string Note { get; set; }
		public double Latitude { get; set; }
		public double Longitude { get; set; }
		public string Color { get; set; }
		public string ImagePath { get; set; }
		public string Marker { get; set; }
		public bool IsDestination { get; set; }

		/// <summary>
		/// The POI-specific custom icon image name.
		/// New app versions should use this field for POI icons.
		/// ImagePath carries the bitmap-asset name resolved by PoiIconHelper for clients that ship PNGs.
		/// </summary>
		public string PoiImage { get; set; }

		/// <summary>
		/// Absolute URL of the department's uploaded icon for the POI type (a 128 px square PNG, anonymous GET,
		/// immutable); null when the type has no uploaded icon, so PoiImage / ImagePath stay the marker icon.
		/// </summary>
		public string IconUrl { get; set; }

		/// <summary>
		/// Ids of the department capabilities this POI offers (GetPoiCapabilities), in the department's order. Empty when it
		/// offers none, or on endpoints that do not fill capabilities.
		/// </summary>
		public List<string> CapabilityIds { get; set; } = new List<string>();

		/// <summary>The names of <see cref="CapabilityIds"/>, same order (e.g. "Cath Lab", "Trauma Center").</summary>
		public List<string> Capabilities { get; set; } = new List<string>();

		/// <summary>
		/// The destination's current availability (Resgrid.Model.PoiAvailabilityStatuses): 0 open, 1 limited, 2 diversion,
		/// 3 closed. An expired report reads as 0.
		/// </summary>
		public int AvailabilityStatus { get; set; }

		/// <summary>Capability ids the destination cannot provide right now (with a non-open <see cref="AvailabilityStatus"/>).</summary>
		public List<string> UnavailableCapabilityIds { get; set; } = new List<string>();

		/// <summary>What the hospital reported, e.g. "ED capacity overload, CT down".</summary>
		public string AvailabilityNote { get; set; }

		/// <summary>When the current report was set (UTC), or null when open.</summary>
		public System.DateTime? AvailabilitySetOnUtc { get; set; }

		/// <summary>When the current report lapses (UTC); null when open or when it lasts until changed.</summary>
		public System.DateTime? AvailabilityExpiresOnUtc { get; set; }
	}

	/// <summary>A capability the department's POIs can offer.</summary>
	public class PoiCapabilityResultData
	{
		public string PoiCapabilityId { get; set; }
		public string Name { get; set; }
		public int Sort { get; set; }
	}

	public class PoiCapabilitiesResult : StandardApiResponseV4Base
	{
		public List<PoiCapabilityResultData> Data { get; set; } = new List<PoiCapabilityResultData>();
	}

	/// <summary>One destination of GetNearestDestinations, best first.</summary>
	public class NearestDestinationResultData
	{
		/// <summary>The destination, with capabilities and availability.</summary>
		public PoiResultData Poi { get; set; }

		/// <summary>Straight-line distance from the origin.</summary>
		public double DistanceMeters { get; set; }

		/// <summary>Travel seconds from the origin; -1 when not looked up (beyond the shortlist, or the lookup failed).</summary>
		public double TravelSeconds { get; set; } = -1;

		/// <summary>True when it can take the patient now (not closed; the asked capability not reported unavailable).</summary>
		public bool CanProvide { get; set; }

		/// <summary>
		/// Travel distance along the route in metres (straight line for air travel); -1 when unavailable: not looked up (beyond
		/// the shortlist), the lookup failed, or the routing answer carried no usable distance.
		/// </summary>
		public double TravelDistanceMeters { get; set; } = -1;
	}

	public class NearestDestinationsResult : StandardApiResponseV4Base
	{
		public List<NearestDestinationResultData> Data { get; set; } = new List<NearestDestinationResultData>();
	}

	/// <summary>One destination of GetSuggestedDestinations, best first.</summary>
	public class SuggestedDestinationResultData : NearestDestinationResultData
	{
		/// <summary>Availability: 0 open, 1 limited, 2 diversion, 3 closed.</summary>
		public int Status { get; set; }

		/// <summary>True when it offers every required capability.</summary>
		public bool MeetsRequirements { get; set; }

		/// <summary>Required capabilities it does not offer.</summary>
		public List<string> MissingCapabilityIds { get; set; } = new List<string>();

		/// <summary>Preferred capabilities it offers (and has not reported unavailable).</summary>
		public List<string> PreferredCapabilityIds { get; set; } = new List<string>();

		/// <summary>0 meets the requirements and can take the patient, 1 the same on diversion, 2 misses or has a required capability down, 3 closed.</summary>
		public int Tier { get; set; }
	}

	/// <summary>A destination rule that applies (or was set aside).</summary>
	public class DestinationRuleRefResultData
	{
		public string DestinationRuleId { get; set; }

		public string Name { get; set; }

		/// <summary>Set-aside rules only: "TypesRemoved" (its destination types no longer exist) or "TypeConflict" (a more specific rule sends elsewhere).</summary>
		public string Reason { get; set; }
	}

	/// <summary>What the destination must be, from the department's destination rules.</summary>
	public class DestinationRequirementsResultData
	{
		/// <summary>The destination POI types allowed; empty for any destination type.</summary>
		public List<int> PoiTypeIds { get; set; } = new List<int>();

		public List<string> RequiredCapabilityIds { get; set; } = new List<string>();

		public List<string> PreferredCapabilityIds { get; set; } = new List<string>();

		/// <summary>The rules that apply, most specific first.</summary>
		public List<DestinationRuleRefResultData> Rules { get; set; } = new List<DestinationRuleRefResultData>();

		/// <summary>Rules that matched but did not apply, with the reason.</summary>
		public List<DestinationRuleRefResultData> SetAsideRules { get; set; } = new List<DestinationRuleRefResultData>();
	}

	public class SuggestedDestinationsResultData
	{
		/// <summary>
		/// The department's setting: 0 off (no destinations are returned, nothing is routed and <c>SuggestedPoiId</c> is null), 1
		/// suggest, 2 suggest and fill an empty destination.
		/// </summary>
		public int Mode { get; set; }

		/// <summary>
		/// A destination with more preferred capabilities wins only within this many minutes of the fastest destination in the
		/// same tier; a faster destination in another tier (on diversion, say) never sets the window.
		/// </summary>
		public int PreferredWithinMinutes { get; set; }

		/// <summary>
		/// The best destination of tier 0 or 1 (meets the requirements and can take the patient, possibly on diversion); null when
		/// none does. <c>MeetsRequirements</c> and an open status alone do not qualify: a destination reporting a required
		/// capability unavailable is tier 2 and never suggested.
		/// </summary>
		public int? SuggestedPoiId { get; set; }

		public DestinationRequirementsResultData Requirements { get; set; } = new DestinationRequirementsResultData();

		public List<SuggestedDestinationResultData> Destinations { get; set; } = new List<SuggestedDestinationResultData>();
	}

	public class SuggestedDestinationsResult : StandardApiResponseV4Base
	{
		public SuggestedDestinationsResultData Data { get; set; } = new SuggestedDestinationsResultData();
	}

	/// <summary>SetPoiAvailability body.</summary>
	public class SetPoiAvailabilityInput
	{
		public int PoiId { get; set; }

		/// <summary>0 open (clears the report), 1 limited, 2 diversion, 3 closed.</summary>
		public int Status { get; set; }

		/// <summary>Capability ids the destination cannot provide right now.</summary>
		public List<string> UnavailableCapabilityIds { get; set; } = new List<string>();

		public string Note { get; set; }

		/// <summary>Minutes until the report lapses (at most a week); null or 0 lasts until changed.</summary>
		public int? ExpiresInMinutes { get; set; }
	}

	public class SetPoiAvailabilityResult : StandardApiResponseV4Base
	{
		/// <summary>The destination after the change.</summary>
		public PoiResultData Data { get; set; }
	}

	public class PoiTypesResult : StandardApiResponseV4Base
	{
		public List<PoiTypeResultData> Data { get; set; } = new List<PoiTypeResultData>();
	}

	public class PoisResult : StandardApiResponseV4Base
	{
		public List<PoiResultData> Data { get; set; } = new List<PoiResultData>();
	}

	public class PoiResult : StandardApiResponseV4Base
	{
		public PoiResultData Data { get; set; }
	}
}
