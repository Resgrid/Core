using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Security;
using Resgrid.Web.Helpers;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Areas.User.Controllers
{
	// Every action here acts on the signed-in user's own credentials and sessions, and reads that
	// identity from the auth cookie's claims. Without this an anonymous request reaches the action with
	// an empty UserId and blows up in the Identity store instead of being sent to the sign-in page.
	[Area("User")]
	[Authorize]
	public class AccountSecurityController : SecureBaseController
	{
		private readonly IUserSessionService _userSessionService;
		private readonly ISystemAuditsService _systemAuditsService;
		private readonly UserManager<IdentityUser> _userManager;
		private readonly IDepartmentSsoService _departmentSsoService;
		private readonly IExternalIdentityLinkService _externalIdentityLinkService;
		private readonly IDepartmentsService _departmentsService;

		public AccountSecurityController(IUserSessionService userSessionService, ISystemAuditsService systemAuditsService,
			UserManager<IdentityUser> userManager, IDepartmentSsoService departmentSsoService,
			IExternalIdentityLinkService externalIdentityLinkService, IDepartmentsService departmentsService)
		{
			_userSessionService = userSessionService;
			_systemAuditsService = systemAuditsService;
			_userManager = userManager;
			_departmentSsoService = departmentSsoService;
			_externalIdentityLinkService = externalIdentityLinkService;
			_departmentsService = departmentsService;
		}

		[HttpGet]
		public async Task<IActionResult> ChangeUsername(CancellationToken cancellationToken)
		{
			var user = await _userManager.FindByIdAsync(UserId);
			var model = new ChangeUsernameView
			{
				CurrentUsername = user?.UserName,
				IsSsoManaged = await IsSsoManagedAsync(cancellationToken)
			};
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ChangeUsername(ChangeUsernameView model, CancellationToken cancellationToken)
		{
			model.IsSsoManaged = await IsSsoManagedAsync(cancellationToken);
			var user = await _userManager.FindByIdAsync(UserId);
			model.CurrentUsername = user?.UserName;
			if (model.IsSsoManaged)
				ModelState.AddModelError(string.Empty, "This account is managed by SSO. Its username cannot be changed in Resgrid.");
			if (user == null || !await _userManager.CheckPasswordAsync(user, model.CurrentPassword ?? string.Empty))
				ModelState.AddModelError(nameof(model.CurrentPassword), "The current password is incorrect.");
			if (await _userManager.FindByNameAsync(model.NewUsername ?? string.Empty) is IdentityUser existing && existing.Id != UserId)
				ModelState.AddModelError(nameof(model.NewUsername), "That username is already in use.");
			if (!ModelState.IsValid)
				return View(model);

			var now = DateTime.UtcNow;
			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = now;
			user.AuthenticationStateChangedOn = now;
			var change = await _userManager.SetUserNameAsync(user, model.NewUsername.Trim());
			if (!change.Succeeded)
			{
				foreach (var error in change.Errors) ModelState.AddModelError(string.Empty, error.Description);
				return View(model);
			}

			await _userSessionService.RevokeAllAfterCredentialChangeAsync(UserId, UserId,
				UserSessionRevocationReason.UsernameChanged, now, cancellationToken);
			await AuditAsync(SystemAuditTypes.UsernameChanged, null, true, cancellationToken);
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			return RedirectToAction("LogOn", "Account", new { area = "", reason = "username-changed" });
		}

		[HttpGet]
		public async Task<IActionResult> ChangePassword(CancellationToken cancellationToken)
		{
			return View(new ChangePasswordView
			{
				MinPasswordLength = await _departmentSsoService.GetEffectiveMinPasswordLengthAsync(DepartmentId),
				IsSsoManaged = await IsSsoManagedAsync(cancellationToken)
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ChangePassword(ChangePasswordView model, CancellationToken cancellationToken)
		{
			model.MinPasswordLength = await _departmentSsoService.GetEffectiveMinPasswordLengthAsync(DepartmentId);
			model.IsSsoManaged = await IsSsoManagedAsync(cancellationToken);
			if (model.IsSsoManaged)
				ModelState.AddModelError(string.Empty, "This account is managed by SSO. Its password cannot be changed in Resgrid.");

			var policyError = await _departmentSsoService.ValidatePasswordAgainstPolicyAsync(DepartmentId, model.NewPassword);
			if (policyError != null)
				ModelState.AddModelError(nameof(model.NewPassword), policyError);
			if (!ModelState.IsValid)
				return View(model);

			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return NotFound();

			var now = DateTime.UtcNow;
			user.AuthenticationGeneration++;
			user.CredentialsValidAfterUtc = now;
			user.AuthenticationStateChangedOn = now;
			var change = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
			if (!change.Succeeded)
			{
				foreach (var error in change.Errors) ModelState.AddModelError(string.Empty, error.Description);
				return View(model);
			}

			await _departmentSsoService.RecordPasswordChangedAsync(DepartmentId, UserId, cancellationToken);
			await _userSessionService.RevokeAllAfterCredentialChangeAsync(UserId, UserId,
				UserSessionRevocationReason.PasswordChanged, now, cancellationToken);
			await AuditAsync(SystemAuditTypes.PasswordChanged, null, true, cancellationToken);
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			return RedirectToAction("LogOn", "Account", new { area = "", reason = "password-changed" });
		}

		[HttpGet]
		public async Task<IActionResult> Sessions(CancellationToken cancellationToken)
		{
			var model = new ActiveSessionsView
			{
				CurrentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId),
				Sessions = await _userSessionService.GetActiveForUserAsync(UserId, cancellationToken)
			};
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RevokeSession(string id, CancellationToken cancellationToken)
		{
			var currentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			var result = await _userSessionService.RevokeSessionAsync(UserId, UserId, id,
				UserSessionRevocationReason.UserRevoked, cancellationToken);
			await AuditAsync(SystemAuditTypes.SessionRevoked, id, result.RevokedSessionCount > 0, cancellationToken);

			if (string.Equals(currentSessionId, id, StringComparison.Ordinal))
			{
				await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
				return RedirectToAction("LogOn", "Account", new { area = "" });
			}

			TempData["SessionMessage"] = result.RevokedSessionCount > 0
				? "The selected session was signed out."
				: "That session was already inactive.";
			return RedirectToAction(nameof(Sessions));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RevokeOtherSessions(CancellationToken cancellationToken)
		{
			var currentSessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			if (string.IsNullOrWhiteSpace(currentSessionId))
			{
				TempData["SessionMessage"] = "This legacy session must be refreshed before other sessions can be distinguished safely.";
				return RedirectToAction(nameof(Sessions));
			}

			var result = await _userSessionService.RevokeOtherSessionsAsync(UserId, currentSessionId,
				UserSessionRevocationReason.OtherSessionsRevoked, cancellationToken);
			await AuditAsync(SystemAuditTypes.OtherSessionsRevoked, null, true, cancellationToken);
			TempData["SessionMessage"] = $"Signed out {result.RevokedSessionCount} other session(s).";
			return RedirectToAction(nameof(Sessions));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RevokeAllSessions(CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var result = await _userSessionService.RevokeAllAsync(UserId, UserId,
				UserSessionRevocationReason.AccountCompromised, now, cancellationToken);
			await AuditAsync(SystemAuditTypes.AllSessionsRevoked, null, true, cancellationToken);
			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			return RedirectToAction("LogOn", "Account", new { area = "", reason = "sessions-revoked" });
		}

		private Task AuditAsync(SystemAuditTypes type, string sessionId, bool successful, CancellationToken cancellationToken)
		{
			return _systemAuditsService.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Website,
				Type = (int)type,
				UserId = UserId,
				TargetUserId = UserId,
				SessionId = SessionSupportSuffix(sessionId),
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				CorrelationId = HttpContext.TraceIdentifier,
				Data = $"Account session operation. Agent={BoundAuditValue(Request.Headers.UserAgent.ToString(), 256)}",
				LoggedOn = DateTime.UtcNow
			}, cancellationToken);
		}

		private static string BoundAuditValue(string value, int maximumLength)
		{
			if (string.IsNullOrWhiteSpace(value)) return "Unknown";
			var sanitized = value.Replace("\r", " ").Replace("\n", " ").Trim();
			return sanitized.Length <= maximumLength ? sanitized : sanitized.Substring(0, maximumLength);
		}

		private static string SessionSupportSuffix(string sessionId) =>
			string.IsNullOrWhiteSpace(sessionId) || sessionId.Length <= 8
				? sessionId
				: sessionId.Substring(sessionId.Length - 8);

		private async Task<bool> IsSsoManagedAsync(CancellationToken cancellationToken)
		{
			var state = await _externalIdentityLinkService.GetSsoManagementStateAsync(UserId, cancellationToken);
			if (state.IsSsoManaged)
				return true;

			var member = await _departmentsService.GetDepartmentMemberAsync(UserId, DepartmentId);
			return member != null && (!string.IsNullOrWhiteSpace(member.ExternalSsoId) || member.SsoLinkedOn.HasValue);
		}
	}
}
