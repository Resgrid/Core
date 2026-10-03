using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

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

		/// <summary>Base map in a light theme, on the website and in every app.</summary>
		public MapStyleTypes MapStyle { get; set; }

		/// <summary>Base map the apps use in a dark theme. Automatic pairs with <see cref="MapStyle"/>.</summary>
		public MapStyleTypes MapStyleNight { get; set; }

		/// <summary>False when the server has no public Mapbox token for the website, so only the apps can honour the choice.</summary>
		public bool WebsiteSupportsMapStyle { get; set; }

		/// <summary>Public website token used for the settings-page style previews; empty disables them.</summary>
		public string PreviewAccessToken { get; set; }

		public double PreviewLatitude { get; set; }
		public double PreviewLongitude { get; set; }

		public bool UseMapboxOverride { get; set; }

		[Display(Name = "Mapbox Style Url")]
		public string MapboxStyleUrl { get; set; }

		[Display(Name = "Mapbox Public Access Token")]
		public string MapboxAccessToken { get; set; }
	}
}
