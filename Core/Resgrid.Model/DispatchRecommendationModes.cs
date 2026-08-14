namespace Resgrid.Model
{
	/// <summary>
	/// How the dispatch recommendation engine selects resources to fill a run card.
	/// Stored as the department-wide default (DepartmentSettingTypes.DispatchRecommendationMode)
	/// and optionally overridden per run card (RunCard.DispatchModeOverride).
	/// </summary>
	public enum DispatchRecommendationModes
	{
		/// <summary>No automatic selection; run cards only inform manual dispatch.</summary>
		Off = 0,

		/// <summary>Fill from the station group whose geofence contains the call, cascading to next-nearest stations on shortfall.</summary>
		StationBased = 1,

		/// <summary>Fill by proximity using the latest unit/personnel geolocation.</summary>
		ClosestUnit = 2
	}
}
