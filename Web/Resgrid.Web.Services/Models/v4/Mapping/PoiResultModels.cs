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
	}

	public class NearestDestinationsResult : StandardApiResponseV4Base
	{
		public List<NearestDestinationResultData> Data { get; set; } = new List<NearestDestinationResultData>();
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
