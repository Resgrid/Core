namespace Resgrid.Config
{
	public static class MarketingConfig
	{
		/// <summary>Loops (loops.so) REST API base. Department administrators and managing members are synced here as contacts.</summary>
		public static string LoopsApiUrl = "https://app.loops.so/api/v1/";

		/// <summary>Loops API key (Settings, API). Leave blank to turn email marketing sync off.</summary>
		public static string LoopsApiKey = "";

		/// <summary>Optional Loops mailing list id that administrators are added to.</summary>
		public static string LoopsAdminMailingListId = "";
	}
}
