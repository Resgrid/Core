namespace Resgrid.Config
{
	public static class LinksConfig
	{
		public static string BitlyAccessToken = "";
		public static string BitlyApi = @"";

		public static string PolrAccessToken = "";
		public static string PolrApi = @"";
	}

	/// <summary>
	/// Possible providers for creating shortened links
	/// </summary>
	public enum LinksProviderTypes
	{
		Bitly = 0,
		Polr = 1
	}
}
