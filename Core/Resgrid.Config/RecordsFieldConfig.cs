namespace Resgrid.Config
{
	/// <summary>
	/// Field Records client change control (RMS plan RMS-1D): the minimum app version each operational app must
	/// report before the server hands it a Field Records catalog, and the sync page bound. A blank minimum accepts
	/// any version. Environment keys: RESGRID:RecordsFieldConfig:MinimumResponderVersion and so on.
	/// </summary>
	public static class RecordsFieldConfig
	{
		public static string MinimumResponderVersion = "";
		public static string MinimumUnitVersion = "";
		public static string MinimumIncidentCommandVersion = "";
		public static string MinimumDispatchVersion = "";

		/// <summary>Most rows one sync page carries; a field bundle is a working set, not an archive pull.</summary>
		public static int SyncTakeMax = 200;

		/// <summary>Most drafts and returned Records of the caller included in one bundle.</summary>
		public static int SyncDraftsMax = 100;

		/// <summary>Most open work assignments returned to one caller.</summary>
		public static int AssignmentsMax = 200;
	}
}
