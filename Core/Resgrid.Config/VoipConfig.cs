namespace Resgrid.Config
{
	/// <summary>
	/// Configuration for using a VOIP system for voice communication between applications
	/// </summary>
	public static class VoipConfig
	{
		public static int BaseChannelExtensionNumber = 10000;
		public static int BaseChannelExtensionBump = 1000;

		public static string VoipDomain = "";
		public static string VoipServerAddress = "";
		public static string VoipServerWebsocketAddress = "";
		public static string VoipServerWebsocketSslAddress = "";

		public static string KazooUsername = "";
		public static string KazooPassword = "";
		public static string KazzoAccount = "";
		public static string KazooCrossbarApiUrl = @"";
		public static string KazooCrossbarApiVersion = "";

		public static string OpenViduUrl = "";
		public static string OpenViduSecret = "";

		public static string LiveKitServerApiUrl = "";
		public static string LiveKitServerUrl = "";
		public static string LiveKitServerApiKey = "";
		public static string LiveKitServerApiSecret = "";
	}

	/// <summary>
	/// Possible backend voip providers
	/// </summary>
	public enum VoipProviderTypes
	{
		Kazoo = 0,
		OpenVidu = 1,
		LiveKit = 2
	}
}
