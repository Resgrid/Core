using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	public class PoiJson
	{
		public int PoiId { get; set; }
		public int PoiTypeId { get; set; }
		public string Name { get; set; }
		public double Longitude { get; set; }
		public double Latitude { get; set; }
		public string Address { get; set; }
		public string Note { get; set; }

		/// <summary>The names of the capabilities the POI offers, in the department's order.</summary>
		public List<string> Capabilities { get; set; } = new List<string>();

		/// <summary>The POI's type is a destination: the list shows its availability and offers to set it.</summary>
		public bool IsDestination { get; set; }

		/// <summary><see cref="Resgrid.Model.PoiAvailabilityStatuses"/>; Open when there is no current report.</summary>
		public int AvailabilityStatus { get; set; }

		/// <summary>The localized status name.</summary>
		public string AvailabilityLabel { get; set; }

		/// <summary>Names of the capabilities the destination reports it cannot provide right now.</summary>
		public List<string> UnavailableCapabilities { get; set; } = new List<string>();

		public string AvailabilityNote { get; set; }

		/// <summary>When the report lapses, in the department's time zone, already formatted; null when it has no expiry.</summary>
		public string AvailabilityExpires { get; set; }
	}
}
