using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Passkeys;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Passkey enrollment and the user's own inventory (passkey plan sections 6.1, 6.5 and 11). A passkey is registered for
	/// the calling app only and works only there; the inventory covers every app, so a passkey made in one app can be
	/// removed from any other. Every command rechecks the rollout gate and the session's server-side evidence.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): a locked
	// shared session must still unlock or end its shift, and Responder must still approve or deny.
	[Resgrid.Web.Services.Filters.AllowDuringDepartmentLock]
	public class PasskeysController : V4AuthenticatedApiControllerbase
	{
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IPasskeyService _passkeys;

		public PasskeysController(UserManager<Model.Identity.IdentityUser> userManager, IPasskeyService passkeys)
		{
			_userManager = userManager;
			_passkeys = passkeys;
		}

		/// <summary>
		/// Creation options for a new passkey bound to this app. Needs a password or SSO verification and a second factor
		/// for this session within five minutes, and an authenticator app with recovery codes already set up.
		/// </summary>
		[HttpPost("RegistrationOptions")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyCeremonyResult>> RegistrationOptions(CancellationToken cancellationToken)
		{
			var user = await _userManager.FindByIdAsync(UserId);
			if (user == null)
				return Problem(type: "session_revoked", title: "User not found.", statusCode: StatusCodes.Status401Unauthorized);

			var start = await _passkeys.BeginRegistrationAsync(ApiPasskeys.Caller(HttpContext, user.Id, user.UserName, DepartmentId),
				await _userManager.GetTwoFactorEnabledAsync(user), await _userManager.CountRecoveryCodesAsync(user), cancellationToken);
			if (!start.Succeeded)
				return Failure(start.Outcome);

			return Ceremony(start);
		}

		/// <summary>Verifies the platform's response and stores the passkey. The key is not usable until this returns.</summary>
		[HttpPost("CompleteRegistration")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyResult>> CompleteRegistration([FromBody] CompletePasskeyRegistrationInput input,
			CancellationToken cancellationToken)
		{
			if (input == null)
				return Failure(PasskeyOutcome.InvalidRequest);

			var registered = await _passkeys.CompleteRegistrationAsync(ApiPasskeys.Caller(HttpContext, UserId, UserName, DepartmentId),
				input.RequestId, ApiPasskeys.CredentialJson(input.Credential), input.DisplayName, cancellationToken);
			if (!registered.Succeeded)
				return Failure(registered.Outcome);

			var result = new PasskeyResult { Data = ToData(registered.Passkey), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>The caller's own active passkeys in every app. Never another user's.</summary>
		[HttpGet("List")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyListResult>> List(CancellationToken cancellationToken)
		{
			var passkeys = await _passkeys.GetActiveForUserAsync(UserId, cancellationToken);
			var result = new PasskeyListResult
			{
				Data = passkeys.Select(ToData).ToList(),
				PageSize = passkeys.Count,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("Rename")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyChangeResult>> Rename([FromBody] RenamePasskeyInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return Failure(PasskeyOutcome.InvalidRequest);

			return Change(await _passkeys.RenameAsync(ApiPasskeys.Caller(HttpContext, UserId, UserName, DepartmentId), input.PasskeyId,
				input.DisplayName, cancellationToken));
		}

		/// <summary>
		/// Removes one passkey at Resgrid, in any app, after a second factor within five minutes (authenticator app, or a
		/// passkey for this app). It may remain in the device or password manager but can no longer be used. Sessions that
		/// signed in with it end, possibly including this one (<c>CurrentSessionEnded</c>).
		/// </summary>
		[HttpPost("Revoke")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyChangeResult>> Revoke([FromBody] RevokePasskeyInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return Failure(PasskeyOutcome.InvalidRequest);

			return Change(await _passkeys.RevokeAsync(ApiPasskeys.Caller(HttpContext, UserId, UserName, DepartmentId), input.PasskeyId, cancellationToken));
		}

		/// <summary>Removes every passkey the caller has for one app, under the same verification rule as <see cref="Revoke"/>.</summary>
		[HttpPost("RevokeAllForClient")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyChangeResult>> RevokeAllForClient([FromBody] RevokeAllPasskeysForClientInput input,
			CancellationToken cancellationToken)
		{
			var client = ApiPasskeys.ClientFromName(input?.Client);
			if (client == null)
				return Failure(PasskeyOutcome.InvalidRequest);

			return Change(await _passkeys.RevokeAllForClientAsync(ApiPasskeys.Caller(HttpContext, UserId, UserName, DepartmentId), client.Value,
				cancellationToken));
		}

		/// <summary>Responder passkeys only: allow or stop approving other apps' requests.</summary>
		[HttpPost("SetApproval")]
		[Authorize]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyChangeResult>> SetApproval([FromBody] SetPasskeyApprovalInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return Failure(PasskeyOutcome.InvalidRequest);

			return Change(await _passkeys.SetApprovalEnabledAsync(ApiPasskeys.Caller(HttpContext, UserId, UserName, DepartmentId), input.PasskeyId,
				input.Enabled, cancellationToken));
		}

		internal static PasskeyResultData ToData(UserPasskey passkey) => new()
		{
			PasskeyId = passkey.UserPasskeyId,
			DisplayName = passkey.DisplayName,
			Client = ApiPasskeys.ClientName((UserSessionClientApplication)passkey.ClientApplication),
			CreatedOn = ApiPasskeys.Iso(passkey.CreatedOnUtc),
			CreatedPlatform = passkey.RegistrationPlatform,
			CreatedInstallation = passkey.RegistrationInstallation,
			CreatedUserAgentFamily = passkey.RegistrationUserAgentFamily,
			CreatedOnSharedInstallation = passkey.RegisteredInSharedMode,
			Attachment = passkey.RegistrationAttachment,
			BackupEligible = passkey.IsBackupEligible,
			BackedUp = passkey.IsBackedUp,
			LastUsedOn = ApiPasskeys.Iso(passkey.LastUsedOnUtc),
			LastUsedClient = passkey.LastUsedClientApplication == null
				? null
				: ApiPasskeys.ClientName((UserSessionClientApplication)passkey.LastUsedClientApplication.Value),
			LastUsedInstallation = passkey.LastUsedInstallation,
			ApprovalEnabled = passkey.ClientApplication == (int)UserSessionClientApplication.Responder ? passkey.ApprovalEnabled : null
		};

		private ActionResult<PasskeyCeremonyResult> Ceremony(PasskeyCeremonyStart start)
		{
			var result = new PasskeyCeremonyResult
			{
				Data = new PasskeyCeremonyResultData { RequestId = start.RequestId, Options = ApiPasskeys.Options(start.OptionsJson) },
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ActionResult<PasskeyChangeResult> Change(PasskeyOutcome outcome) => Change(PasskeyRevocationResult.Of(outcome));

		private ActionResult<PasskeyChangeResult> Change(PasskeyRevocationResult change)
		{
			if (change.Outcome != PasskeyOutcome.Succeeded)
				return Failure(change.Outcome);

			var result = new PasskeyChangeResult
			{
				Data = new PasskeyChangeResultData
				{
					Revoked = change.Revoked,
					SessionsEnded = change.SessionsEnded,
					CurrentSessionEnded = change.CurrentSessionEnded
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ObjectResult Failure(PasskeyOutcome outcome) =>
			Problem(type: PasskeyOutcomes.ErrorCode(outcome), title: ApiPasskeys.TitleFor(outcome), statusCode: ApiPasskeys.StatusFor(outcome));
	}
}
