namespace Resgrid.Config
{
	/// <summary>
	/// Configuration settings for the Model Context Protocol (MCP) server
	/// Note: MCP uses SystemBehaviorConfig.ResgridApiBaseUrl for the API base URL
	/// </summary>
	public static class McpConfig
	{
		/// <summary>
		/// The name of the MCP server
		/// </summary>
		public static string ServerName = "Resgrid CAD System";

		/// <summary>
		/// The version of the MCP server
		/// </summary>
		public static string ServerVersion = "1.0.0";

		/// <summary>
		/// The transport mechanism for MCP (e.g., "stdio")
		/// </summary>
		public static string Transport = "stdio";
	}
}


