using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>Why a call is in a location history. A call can match more than one way.</summary>
	[Flags]
	public enum CallLocationMatch
	{
		None = 0,
		/// <summary>The call is linked to one of the contacts being looked up.</summary>
		SameContact = 1,
		/// <summary>Same address, spelled any way (<see cref="StreetAddressMatch.Same"/>).</summary>
		SameAddress = 2,
		/// <summary>Probably the same address; one side leaves out a directional or names another locality (<see cref="StreetAddressMatch.Similar"/>).</summary>
		SimilarAddress = 4,
		/// <summary>Within <see cref="CallLocationQuery.NearbyMeters"/> of the location, where one side has no street address to compare.</summary>
		Nearby = 8
	}

	/// <summary>What to look a location history up by.</summary>
	public class CallLocationQuery
	{
		public const int DefaultLimit = 50;
		public const int MaxLimit = 200;
		public const double DefaultNearbyMeters = 40;

		/// <summary>Free-text address (a call's Address, an occupancy's AddressText).</summary>
		public string Address { get; set; }
		/// <summary>Structured locality and postal code for sources that keep them apart; optional.</summary>
		public string Locality { get; set; }
		public string PostalCode { get; set; }
		public double? Latitude { get; set; }
		public double? Longitude { get; set; }

		/// <summary>Calls linked to any of these contacts are included.</summary>
		public List<string> ContactIds { get; set; } = new List<string>();
		/// <summary>
		/// When true a contact-linked call only counts if it has no location of its own: an occupancy's history must not
		/// pull in a multi-site business contact's calls at its other sites.
		/// </summary>
		public bool ContactCallsOnlyWithoutLocation { get; set; }

		public int? ExcludeCallId { get; set; }
		public double NearbyMeters { get; set; } = DefaultNearbyMeters;
		public int Limit { get; set; } = DefaultLimit;
		public bool IncludeNotes { get; set; } = true;
	}

	/// <summary>One call in a location history.</summary>
	public class CallLocationHistoryEntry
	{
		public Call Call { get; set; }
		public CallLocationMatch Match { get; set; }
		/// <summary>Meters from the looked-up point when both have coordinates.</summary>
		public double? DistanceMeters { get; set; }
		/// <summary>Live (not deleted) notes, oldest first, when <see cref="CallLocationQuery.IncludeNotes"/> was set. Protected departments keep their envelopes.</summary>
		public List<CallNote> Notes { get; set; } = new List<CallNote>();
	}

	/// <summary>A count from bounded candidate lookups; a lower bound displays as N+ rather than an exact total.</summary>
	public class CallLocationCount
	{
		public int Count { get; set; }
		public bool IsLowerBound { get; set; }
	}

	public class CallLocationHistoryResult
	{
		public List<CallLocationHistoryEntry> Entries { get; set; } = new List<CallLocationHistoryEntry>();
		/// <summary>More calls matched than <see cref="CallLocationQuery.Limit"/>; the newest are returned.</summary>
		public bool HasMore { get; set; }
		/// <summary>
		/// False while the department's Advanced Data Protection policy is not Disabled: call addresses are encrypted,
		/// so only contact links are searched.
		/// </summary>
		public bool AddressMatchingAvailable { get; set; }
		/// <summary>False while the backfill has not reached the department's oldest call; older calls may be missing.</summary>
		public bool IndexComplete { get; set; }
		/// <summary>How the looked-up address was understood, for display ("110 S MAIN ST"); null when it did not parse.</summary>
		public string InterpretedAddress { get; set; }
	}

	/// <summary>A candidate row read from the location index or the contact links, before the call itself is loaded.</summary>
	public class CallLocationCandidate
	{
		public int CallId { get; set; }
		public DateTime LoggedOn { get; set; }
		public string AddressKey { get; set; }
		public string AddressCanonical { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		/// <summary>False for a contact-linked call that has no row in the location index.</summary>
		public bool Indexed { get; set; }
	}

	/// <summary>A source call for the backfill: only the columns the key needs.</summary>
	public class CallLocationSource
	{
		public int CallId { get; set; }
		public int DepartmentId { get; set; }
		public string Address { get; set; }
		public string GeoLocationData { get; set; }
		public DateTime LoggedOn { get; set; }
	}

	public class CallLocationIndexSweepResult
	{
		public int DepartmentsVisited { get; set; }
		public int CallsIndexed { get; set; }
		public int DepartmentsCompleted { get; set; }
		public int DepartmentsSuppressed { get; set; }
		public int DepartmentsReset { get; set; }
		public int Errors { get; set; }

		public string Message =>
			$"Call location index: {DepartmentsVisited} department(s), {CallsIndexed} call(s) indexed, {DepartmentsCompleted} completed, " +
			$"{DepartmentsSuppressed} suppressed for data protection, {DepartmentsReset} reset, {Errors} error(s).";
	}

	/// <summary>
	/// An occupancy's identity and location as Contacts, Calls and the location history need it. Name and address are
	/// plaintext on the occupancy master (only access, hazard and contact-name fields are protected).
	/// </summary>
	public class OccupancyLocationSummary
	{
		public string OccupancyId { get; set; }
		public string OccupancyNumber { get; set; }
		public string Name { get; set; }
		/// <summary><see cref="RmsOccupancyStatus"/>.</summary>
		public int Status { get; set; }
		public string AddressText { get; set; }
		public string City { get; set; }
		public string StateProvince { get; set; }
		public string PostalCode { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		/// <summary>The contact's role at this occupancy when read for a contact; 0 when read for the occupancy itself.</summary>
		public int Role { get; set; }
		public bool IsPrimary { get; set; }
		/// <summary>Every contact linked to the occupancy, any role.</summary>
		public List<string> ContactIds { get; set; } = new List<string>();

		public string FullAddress => string.Join(", ", new[] { ProtectedDataEnvelope.SafeDisplay(AddressText), ProtectedDataEnvelope.SafeDisplay(City), string.Join(" ", new[] { ProtectedDataEnvelope.SafeDisplay(StateProvince), ProtectedDataEnvelope.SafeDisplay(PostalCode) }) }.WhereNotBlank());

		public CallLocationQuery ToLocationQuery() => new CallLocationQuery
		{
			Address = AddressText,
			Locality = City,
			PostalCode = PostalCode,
			Latitude = Latitude.HasValue ? (double?)(double)Latitude.Value : null,
			Longitude = Longitude.HasValue ? (double?)(double)Longitude.Value : null
		};
	}

	internal static class CallLocationStringExtensions
	{
		public static IEnumerable<string> WhereNotBlank(this IEnumerable<string> values)
		{
			foreach (var value in values)
				if (!string.IsNullOrWhiteSpace(value))
					yield return value.Trim();
		}
	}
}
