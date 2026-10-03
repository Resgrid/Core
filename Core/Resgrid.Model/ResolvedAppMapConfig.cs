namespace Resgrid.Model
{
	/// <summary>
	/// What a native app renders its Mapbox base maps with for one department. Built by
	/// <c>IDepartmentSettingsService.GetAppMapConfigForDepartmentAsync</c>.
	/// </summary>
	public class ResolvedAppMapConfig
	{
		/// <summary>
		/// Public (pk.) Mapbox token the app should use, or empty to keep the token built into the app.
		/// The department's own token when its Mapbox override is on, otherwise the system token for the app.
		/// </summary>
		public string AccessToken { get; set; } = string.Empty;

		/// <summary>mapbox:// style url for a light theme. Always populated.</summary>
		public string DayStyleUrl { get; set; }

		/// <summary>mapbox:// style url for a dark theme. Always populated.</summary>
		public string NightStyleUrl { get; set; }

		/// <summary>True when the department's own Mapbox account (token and custom style) is in use.</summary>
		public bool IsDepartmentOverride { get; set; }
	}
}
