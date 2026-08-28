using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Resgrid.Config;

namespace Resgrid.Web.Broker.Middleware
{
	/// <summary>
	/// Application-tier workload gate (ADP plan section 2.2): every broker API request must present
	/// the shared workload key in X-Resgrid-Broker-Key. This is defense-in-depth UNDER network
	/// isolation and transport-level mTLS, never the only control. An unconfigured key refuses
	/// everything (503, fail closed); a wrong key is 401 with no detail. Comparison is
	/// constant-time. /health is exempt for the k8s probes.
	/// </summary>
	public class WorkloadKeyMiddleware
	{
		private readonly RequestDelegate _next;

		public WorkloadKeyMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
			{
				await _next(context);
				return;
			}

			var configuredKey = DataProtectionConfig.BrokerApiKey;
			if (string.IsNullOrWhiteSpace(configuredKey))
			{
				context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				return;
			}

			var presentedKey = context.Request.Headers["X-Resgrid-Broker-Key"].ToString();
			if (string.IsNullOrEmpty(presentedKey) || !FixedTimeEquals(presentedKey, configuredKey))
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return;
			}

			await _next(context);
		}

		private static bool FixedTimeEquals(string presented, string configured)
		{
			var presentedBytes = Encoding.UTF8.GetBytes(presented);
			var configuredBytes = Encoding.UTF8.GetBytes(configured);
			return CryptographicOperations.FixedTimeEquals(presentedBytes, configuredBytes);
		}
	}
}
