namespace Resgrid.Model
{
	/// <summary>
	/// Who may receive a realtime location update: everyone connected to the department, or only the
	/// viewers in one visibility set (a distinct viewer list from the location visibility matrix).
	/// </summary>
	public sealed class LocationAudience
	{
		public static readonly LocationAudience EntireDepartment = new LocationAudience(null);

		private LocationAudience(string visibilitySetKey)
		{
			VisibilitySetKey = visibilitySetKey;
		}

		/// <summary>Null when the whole department may see the location.</summary>
		public string VisibilitySetKey { get; }

		public bool IsEntireDepartment => VisibilitySetKey == null;

		public static LocationAudience ForVisibilitySet(string visibilitySetKey)
		{
			return string.IsNullOrWhiteSpace(visibilitySetKey) ? EntireDepartment : new LocationAudience(visibilitySetKey);
		}
	}
}
