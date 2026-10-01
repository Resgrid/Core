using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.AccountSecurity;
using Resgrid.Repositories.DataRepository.Stores;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// "I lost my authenticator" (passkey plan sections 5.4 and 6.3). A sign-in's verified first factor (its login
	/// transaction) and a recovery code open a restricted recovery; its secret permits only status, staging a new
	/// authenticator, completing and canceling. Completion replaces the authenticator, rotates the recovery codes, removes
	/// the lost passkeys the user chose, and ends every session; the user then signs in normally. Nothing here issues a token.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/AccountSecurity")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[AllowAnonymous]
	public class FactorRecoveryController : ControllerBase
	{
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IUserStore<Model.Identity.IdentityUser> _userStore;
		private readonly IUserMfaStateRepository _mfaState;
		private readonly IMfaLoginTransactionService _loginTransactions;
		private readonly IFactorRecoveryService _recoveries;
		private readonly IUserPasskeyRepository _passkeys;
		private readonly IAuthenticationChallengeService _challenges;
		private readonly IMfaApprovalRequestRepository _approvals;
		private readonly IUserSessionService _sessions;
		private readonly IMfaEvidenceService _evidence;
		private readonly ISecurityNoticeService _notices;
		private readonly ISystemAuditsService _audits;

		public FactorRecoveryController(UserManager<Model.Identity.IdentityUser> userManager, IUserStore<Model.Identity.IdentityUser> userStore,
			IUserMfaStateRepository mfaState, IMfaLoginTransactionService loginTransactions, IFactorRecoveryService recoveries,
			IUserPasskeyRepository passkeys, IAuthenticationChallengeService challenges, IMfaApprovalRequestRepository approvals,
			IUserSessionService sessions, IMfaEvidenceService evidence, ISecurityNoticeService notices, ISystemAuditsService audits)
		{
			_userManager = userManager;
			_userStore = userStore;
			_mfaState = mfaState;
			_loginTransactions = loginTransactions;
			_recoveries = recoveries;
			_passkeys = passkeys;
			_challenges = challenges;
			_approvals = approvals;
			_sessions = sessions;
			_evidence = evidence;
			_notices = notices;
			_audits = audits;
		}

		private UserSessionClientApplication Client => ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]);

		/// <summary>
		/// Opens a recovery from a sign-in in progress (<c>mfa_transaction</c>) and a recovery code. The code is spent and the
		/// sign-in ends; the recovery secret is returned once. A wrong code counts against the sign-in and the account lockout.
		/// </summary>
		[HttpPost("BeginFactorRecovery")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FactorRecoveryStatusResult>> BeginFactorRecovery([FromBody] BeginFactorRecoveryInput input, CancellationToken cancellationToken)
		{
			if (!_recoveries.IsEnabled)
				return Problem(type: "recovery_unavailable", title: "Recovery is not available here. Use Resgrid on the web.", statusCode: StatusCodes.Status400BadRequest);

			var opened = await _loginTransactions.OpenAsync(input?.Transaction, Client, cancellationToken);
			if (!opened.IsUsable)
				return Problem(type: MfaLoginTransactions.ErrorCode(opened.Outcome) ?? "mfa_transaction_invalid", title: "Sign in again, then start recovery.",
					statusCode: StatusCodes.Status400BadRequest);

			var login = opened.Transaction;
			var user = await _userManager.FindByIdAsync(login.UserId);
			if (user == null || await _userManager.IsLockedOutAsync(user))
				return Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and sign in again.",
					statusCode: StatusCodes.Status429TooManyRequests);
			if (!await _userManager.GetTwoFactorEnabledAsync(user) || string.IsNullOrWhiteSpace(input.Code))
				return Problem(type: "invalid_recovery_code", title: "Enter one of your recovery codes.", statusCode: StatusCodes.Status400BadRequest);

			// The code is spent here, once (plan section 5.4); if the recovery then cannot open, the user uses another code.
			if (!(await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, input.Code.Trim())).Succeeded)
			{
				await _userManager.AccessFailedAsync(user);
				await _loginTransactions.RecordFailedAttemptAsync(login, cancellationToken);
				await AuditAsync(user, SystemAuditTypes.FactorRecoveryStarted, false, "Factor recovery refused: wrong recovery code.", cancellationToken);
				return Problem(type: "invalid_recovery_code", title: "The recovery code is invalid or has already been used.",
					statusCode: StatusCodes.Status401Unauthorized);
			}

			await _userManager.ResetAccessFailedCountAsync(user);
			await _loginTransactions.AbandonAsync(login, cancellationToken);
			FactorRecoveryStart start;
			try
			{
				start = await _recoveries.BeginAsync(login, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Framework.Logging.LogException(ex, "A factor recovery could not be opened after its recovery code was spent.");
				return Problem(type: "service_unavailable", title: "Recovery is temporarily unavailable. Try again with another recovery code.",
					statusCode: StatusCodes.Status503ServiceUnavailable);
			}

			await AuditAsync(user, SystemAuditTypes.FactorRecoveryStarted, true, "Factor recovery started with a recovery code.", cancellationToken);
			await _notices.QueueAsync(new SecurityNoticeRequest { UserId = user.Id, Kind = SecurityNoticeKind.RecoveryCodeUsed, ClientApplication = Client },
				cancellationToken);

			return Wrap(new FactorRecoveryStatusResult { Data = await StatusDataAsync(start.Transaction, start.Secret, cancellationToken) });
		}

		/// <summary>What the recovery allows next, and the passkeys the user may choose to remove. No factor secret is ever returned.</summary>
		[HttpPost("FactorRecoveryStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FactorRecoveryStatusResult>> FactorRecoveryStatus([FromBody] FactorRecoveryInput input, CancellationToken cancellationToken)
		{
			var (recovery, refusal) = await OpenAsync(input?.Transaction, cancellationToken);
			if (refusal != null)
				return refusal;

			return Wrap(new FactorRecoveryStatusResult { Data = await StatusDataAsync(recovery, null, cancellationToken) });
		}

		/// <summary>Stages the replacement authenticator; the current one keeps working until the recovery completes.</summary>
		[HttpPost("PrepareReplacement")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ReplaceTotpOptionsResult>> PrepareReplacement([FromBody] FactorRecoveryInput input, CancellationToken cancellationToken)
		{
			var (recovery, refusal) = await OpenAsync(input?.Transaction, cancellationToken);
			if (refusal != null)
				return refusal;

			var user = await _userManager.FindByIdAsync(recovery.UserId);
			var (sharedKey, uri) = await AuthenticatorSetup.StageAsync(_userManager, user);
			return Wrap(new ReplaceTotpOptionsResult
			{
				Data = new ReplaceTotpOptionsResultData
				{
					SharedKey = sharedKey,
					AuthenticatorUri = uri,
					ExpiresIn = (int)TimeSpan.FromMinutes(Math.Max(1, TwoFactorConfig.StagedAuthenticatorLifetimeMinutes)).TotalSeconds
				}
			});
		}

		/// <summary>
		/// Completes the recovery with a code from the new authenticator: it becomes the authenticator, the recovery codes are
		/// replaced (returned once), the chosen passkeys are removed, and every session and piece of evidence ends. A wrong
		/// code counts against the recovery; nothing changes until the code verifies.
		/// </summary>
		[HttpPost("CompleteFactorRecovery")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ReplaceTotpResult>> CompleteFactorRecovery([FromBody] CompleteFactorRecoveryInput input, CancellationToken cancellationToken)
		{
			var (recovery, refusal) = await OpenAsync(input?.Transaction, cancellationToken);
			if (refusal != null)
				return refusal;

			var user = await _userManager.FindByIdAsync(recovery.UserId);
			var stagedKey = await AuthenticatorSetup.GetStagedKeyAsync(_userManager, user);
			if (stagedKey == null)
				return Problem(type: "setup_expired", title: "Set up the new authenticator again.", statusCode: StatusCodes.Status400BadRequest);

			// Only the user's own active passkeys can be chosen for removal.
			var active = await _passkeys.GetActiveForUserAsync(user.Id, cancellationToken);
			var remove = (input.RemovePasskeyIds ?? new List<string>()).Distinct(StringComparer.Ordinal).ToList();
			if (remove.Any(id => active.All(p => p.UserPasskeyId != id)))
				return Problem(type: "passkey_not_found", title: "One of the passkeys to remove is not yours or is already removed.",
					statusCode: StatusCodes.Status400BadRequest);

			if (!await AuthenticatorSetup.VerifyStagedCodeAsync(_mfaState, user, stagedKey, input.Code, cancellationToken))
			{
				await _recoveries.RecordFailedAttemptAsync(recovery, cancellationToken);
				return Problem(type: "invalid_totp", title: "The code from the new authenticator is invalid or has expired.",
					statusCode: StatusCodes.Status401Unauthorized);
			}

			// One completion per recovery: a concurrent request, an expiry or exhaustion leaves the working factor untouched.
			if (!await _recoveries.TryCompleteAsync(recovery, cancellationToken))
				return Problem(type: "recovery_transaction_invalid", title: "This recovery is no longer valid. Sign in and start again.",
					statusCode: StatusCodes.Status400BadRequest);

			var now = DateTime.UtcNow;
			var shared = SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]);
			await AuthenticatorSetup.PromoteAsync(_userManager, _userStore, _mfaState, user, stagedKey,
				new TotpEnrollmentContext(shared, (int)Client, Request.Headers["X-Resgrid-Device-Name"].ToString()), cancellationToken);
			var codes = await AuthenticatorSetup.RetireOldAuthorityAsync(_userManager, _sessions, _evidence, user, cancellationToken);
			foreach (var id in remove)
				await _passkeys.TryRevokeAsync(id, user.Id, PasskeyRevocationReason.FactorRecovery, user.Id, now, cancellationToken);
			await _challenges.CancelPendingForUserAsync(user.Id, cancellationToken);
			await _approvals.CancelPendingForUserAsync(user.Id, MfaApprovalEndReason.ApproverRevoked, now, cancellationToken);

			await AuditAsync(user, SystemAuditTypes.FactorRecoveryCompleted, true,
				$"Factor recovery completed: authenticator replaced, recovery codes rotated, {remove.Count} passkey(s) removed, all sessions ended.",
				cancellationToken);
			await _notices.QueueAsync(new SecurityNoticeRequest { UserId = user.Id, Kind = SecurityNoticeKind.FactorRecoveryCompleted, ClientApplication = Client },
				cancellationToken);
			if (shared)
				await _notices.QueueAsync(new SecurityNoticeRequest { UserId = user.Id, Kind = SecurityNoticeKind.SharedInstallationFactor, ClientApplication = Client },
					cancellationToken);

			return Wrap(new ReplaceTotpResult { Data = new ReplaceTotpResultData { RecoveryCodes = codes.ToList(), SignInAgain = true } });
		}

		/// <summary>Ends the recovery; the recovery code that opened it stays spent, and nothing about the account changes.</summary>
		[HttpPost("CancelFactorRecovery")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FactorRecoveryStatusResult>> CancelFactorRecovery([FromBody] FactorRecoveryInput input, CancellationToken cancellationToken)
		{
			var (recovery, refusal) = await OpenAsync(input?.Transaction, cancellationToken);
			if (refusal != null)
				return refusal;

			var outcome = await _recoveries.CancelAsync(input.Transaction, Client, cancellationToken);
			if (outcome != FactorRecoveryOutcome.Usable)
				return Refuse(outcome);

			var user = await _userManager.FindByIdAsync(recovery.UserId);
			await AuditAsync(user, SystemAuditTypes.FactorRecoveryCanceled, true, "Factor recovery canceled.", cancellationToken);
			return Wrap(new FactorRecoveryStatusResult { Data = new FactorRecoveryStatusResultData { State = "canceled" } });
		}

		private async Task<(FactorRecoveryTransaction Recovery, ObjectResult Refusal)> OpenAsync(string secret, CancellationToken cancellationToken)
		{
			if (!_recoveries.IsEnabled)
				return (null, Problem(type: "recovery_unavailable", title: "Recovery is not available here.", statusCode: StatusCodes.Status400BadRequest));

			var opened = await _recoveries.OpenAsync(secret, Client, cancellationToken);
			return opened.IsUsable ? (opened.Transaction, null) : (null, Refuse(opened.Outcome));
		}

		private async Task<FactorRecoveryStatusResultData> StatusDataAsync(FactorRecoveryTransaction recovery, string secret, CancellationToken cancellationToken)
		{
			var passkeys = await _passkeys.GetActiveForUserAsync(recovery.UserId, cancellationToken);
			return new FactorRecoveryStatusResultData
			{
				Transaction = secret,
				State = "pending",
				ExpiresIn = Math.Max(0, (int)(recovery.ExpiresOnUtc - DateTime.UtcNow).TotalSeconds),
				NextActions = new List<string> { "prepare_replacement", "complete", "cancel" },
				Passkeys = passkeys.Select(PasskeysController.ToData).ToList()
			};
		}

		private ObjectResult Refuse(FactorRecoveryOutcome outcome) => Problem(
			type: FactorRecoveryOutcomes.ErrorCode(outcome) ?? "recovery_transaction_invalid",
			title: outcome switch
			{
				FactorRecoveryOutcome.Expired => "The recovery took too long. Sign in and start again.",
				FactorRecoveryOutcome.TooManyAttempts => "Too many wrong codes. Sign in and start again with another recovery code.",
				FactorRecoveryOutcome.SessionRevoked => "Your account's sign-in state changed. Sign in and start again.",
				FactorRecoveryOutcome.Unavailable => "Recovery is temporarily unavailable. Try again.",
				_ => "This recovery is no longer valid. Sign in and start again."
			},
			statusCode: outcome switch
			{
				FactorRecoveryOutcome.TooManyAttempts => StatusCodes.Status429TooManyRequests,
				FactorRecoveryOutcome.SessionRevoked => StatusCodes.Status401Unauthorized,
				FactorRecoveryOutcome.Unavailable => StatusCodes.Status503ServiceUnavailable,
				_ => StatusCodes.Status400BadRequest
			});

		private T Wrap<T>(T result) where T : Models.v4.StandardApiResponseV4Base
		{
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private Task AuditAsync(Model.Identity.IdentityUser user, SystemAuditTypes type, bool successful, string data, CancellationToken cancellationToken) =>
			_audits.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)type,
				UserId = user?.Id,
				Username = user?.UserName,
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = data
			}, cancellationToken);
	}
}
