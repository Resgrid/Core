using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Web.Services.Middleware
{
	public class CapabilityPathRedactionMiddleware
	{
		public const string CapabilityTokenItemKey =
			"Resgrid.UnitTracking.CapabilityToken";
		public const string RedactedCapabilityPath =
			"/api/v4/unit-trackers/c/[REDACTED]";
		private const string CapabilityPathPrefix =
			"/api/v4/unit-trackers/c/";

		private readonly RequestDelegate _next;

		public CapabilityPathRedactionMiddleware(RequestDelegate next)
		{
			_next = next;
		}

		public async Task InvokeAsync(HttpContext context)
		{
			var path = context.Request.Path.Value;
			if (!string.IsNullOrWhiteSpace(path) &&
			    path.StartsWith(CapabilityPathPrefix, StringComparison.OrdinalIgnoreCase))
			{
				var suffix = path.Substring(CapabilityPathPrefix.Length);
				var separator = suffix.IndexOf('/');
				var token = separator >= 0 ? suffix.Substring(0, separator) : suffix;
				if (!string.IsNullOrWhiteSpace(token))
				{
					context.Items[CapabilityTokenItemKey] = token;
					context.Request.Path =
						RedactedCapabilityPath +
						(separator >= 0 ? suffix.Substring(separator) : string.Empty);
				}
			}

			await _next(context);
		}

		public static string RedactCapabilityUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return url;

			var start = url.IndexOf(CapabilityPathPrefix, StringComparison.OrdinalIgnoreCase);
			if (start < 0)
				return url;

			var tokenStart = start + CapabilityPathPrefix.Length;
			var tokenEnd = url.IndexOfAny(new[] { '?', '#', '/' }, tokenStart);
			if (tokenEnd < 0)
				tokenEnd = url.Length;

			return url.Substring(0, tokenStart) +
			       "[REDACTED]" +
			       url.Substring(tokenEnd);
		}
	}
}
