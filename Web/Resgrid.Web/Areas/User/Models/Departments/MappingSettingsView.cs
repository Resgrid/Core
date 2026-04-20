using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Departments
{
	public class MappingSettingsView
	{
		public bool? SaveSuccess { get; set; }
		public string Message { get; set; }

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
