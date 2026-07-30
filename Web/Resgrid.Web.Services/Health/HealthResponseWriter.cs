using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Resgrid.Web.Services.Health
{
	/// <summary>
	/// Shared JSON writer for the /health and /health/full endpoints. Matches the payload
	/// shape the TTS microservice emits so monitoring can consume both uniformly.
	/// </summary>
	public static class HealthResponseWriter
	{
		public static async Task WriteAsync(HttpContext context, HealthReport report)
		{
			context.Response.ContentType = "application/json";

			var payload = new
			{
				status = report.Status.ToString(),
				checks = report.Entries.ToDictionary(
					entry => entry.Key,
					entry => new
					{
						status = entry.Value.Status.ToString(),
						description = entry.Value.Description,
						data = entry.Value.Data.Count > 0
							? entry.Value.Data.ToDictionary(item => item.Key, item => item.Value?.ToString())
							: null
					})
			};

			await context.Response.WriteAsync(JsonSerializer.Serialize(payload), context.RequestAborted);
		}
	}
}
