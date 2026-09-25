namespace Resgrid.Web.Eventing.Hubs
{
	/// <summary>
	/// Single source of truth for the geolocation hub's group names, shared by the subscribers (hub,
	/// membership sync) and the publisher (Worker) so they cannot drift. User ids are GUIDs whose casing
	/// varies by caller, so they are lower-cased.
	/// </summary>
	public static class GeolocationGroups
	{
		/// <summary>Department maps. Only carries locations everyone in the department may see.</summary>
		public static string Department(int departmentId) => departmentId.ToString();

		/// <summary>
		/// Viewers who share one restricted viewer list from the location visibility matrix
		/// (see <see cref="Resgrid.Model.Services.ILocationVisibilityService"/>).
		/// </summary>
		public static string VisibilitySet(int departmentId, string visibilitySetKey) => $"GeoVisibility_{departmentId}_{visibilitySetKey}";

		/// <summary>A person's own connections: everyone may see their own location.</summary>
		public static string Self(int departmentId, string userId) => $"GeoSelf_{departmentId}_{Normalize(userId)}";

		/// <summary>Single-unit trackers (UnitLocationConnect).</summary>
		public static string Unit(string unitId) => $"UnitLocation_{unitId?.Trim()}";

		/// <summary>Single-person trackers (PersonLocationConnect).</summary>
		public static string Person(int departmentId, string userId) => $"PersonLocation_{departmentId}_{Normalize(userId)}";

		private static string Normalize(string userId) => userId?.Trim().ToLowerInvariant();
	}
}
