﻿using System;
using System.Net.Http;
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
		private readonly IHttpClientFactory _httpClientFactory;

		public HealthController(
			McpToolRegistry toolRegistry,
			IResponseCache responseCache,
			IHttpClientFactory httpClientFactory)
		{
			_toolRegistry = toolRegistry;
			_responseCache = responseCache;
			_httpClientFactory = httpClientFactory;
		}

		/// <summary>
		/// Gets the current health status of the MCP Server
		/// </summary>
		/// <returns>HealthResult object with the server health status</returns>
		[HttpGet("current")]
		[HttpGet("getcurrent")]
		public async Task<IActionResult> GetCurrent()
		{
			var result = new HealthResult
			{
				ServerVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown",
				ServerName = McpConfig.ServerName ?? "Resgrid MCP Server",
				SiteId = "0",
				ToolCount = _toolRegistry.GetToolCount(),
				ServerRunning = true
			};

			// Check cache connectivity with real probe
			result.CacheOnline = await ProbeCacheConnectivityAsync();

			// Check API connectivity with real probe
			result.ApiOnline = await ProbeApiConnectivityAsync();

			return Json(result);
		}

		private async Task<bool> ProbeCacheConnectivityAsync()
		{
			try
			{
				const string sentinelKey = "_healthcheck_sentinel";
				var sentinelValue = Guid.NewGuid().ToString();
				var ttl = TimeSpan.FromSeconds(5);

				// Attempt to set and retrieve a sentinel value
				var retrieved = await _responseCache.GetOrCreateAsync(
					sentinelKey,
					() => Task.FromResult(sentinelValue),
					ttl);

				// Verify the value matches and clean up
				var success = retrieved == sentinelValue;
				_responseCache.Remove(sentinelKey);

				return success;
			}
			catch
			{
				return false;
			}
		}

		private async Task<bool> ProbeApiConnectivityAsync()
		{
			try
			{
				var apiBaseUrl = SystemBehaviorConfig.ResgridApiBaseUrl;
				if (string.IsNullOrWhiteSpace(apiBaseUrl))
					return false;

				using var httpClient = _httpClientFactory.CreateClient("ResgridApi");
				using var request = new HttpRequestMessage(HttpMethod.Head, "/");
				using var response = await httpClient.SendAsync(request);

				return response.IsSuccessStatusCode;
			}
			catch
			{
				return false;
			}
		}
	}
}


