using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	/// <summary>Mapping/PoiCapabilities: the department's POI capabilities ("Cath Lab", "Stroke Center"), managed by map administrators.</summary>
	public class PoiCapabilitiesView
	{
		public List<PoiCapability> Capabilities { get; set; } = new List<PoiCapability>();

		/// <summary>How many POIs offer each capability, by capability id (shown so a delete's reach is visible).</summary>
		public Dictionary<string, int> PoiCounts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		public string Message { get; set; }

		public string ErrorMessage { get; set; }
	}

	/// <summary>One destination POI with what it offers and how available it is now, as the destination lists show it.</summary>
	public class PoiAvailabilityRow
	{
		public int PoiId { get; set; }
		public int PoiTypeId { get; set; }
		public string TypeName { get; set; }
		public string Name { get; set; }
		public string Address { get; set; }

		/// <summary>The names of the capabilities the destination offers, in the department's order.</summary>
		public List<string> Capabilities { get; set; } = new List<string>();

		/// <summary><see cref="PoiAvailabilityStatuses"/>; Open when there is no current report.</summary>
		public int Status { get; set; }

		/// <summary>The localized status name.</summary>
		public string StatusLabel { get; set; }

		/// <summary>Names of the capabilities reported unavailable right now.</summary>
		public List<string> UnavailableCapabilities { get; set; } = new List<string>();

		public string Note { get; set; }

		/// <summary>When the report was set, in the department's time zone.</summary>
		public DateTime? SetOn { get; set; }

		/// <summary>When the report lapses, in the department's time zone; null when it lasts until changed.</summary>
		public DateTime? ExpiresOn { get; set; }

		public bool IsOpen => Status == (int)PoiAvailabilityStatuses.Open;
	}

	/// <summary>Mapping/Destinations: every destination POI and its current availability, for dispatchers and map administrators.</summary>
	public class DestinationAvailabilityView
	{
		public List<PoiAvailabilityRow> Destinations { get; set; } = new List<PoiAvailabilityRow>();

		/// <summary>The member also manages map data (POIs, capabilities): the page links to those screens.</summary>
		public bool CanManagePois { get; set; }

		public string Message { get; set; }
	}

	/// <summary>Mapping/SetPoiAvailability: one destination's availability form.</summary>
	public class SetPoiAvailabilityView
	{
		/// <summary>The expiry choices offered, in hours; 0 is "no expiry" (the report lasts until someone changes it).</summary>
		public static readonly int[] ExpiryHours = { 0, 1, 2, 4, 8, 12, 24 };

		/// <summary>Posted to keep a current report's expiry when only its status, capabilities or note change.</summary>
		public const int KeepExpiry = -1;

		public int PoiId { get; set; }

		/// <summary><see cref="PoiAvailabilityStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>The ticked capabilities the destination cannot provide right now.</summary>
		public List<string> UnavailableCapabilityIds { get; set; } = new List<string>();

		public string Note { get; set; }

		/// <summary>One of <see cref="ExpiryHours"/>, or <see cref="KeepExpiry"/>.</summary>
		public int ExpiresInHours { get; set; }

		/// <summary>"Type" returns to the POI type's list (map administrators); anything else to the destinations page.</summary>
		public string ReturnTo { get; set; }

		// Display only (filled by the controller, never bound from the post).
		public PoiAvailabilityRow Destination { get; set; }

		/// <summary>The capabilities that can be ticked unavailable: those the destination offers, plus any reported unavailable now.</summary>
		public List<PoiCapability> Capabilities { get; set; } = new List<PoiCapability>();
	}

	/// <summary>The colour of a destination's availability badge, shared by the server-rendered pages (the POI list mirrors it in script).</summary>
	public static class PoiAvailabilityBadge
	{
		public static string Color(int status)
		{
			switch ((PoiAvailabilityStatuses)status)
			{
				case PoiAvailabilityStatuses.Limited:
					return "#f0ad4e";
				case PoiAvailabilityStatuses.Diversion:
					return "#e8590c";
				case PoiAvailabilityStatuses.Closed:
					return "#d9534f";
				default:
					return "#1ab394";
			}
		}
	}
}
