namespace Resgrid.Config
{

	public static class LinksConfig
	{
		public static string BitlyAccessToken = "";
		public static string BitlyApi = @"https://api-ssl.bitly.com/shorten?access_token={0}&longUrl={1}";

		public static string PolrAccessToken = "";
		public static string PolrApi = @"http://yourpolrapi.com/api/v2/action/shorten?key={0}&url={1}&is_secret=true";
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
