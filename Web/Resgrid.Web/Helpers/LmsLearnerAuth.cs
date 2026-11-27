using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Resgrid.Model.Lms;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// The learner portal's own sign-in (Training T5 item 2): a cookie scheme of its own that the member app never reads. The cookie is
	/// scoped to /Learn (the browser never sends it to a member page), carries no member claim (no name identifier, no department claims,
	/// no roles) and is never the default scheme, so a learner is anonymous to every member controller and to the v4 API, which issues
	/// tokens to AspNetUsers accounts only. The portal controller reads it explicitly and checks the learner's security stamp every request.
	/// </summary>
	public static class LmsLearnerAuth
	{
		public const string Scheme = "ResgridLearner";
		public const string CookieName = "RGLEARNERAUTH";
		public const string CookiePath = "/Learn";
		public const string LearnerIdClaim = "rg_xl_learner";
		public const string DepartmentClaim = "rg_xl_department";
		public const string StampClaim = "rg_xl_stamp";
		public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

		public static void Configure(CookieAuthenticationOptions options, bool development)
		{
			options.Cookie.Name = CookieName;
			options.Cookie.Path = CookiePath;
			options.Cookie.HttpOnly = true;
			options.Cookie.SameSite = SameSiteMode.Lax;
			options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
			options.ExpireTimeSpan = Lifetime;
			options.SlidingExpiration = true;
			options.LoginPath = new PathString(CookiePath);
			options.AccessDeniedPath = new PathString(CookiePath);
			// The portal never challenges through the scheme; anything that tries gets a status code, never a redirect.
			options.Events.OnRedirectToLogin = context =>
			{
				context.Response.StatusCode = StatusCodes.Status401Unauthorized;
				return Task.CompletedTask;
			};
			options.Events.OnRedirectToAccessDenied = context =>
			{
				context.Response.StatusCode = StatusCodes.Status403Forbidden;
				return Task.CompletedTask;
			};
		}

		public static ClaimsPrincipal CreatePrincipal(LmsExternalLearner learner)
		{
			if (learner == null)
				throw new ArgumentNullException(nameof(learner));
			var identity = new ClaimsIdentity(new[]
			{
				new Claim(LearnerIdClaim, learner.LmsExternalLearnerId.ToString(CultureInfo.InvariantCulture)),
				new Claim(DepartmentClaim, learner.DepartmentId.ToString(CultureInfo.InvariantCulture)),
				new Claim(StampClaim, learner.SecurityStamp ?? string.Empty)
			}, Scheme, LearnerIdClaim, null);
			return new ClaimsPrincipal(identity);
		}

		/// <summary>The learner session a principal of this scheme carries; null for anything else.</summary>
		public static (int DepartmentId, int LearnerId, string Stamp)? Read(ClaimsPrincipal principal)
		{
			var identity = principal?.Identity as ClaimsIdentity;
			if (identity == null || !identity.IsAuthenticated || !string.Equals(identity.AuthenticationType, Scheme, StringComparison.Ordinal))
				return null;
			if (!int.TryParse(identity.FindFirst(DepartmentClaim)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var departmentId) ||
				!int.TryParse(identity.FindFirst(LearnerIdClaim)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var learnerId))
				return null;
			var stamp = identity.FindFirst(StampClaim)?.Value;
			return string.IsNullOrEmpty(stamp) ? null : (departmentId, learnerId, stamp);
		}
	}
}
