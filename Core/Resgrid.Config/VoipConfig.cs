namespace Resgrid.Config
{
	/// <summary>
	/// Configuration for using a VOIP system for voice communication between applications
	/// </summary>
	public static class VoipConfig
	{
		public static int BaseChannelExtensionNumber = 15;
		public static int BaseChannelExtensionBump = 15;

		public static string VoipDomain = "";
		public static string VoipServerAddress = "";
		public static string VoipServerWebsocketAddress = "";
		public static string VoipServerWebsocketSslAddress = "";

		public static string KazooUsername = "";
		public static string KazooPassword = "";
		public static string KazzoAccount = "";
		public static string KazooCrossbarApiUrl = @"";
		public static string KazooCrossbarApiVersion = "";
	}

	/// <summary>
	/// Possible backend voip providers
	/// </summary>
	public enum VoipProviderTypes
	{
		Kazoo = 0
	}
}
