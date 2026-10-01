using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Core Web on a shared workstation (passkey plan sections 10.5 and 12.5). A browser the station set up as a shared workstation
	/// asks for a shared session at every sign-in; that request can only make the session stricter, and is honored only while
	/// <c>PasskeyConfig.SharedDeviceModeEnabled</c> is on. The server owns the idle lock, the shift ceiling and the lock itself;
	/// a locked session reaches only its own lock screen, status, unlock and end-shift routes.
	/// </summary>
	public static class WebSharedSession
	{
		/// <summary>HttpContext.Items key under which session validation leaves the validated (or locked) session row.</summary>
		public const string SessionItemKey = "Resgrid.Web.ValidatedUserSession";

		/// <summary>The station's own setting: a label, never a person. It survives shifts and is not an authentication factor.</summary>
		public const string WorkstationCookie = ".Resgrid.SharedWorkstation";

		public const string LockedPath = "/SharedSession/Locked";

		private const string WorkstationPrefix = "v1:";
		private const int MaxLabelLength = 64;

		private static readonly Regex LockedPaths = new(
			@"^/(SharedSession/(Locked|Status|Lock|EndShift|Unlock|UnlockPasskeyOptions|UnlockPasskey|UnlockApproval|UnlockApprovalStatus|UnlockApprovalComplete)|Account/(SsoUnlockBegin|SsoReturn))/?$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

		/// <summary>Shared workstations need the deployment gate and tracked sessions, which hold the lock.</summary>
		public static bool IsAvailable => PasskeyConfig.SharedDeviceModeEnabled && SessionSecurityConfig.TrackingEnabled;

		/// <summary>The session row this request was validated against (locked only on the routes a locked session may reach).</summary>
		public static UserSession SessionOf(HttpContext httpContext) =>
			httpContext?.Items.TryGetValue(SessionItemKey, out var value) == true ? value as UserSession : null;

		/// <summary>True when this request belongs to a shared session that is locked (it reached one of its lock-screen routes).</summary>
		public static bool IsLocked(HttpContext httpContext) => SessionOf(httpContext) is { SharedMode: true, IsLocked: true };

		public static bool AcceptsLockedSession(HttpRequest request) =>
			request?.Path.HasValue == true && LockedPaths.IsMatch(request.Path.Value);

		/// <summary>
		/// Whether the operator caused this request (plan section 10.5): a navigation the browser marks as user-activated
		/// (<c>Sec-Fetch-User: ?1</c>, which page script cannot set), or a request this site's shared-session script marks after
		/// real input. Polling, sockets and incoming alerts are neither.
		/// </summary>
		public static bool IsOperatorActivity(HttpRequest request)
		{
			if (request == null)
				return false;
			if (string.Equals(request.Headers["Sec-Fetch-User"].ToString().Trim(), "?1", StringComparison.Ordinal))
				return true;

			var value = request.Headers[SharedSessionRules.ActivityHeader].ToString().Trim();
			return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>A top-level page load, which can be sent to the lock screen; anything else gets a status code instead.</summary>
		public static bool IsNavigation(HttpRequest request)
		{
			if (request == null || !HttpMethods.IsGet(request.Method))
				return false;

			var mode = request.Headers["Sec-Fetch-Mode"].ToString();
			if (!string.IsNullOrWhiteSpace(mode))
				return string.Equals(mode, "navigate", StringComparison.OrdinalIgnoreCase);

			return !string.Equals(request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase) &&
				request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>The lock screen, returning to <paramref name="returnPathAndQuery"/> once the same operator unlocks.</summary>
		public static string LockedUrl(string returnPathAndQuery) =>
			string.IsNullOrWhiteSpace(returnPathAndQuery) ? LockedPath : LockedPath + "?returnUrl=" + Uri.EscapeDataString(returnPathAndQuery);

		/// <summary>Whether this browser was set up as a shared workstation (whatever the gate says; see <see cref="IsAvailable"/>).</summary>
		public static bool IsWorkstation(HttpRequest request) => WorkstationLabel(request) != null;

		/// <summary>The workstation's label ("" when it has none), or null when this browser is not a shared workstation.</summary>
		public static string WorkstationLabel(HttpRequest request)
		{
			var value = request?.Cookies[WorkstationCookie];
			if (string.IsNullOrEmpty(value))
				return null;

			string decoded;
			try
			{
				decoded = Uri.UnescapeDataString(value);
			}
			catch (UriFormatException)
			{
				return null;
			}

			return decoded.StartsWith(WorkstationPrefix, StringComparison.Ordinal) ? SanitizeLabel(decoded.Substring(WorkstationPrefix.Length)) : null;
		}

		public static void SetWorkstation(HttpResponse response, string label)
		{
			response.Cookies.Append(WorkstationCookie, Uri.EscapeDataString(WorkstationPrefix + SanitizeLabel(label)), new CookieOptions
			{
				HttpOnly = true,
				Secure = true,
				SameSite = SameSiteMode.Strict,
				IsEssential = true,
				Path = "/",
				Expires = DateTimeOffset.UtcNow.AddDays(400)
			});
		}

		public static void ClearWorkstation(HttpResponse response) =>
			response.Cookies.Delete(WorkstationCookie, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Strict });

		/// <summary>A display label only: printable, trimmed and bounded, so it is safe in the session row and the audit.</summary>
		public static string SanitizeLabel(string label)
		{
			if (string.IsNullOrWhiteSpace(label))
				return string.Empty;

			var printable = new string(label.Where(c => !char.IsControl(c)).ToArray()).Trim();
			return printable.Length <= MaxLabelLength ? printable : printable.Substring(0, MaxLabelLength).Trim();
		}
	}
}
