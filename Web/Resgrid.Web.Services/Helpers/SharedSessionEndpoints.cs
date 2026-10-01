using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Resgrid.Model;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// The narrow set of API endpoints a locked shared session may still reach (passkey plan section 12.5.3): its own status,
	/// lock, unlock and end shift. Everything else refuses it, including refresh, grants and SignalR.
	/// </summary>
	public static class SharedSessionEndpoints
	{
		/// <summary>HttpContext.Items key under which session validation leaves the validated (or locked) session row.</summary>
		public const string SessionItemKey = "Resgrid.ValidatedUserSession";

		private static readonly Regex LockedPaths = new(
			@"^/api/v[0-9.]+/sessions/(current|lock|end-shift|unlock-options|unlock-sso|complete-unlock|unlock-approval(/[^/]+)?)/?$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

		public static bool AcceptsLockedSession(HttpRequest request) =>
			request?.Path.HasValue == true && LockedPaths.IsMatch(request.Path.Value);

		/// <summary>The session row this request was validated against (locked only on the endpoints above); null otherwise.</summary>
		public static UserSession SessionOf(HttpContext httpContext) =>
			httpContext?.Items.TryGetValue(SessionItemKey, out var value) == true ? value as UserSession : null;

		/// <summary>True when the client marked this request as caused by the operator (<c>X-Resgrid-Operator-Activity: 1</c>).</summary>
		public static bool IsOperatorActivity(HttpRequest request)
		{
			var value = request?.Headers[SharedSessionRules.ActivityHeader].ToString().Trim();
			return value == "1" || string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase);
		}
	}
}
