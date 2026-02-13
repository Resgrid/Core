namespace Resgrid.Web.Mcp.Models
{
	/// <summary>
	/// Response for getting the health of the Resgrid MCP Server.
	/// </summary>
	public sealed class HealthResult
	{
		/// <summary>
		/// Site\Location of this MCP Server
		/// </summary>
		public string SiteId { get; set; }

		/// <summary>
		/// The Version of the MCP Server
		/// </summary>
		public string ServerVersion { get; set; }

		/// <summary>
		/// The name of the MCP Server
		/// </summary>
		public string ServerName { get; set; }

		/// <summary>
		/// Number of registered tools
		/// </summary>
		public int ToolCount { get; set; }

		/// <summary>
		/// Can the MCP Server talk to the Resgrid API
		/// </summary>
		public bool ApiOnline { get; set; }

		/// <summary>
		/// Can the MCP Server talk to the cache
		/// </summary>
		public bool CacheOnline { get; set; }

		/// <summary>
		/// Is the MCP Server running
		/// </summary>
		public bool ServerRunning { get; set; }
	}
}

