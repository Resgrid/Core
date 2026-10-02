using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;


namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Passkeys for Core Web on the account security page (passkey plan sections 6.1 and 6.5): add a passkey bound to Web,
	/// rename and remove any of the user's passkeys in any app. Every command goes through the same passkey service as the
	/// API, which rechecks the rollout gate, the Web relying party and this session's server-side evidence; the page only
	/// forwards the browser's ceremony. JSON only, antiforgery on every post.
	/// </summary>
	[Area("User")]
	[Microsoft.AspNetCore.Authorization.Authorize]
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): signing in
	// and out, locking and unlocking a shared session, and verifying a second factor touch no department data.
	[Resgrid.Web.Filters.AllowDuringDepartmentLock]
	public class PasskeysController : SecureBaseController
	{
		private readonly IPasskeyService _passkeys;
		private readonly UserManager<IdentityUser> _userManager;

		public PasskeysController(IPasskeyService passkeys, UserManager<IdentityUser> userManager)
		{
			_passkeys = passkeys;
			_userManager = userManager;
		}

		/// <summary>Creation options for a new Web passkey; needs a recent password (or SSO) confirmation and second factor.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RegistrationOptions(CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null)
				return Refuse(PasskeyOutcome.SessionRequired);

			var totpEnrolled = await _userManager.GetTwoFactorEnabledAsync(user);
			var codesLeft = totpEnrolled ? await _userManager.CountRecoveryCodesAsync(user) : 0;
			var start = await _passkeys.BeginRegistrationAsync(Caller(user), totpEnrolled, codesLeft, cancellationToken);
			return start.Succeeded
				? Json(new { success = true, requestId = start.RequestId, options = start.OptionsJson })
				: Refuse(start.Outcome);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CompleteRegistration([FromForm] string requestId, [FromForm] string credential, [FromForm] string displayName,
			CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null)
				return Refuse(PasskeyOutcome.SessionRequired);
			if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(credential))
				return Refuse(PasskeyOutcome.InvalidRequest);

			var registered = await _passkeys.CompleteRegistrationAsync(Caller(user), requestId, credential, displayName, cancellationToken);
			return registered.Outcome == PasskeyOutcome.Succeeded ? Json(new { success = true }) : Refuse(registered.Outcome);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Rename([FromForm] string id, [FromForm] string displayName, CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null)
				return Refuse(PasskeyOutcome.SessionRequired);

			var outcome = await _passkeys.RenameAsync(Caller(user), id, displayName, cancellationToken);
			return outcome == PasskeyOutcome.Succeeded ? Json(new { success = true }) : Refuse(outcome);
		}

		/// <summary>Removes one of the user's passkeys, in any app; needs the authenticator or a Web passkey within five minutes.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Remove([FromForm] string id, CancellationToken cancellationToken)
		{
			var user = await _userManager.GetUserAsync(User);
			if (user == null)
				return Refuse(PasskeyOutcome.SessionRequired);

			var removed = await _passkeys.RevokeAsync(Caller(user), id, cancellationToken);
			return removed.Outcome == PasskeyOutcome.Succeeded
				? Json(new { success = true, signedOut = removed.CurrentSessionEnded })
				: Refuse(removed.Outcome);
		}

		private PasskeyCaller Caller(IdentityUser user) =>
			PasskeyCaller.From(HttpProtectedGrantContext.SessionOf(HttpContext), user.Id, user.UserName, DepartmentId, SystemAuditSystems.Website,
				IpAddressHelper.GetRequestIP(Request, true));

		/// <summary>
		/// A value-free code, and where the page sends the user when the answer is "prove who you are first": the password
		/// (or SSO) confirmation, or a fresh second factor, each returning to the account security page.
		/// </summary>
		private IActionResult Refuse(PasskeyOutcome outcome)
		{
			var back = Url.Action("Index", "TwoFactor", new { area = "User" });
			var redirect = outcome switch
			{
				PasskeyOutcome.ReauthenticationRequired => Url.Action("Reauthenticate", "AccountSecurity", new { area = "User", returnUrl = back }),
				PasskeyOutcome.StepUpRequired => Url.Action("Verify2FA", "TwoFactor", new { area = "User", returnUrl = back }),
				PasskeyOutcome.SessionRequired => Url.Action("LogOn", "Account", new { area = "" }),
				_ => null
			};
			return Json(new { success = false, error = PasskeyOutcomes.ErrorCode(outcome), redirect });
		}
	}
}
