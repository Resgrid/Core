namespace Resgrid.Config
{
	public static class LinksConfig
	{
		public static string BitlyAccessToken = "";
		public static string BitlyApi = @"";

		public static string PolrAccessToken = "";
		public static string PolrApi = @"";

		public static string KuttAccessToken = "";
		public static string KuttApi = @"";
	}

	/// <summary>
	/// Possible providers for creating shortened links
	/// </summary>
	public enum LinksProviderTypes
	{
		Bitly = 0,
		Polr = 1,
		Kutt = 2
	}
}
