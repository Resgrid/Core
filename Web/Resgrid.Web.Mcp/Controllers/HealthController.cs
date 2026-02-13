using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Web.Mcp.Infrastructure;
using Resgrid.Web.Mcp.Models;

namespace Resgrid.Web.Mcp.Controllers
{
	/// <summary>
	/// Health Check system to get information and health status of the MCP Server
	/// </summary>
	[AllowAnonymous]
	[Route("health")]
	public sealed class HealthController : Controller
	{
		private readonly McpToolRegistry _toolRegistry;
		private readonly IResponseCache _responseCache;

		public HealthController(
			McpToolRegistry toolRegistry,
			IResponseCache responseCache)
		{
			_toolRegistry = toolRegistry;
			_responseCache = responseCache;
		}

		/// <summary>
		/// Gets the current health status of the MCP Server
		/// </summary>
		/// <returns>HealthResult object with the server health status</returns>
		[HttpGet("current")]
		public IActionResult GetCurrent()
		{
			var result = new HealthResult
			{
				ServerVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown",
				ServerName = McpConfig.ServerName ?? "Resgrid MCP Server",
				SiteId = "0",
				ToolCount = _toolRegistry.GetToolCount(),
				ServerRunning = true
			};

			// Check cache connectivity
			try
			{
				result.CacheOnline = _responseCache != null;
			}
			catch
			{
				result.CacheOnline = false;
			}

			// Check API connectivity
			try
			{
				// Simple ping to the API to check connectivity
				var apiBaseUrl = SystemBehaviorConfig.ResgridApiBaseUrl;
				result.ApiOnline = !string.IsNullOrWhiteSpace(apiBaseUrl);
			}
			catch
			{
				result.ApiOnline = false;
			}

			return Json(result);
		}
	}
}


