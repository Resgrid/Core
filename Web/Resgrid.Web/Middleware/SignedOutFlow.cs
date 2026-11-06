using System;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Resgrid.Web.Middleware
{
	/// <summary>
	/// The pages of a signed-out flow (forgot password and the emailed reset link). They run as anonymous whatever sign-in cookie
	/// the browser holds: <see cref="SessionValidationMiddleware"/> replaces the principal before session validation, the sign-in
	/// policy middlewares (password change, MFA enrollment) and the anti-forgery check see it.
	/// <para>
	/// Without this, a browser that is signed in (a shared station computer, a member still signed in when an administrator sends
	/// them a link) or that holds a revoked or idle-ended session cookie breaks the flow. The reset link opened from webmail is a
	/// cross-site navigation that carries no SameSite=Strict cookie, so the page's anti-forgery token is issued to an anonymous
	/// user, while the page's own same-site post carries the sign-in and is refused (400). A stale cookie gets a bare 401 from session
	/// validation. A session held for a password change or MFA enrollment is redirected away from the form.
	/// </para>
	/// <para>
	/// Matched by path because the site routes with UseMvc (endpoint routing is off), so no endpoint metadata exists when the
	/// middlewares run. These actions must not read the signed-in user. Nothing is signed out here.
	/// </para>
	/// </summary>
	public static class SignedOutFlow
	{
		/// <summary>AccountController's password recovery actions. Each also covers its own sub-paths (the route's optional id).</summary>
		public static readonly string[] Paths =
		{
			"/Account/ForgotPassword",
			"/Account/ForgotPasswordConfirmation",
			"/Account/ResetPassword",
			"/Account/BeginPasswordReset"
		};

		/// <summary>Whether the request path (without its PathBase) is one of the <see cref="Paths"/>, in any letter case.</summary>
		public static bool Matches(PathString path) =>
			Paths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
	}
}
