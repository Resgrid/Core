using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class MappingSettingsView
	{
		public bool? SaveSuccess { get; set; }
		public string Message { get; set; }

		/// <summary>
		/// Time-in-status thresholds driving Big Board highlighting, one row per canonical status
		/// meaning. Entered in minutes because that is how dispatchers talk about them.
		/// </summary>
		public List<UnitStatusThresholdRow> UnitStatusThresholds { get; set; } = new List<UnitStatusThresholdRow>();

		public int PersonnelLocationTTL { get; set; }
		public int UnitLocationTTL { get; set; }

		public bool PersonnelAllowStatusWithNoLocationToOverwrite { get; set; }
		public bool UnitAllowStatusWithNoLocationToOverwrite { get; set; }

		public bool UseMapboxOverride { get; set; }

		[Display(Name = "Mapbox Style Url")]
		public string MapboxStyleUrl { get; set; }

		[Display(Name = "Mapbox Public Access Token")]
		public string MapboxAccessToken { get; set; }
	}
}
