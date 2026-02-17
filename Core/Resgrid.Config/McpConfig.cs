﻿namespace Resgrid.Config
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
		/// The transport mechanism for MCP (e.g., "stdio", "http")
		/// </summary>
		public static string Transport = "http";

		/// <summary>
		/// Enable CORS for HTTP transport (allows cross-origin requests)
		/// </summary>
		public static bool EnableCors = true;

		/// <summary>
		/// Allowed CORS origins (comma-separated list). Empty or "*" allows all origins.
		/// </summary>
		public static string CorsAllowedOrigins = "*";

		/// <summary>
		/// Enable HTTP transport endpoint
		/// </summary>
		public static bool EnableHttpTransport = true;

		/// <summary>
		/// Enable stdio transport (for backwards compatibility)
		/// </summary>
		public static bool EnableStdioTransport = false;
	}
}


