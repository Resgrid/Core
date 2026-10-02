using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Authentication;
using Resgrid.Web.Services.Models.v4.Passkeys;
using Resgrid.Repositories.DataRepository.Stores;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Second-factor completion for a login MFA transaction (passkey plan sections 7.2 and 7.5 rule 3; workbook section
	/// 7.1). The transaction secret from the password grant is the only authority here: no password or IdP token is
	/// resent, and nothing here issues a token. TOTP, a passkey bound to the signing-in app, or a recovery code completes it
	/// once; failures of any method share the transaction's attempt limit and the account lockout (section 7.5 rule 6).
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[AllowAnonymous]
	public class AuthenticationController : ControllerBase
	{
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IMfaLoginTransactionService _transactions;
		private readonly IPasskeyService _passkeys;
		private readonly ISystemAuditsService _systemAudits;
		private readonly ISsoBrokerService _ssoBroker;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IMfaApprovalService _approvals;
		private readonly IUserStore<Model.Identity.IdentityUser> _userStore;
		private readonly IUserMfaStateRepository _mfaState;
		private readonly ISecurityNoticeService _notices;

		private readonly IMfaActivityService _activity;

		public AuthenticationController(UserManager<Model.Identity.IdentityUser> userManager, IMfaLoginTransactionService transactions,
			IPasskeyService passkeys, ISystemAuditsService systemAudits, ISsoBrokerService ssoBroker, IDepartmentSsoService departmentSso,
			IMfaApprovalService approvals, IUserStore<Model.Identity.IdentityUser> userStore, IUserMfaStateRepository mfaState,
			ISecurityNoticeService notices, IMfaActivityService activity)
		{
			_activity = activity;
			_userStore = userStore;
			_mfaState = mfaState;
			_notices = notices;
			_approvals = approvals;
			_ssoBroker = ssoBroker;
			_departmentSso = departmentSso;
			_userManager = userManager;
			_transactions = transactions;
			_passkeys = passkeys;
			_systemAudits = systemAudits;
		}

		/// <summary>Completes the sign-in with the current authenticator code.</summary>
		[HttpPost("CompleteTotp")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LoginCompletionResult>> CompleteTotp([FromBody] CompleteLoginCodeInput input, CancellationToken cancellationToken)
		{
			var (transaction, user, refusal) = await OpenAsync(input, MfaEvidenceMethod.Totp, cancellationToken);
			if (refusal != null)
				return refusal;
			if (string.IsNullOrWhiteSpace(input.Code))
				return Refuse("invalid_totp", "A verification code is required.", StatusCodes.Status400BadRequest);

			// One-time: ResgridAuthenticatorTokenProvider accepts each time step once per user, on every surface.
			if (!await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider,
					input.Code.Replace(" ", string.Empty).Replace("-", string.Empty)))
				return await FailedAsync(transaction, user, MfaEvidenceMethod.Totp, "invalid_totp",
					"The verification code is invalid or has expired.", StatusCodes.Status401Unauthorized, cancellationToken);

			return await CompleteAsync(transaction, user, MfaEvidenceMethod.Totp, null, DateTime.UtcNow, cancellationToken);
		}

		/// <summary>Assertion options for the user's passkeys bound to the signing-in app, bound to this transaction.</summary>
		[HttpPost("PasskeyOptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyCeremonyResult>> PasskeyOptions([FromBody] LoginTransactionInput input, CancellationToken cancellationToken)
		{
			var (transaction, user, refusal) = await OpenAsync(input, MfaEvidenceMethod.Passkey, cancellationToken);
			if (refusal != null)
				return refusal;

			var start = await _passkeys.BeginAssertionAsync(PasskeyCaller.ForLoginTransaction(transaction, user.UserName, SystemAuditSystems.Api,
				IpAddressHelper.GetRequestIP(Request, true)), AuthenticationChallengePurpose.LoginSecondFactor, cancellationToken);
			if (!start.Succeeded)
				return Refuse(PasskeyOutcomes.ErrorCode(start.Outcome), ApiPasskeys.TitleFor(start.Outcome), ApiPasskeys.StatusFor(start.Outcome));

			var result = new PasskeyCeremonyResult
			{
				Data = new PasskeyCeremonyResultData { RequestId = start.RequestId, Options = ApiPasskeys.Options(start.OptionsJson) },
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Completes the sign-in with a passkey assertion for the request from <see cref="PasskeyOptions"/>.</summary>
		[HttpPost("CompletePasskey")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LoginCompletionResult>> CompletePasskey([FromBody] CompleteLoginPasskeyInput input, CancellationToken cancellationToken)
		{
			var (transaction, user, refusal) = await OpenAsync(input, MfaEvidenceMethod.Passkey, cancellationToken);
			if (refusal != null)
				return refusal;

			var assertion = await _passkeys.CompleteAssertionAsync(PasskeyCaller.ForLoginTransaction(transaction, user.UserName, SystemAuditSystems.Api,
				IpAddressHelper.GetRequestIP(Request, true)), AuthenticationChallengePurpose.LoginSecondFactor, input.RequestId,
				ApiPasskeys.CredentialJson(input.Credential), cancellationToken);
			if (!assertion.Succeeded)
			{
				// A signature that did not verify is a failed second factor like a wrong code; an expired or reused request is not.
				if (assertion.Outcome is PasskeyOutcome.VerificationFailed or PasskeyOutcome.NotRegisteredForClient)
					return await FailedAsync(transaction, user, MfaEvidenceMethod.Passkey, PasskeyOutcomes.ErrorCode(assertion.Outcome),
						ApiPasskeys.TitleFor(assertion.Outcome), ApiPasskeys.StatusFor(assertion.Outcome), cancellationToken);

				return Refuse(PasskeyOutcomes.ErrorCode(assertion.Outcome), ApiPasskeys.TitleFor(assertion.Outcome), ApiPasskeys.StatusFor(assertion.Outcome));
			}

			return await CompleteAsync(transaction, user, MfaEvidenceMethod.Passkey, UserPasskey.FactorReferenceFor(assertion.Passkey.UserPasskeyId),
				assertion.VerifiedOnUtc, cancellationToken);
		}

		/// <summary>
		/// Completes the sign-in with provider step-up (plan sections 7.6 row 3 and 7.8): the brokered round trip begun
		/// through <c>Sso/Begin</c> with purpose <c>step_up</c> and this transaction, redeemed once here with its one-time code
		/// and PKCE verifier. The provider must have signed in this account after the step-up began, with a value the
		/// department's tested mapping counts as MFA; the evidence carries the provider's authentication time.
		/// </summary>
		[HttpPost("CompleteFederated")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LoginCompletionResult>> CompleteFederated([FromBody] CompleteLoginFederatedInput input, CancellationToken cancellationToken)
		{
			var (transaction, user, refusal) = await OpenAsync(input, MfaEvidenceMethod.Federated, cancellationToken);
			if (refusal != null)
				return refusal;

			var redeemed = await _ssoBroker.RedeemAsync(input.SsoTransactionId, input.SsoCode, input.CodeVerifier,
				ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]), cancellationToken, SsoTransactionPurpose.StepUp);
			if (!redeemed.Succeeded)
				return Refuse(SsoBrokerOutcomes.ErrorCode(redeemed.Outcome) ?? "sso_failed", "The provider step-up could not be completed. Start it again.",
					redeemed.Outcome == SsoBrokerOutcome.ServiceUnavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status400BadRequest);

			// The step-up must be this sign-in's: its login transaction, account and generation, under the unchanged mapping.
			var step = redeemed.Transaction;
			var config = transaction.DepartmentId is int departmentId
				? await _departmentSso.GetTestedFederatedMfaConfigAsync(departmentId, cancellationToken)
				: null;
			if (!FederatedMfaMapping.Satisfies(step, config) || step.DepartmentId != transaction.DepartmentId ||
				!string.Equals(step.LoginTransactionId, transaction.MfaLoginTransactionId, StringComparison.Ordinal) ||
				!string.Equals(step.Operation, SsoLoginTransaction.LoginOperation, StringComparison.Ordinal) ||
				!string.Equals(step.ExpectedUserId, transaction.UserId, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(step.UserId, transaction.UserId, StringComparison.OrdinalIgnoreCase) ||
				step.AuthenticationGeneration != transaction.AuthenticationGeneration)
				return await FailedAsync(transaction, user, MfaEvidenceMethod.Federated, "federated_mfa_not_satisfied",
					"The provider step-up does not belong to this sign-in. Start it again.", StatusCodes.Status401Unauthorized, cancellationToken);

			return await CompleteAsync(transaction, user, MfaEvidenceMethod.Federated,
				FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion),
				step.AuthenticatedOnUtc ?? DateTime.UtcNow, cancellationToken);
		}

		/// <summary>
		/// Completes the sign-in with a Responder approval (plan section 7.9 step 6): the request made for this transaction
		/// through <c>MfaApproval/Request</c> and approved in the user's Responder. It is used once, while the approving
		/// passkey and Responder session still count; the evidence names them and carries the approval time.
		/// </summary>
		[HttpPost("CompleteApproval")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LoginCompletionResult>> CompleteApproval([FromBody] CompleteLoginApprovalInput input, CancellationToken cancellationToken)
		{
			var (transaction, user, refusal) = await OpenAsync(input, MfaEvidenceMethod.PasskeyApproval, cancellationToken);
			if (refusal != null)
				return refusal;

			var consumed = await _approvals.ConsumeAsync(input.ApprovalRequestId, MfaApprovalRequesterKind.LoginTransaction, transaction.MfaLoginTransactionId,
				transaction.UserId, transaction.AuthenticationGeneration, cancellationToken);
			if (!consumed.Succeeded)
				return Refuse(MfaApprovalOutcomes.ErrorCode(consumed.Outcome) ?? "approval_unavailable", consumed.Outcome switch
				{
					MfaApprovalOutcome.Pending => "The request has not been approved yet.",
					MfaApprovalOutcome.Denied => "The request was denied in Responder. Use another verification method.",
					_ => "The approval is no longer valid. Request it again."
				}, consumed.Outcome switch
				{
					MfaApprovalOutcome.Pending => StatusCodes.Status409Conflict,
					MfaApprovalOutcome.Denied => StatusCodes.Status403Forbidden,
					MfaApprovalOutcome.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
					_ => StatusCodes.Status400BadRequest
				});

			var approval = consumed.Request;
			return await CompleteAsync(transaction, user, MfaEvidenceMethod.PasskeyApproval,
				MfaApprovalRequest.FactorReferenceFor(approval.ApproverPasskeyId, approval.ApproverSessionId), approval.DecidedOnUtc ?? DateTime.UtcNow,
				cancellationToken);
		}

		/// <summary>
		/// Completes the sign-in with a one-time recovery code. The session is recovery-classified: it never satisfies a
		/// later MFA check or ADP, and the user should replace the lost factor (plan sections 6.1 item 10 and 6.3).
		/// </summary>
		[HttpPost("CompleteRecoveryCode")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LoginCompletionResult>> CompleteRecoveryCode([FromBody] CompleteLoginCodeInput input, CancellationToken cancellationToken)
		{
			var (transaction, user, refusal) = await OpenAsync(input, MfaEvidenceMethod.RecoveryCode, cancellationToken);
			if (refusal != null)
				return refusal;
			if (string.IsNullOrWhiteSpace(input.Code))
				return Refuse("invalid_recovery_code", "A recovery code is required.", StatusCodes.Status400BadRequest);

			var redeemed = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, input.Code.Trim());
			if (!redeemed.Succeeded)
				return await FailedAsync(transaction, user, MfaEvidenceMethod.RecoveryCode, "invalid_recovery_code",
					"The recovery code is invalid or has already been used.", StatusCodes.Status401Unauthorized, cancellationToken);

			await _notices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = user.Id, Kind = SecurityNoticeKind.RecoveryCodeUsed, ClientApplication = (UserSessionClientApplication)transaction.ClientApplication
			}, cancellationToken);
			return await CompleteAsync(transaction, user, MfaEvidenceMethod.RecoveryCode, null, DateTime.UtcNow, cancellationToken);
		}

		// ── Setup transaction (plan section 6.2) ─────────────────────────────────────────

		/// <summary>
		/// For a sign-in that must have MFA the account does not have yet (<c>mfa_setup_transaction</c>): stages a new
		/// authenticator key. The transaction permits only this setup; it grants nothing until a code from the new key verifies.
		/// </summary>
		[HttpPost("TotpSetupOptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<TotpSetupResult>> TotpSetupOptions([FromBody] LoginTransactionInput input, CancellationToken cancellationToken)
		{
			var (_, user, refusal) = await OpenForSetupAsync(input, cancellationToken);
			if (refusal != null)
				return refusal;

			var (sharedKey, uri) = await AuthenticatorSetup.StageAsync(_userManager, user);
			var result = new TotpSetupResult
			{
				Data = new TotpSetupResultData
				{
					SharedKey = sharedKey,
					AuthenticatorUri = uri,
					ExpiresIn = (int)TimeSpan.FromMinutes(Math.Max(1, Config.TwoFactorConfig.StagedAuthenticatorLifetimeMinutes)).TotalSeconds
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Turns the new authenticator on once its code verifies, and completes the sign-in with it: the completion code
		/// redeems at the token endpoint like any other, and the recovery codes are returned once.
		/// </summary>
		[HttpPost("CompleteTotpSetup")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LoginCompletionResult>> CompleteTotpSetup([FromBody] CompleteLoginCodeInput input, CancellationToken cancellationToken)
		{
			var (transaction, user, refusal) = await OpenForSetupAsync(input, cancellationToken);
			if (refusal != null)
				return refusal;

			var stagedKey = await AuthenticatorSetup.GetStagedKeyAsync(_userManager, user);
			if (stagedKey == null)
				return Refuse("setup_expired", "Start the authenticator setup again.", StatusCodes.Status400BadRequest);
			if (!await AuthenticatorSetup.VerifyStagedCodeAsync(_mfaState, user, stagedKey, input.Code, cancellationToken))
				return await FailedAsync(transaction, user, MfaEvidenceMethod.Totp, "invalid_totp",
					"The verification code is invalid or has expired.", StatusCodes.Status401Unauthorized, cancellationToken);

			// Set up on a shared installation when the installation says so or the department makes this app's sessions shared
			// (plan section 6.5); the flag only ever adds a warning.
			var client = (UserSessionClientApplication)transaction.ClientApplication;
			var shared = SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]) ||
				(transaction.DepartmentId is int departmentId &&
					SharedSessionRules.IsRequiredFor(await _departmentSso.GetSecurityPolicyForDepartmentAsync(departmentId, cancellationToken), client));
			await AuthenticatorSetup.PromoteAsync(_userManager, _userStore, _mfaState, user, stagedKey,
				new TotpEnrollmentContext(shared, (int)client, Request.Headers["X-Resgrid-Device-Name"].ToString()), cancellationToken);
			var codes = await AuthenticatorSetup.NewRecoveryCodesAsync(_userManager, user);
			await _systemAudits.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)SystemAuditTypes.TwoFactorEnabled,
				UserId = user.Id,
				Username = user.UserName,
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = "Authenticator set up during sign-in (setup transaction)."
			}, cancellationToken);
			await _notices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = user.Id, Kind = SecurityNoticeKind.TotpEnabled, ClientApplication = client
			}, cancellationToken);
			if (shared)
				await _notices.QueueAsync(new SecurityNoticeRequest { UserId = user.Id, Kind = SecurityNoticeKind.SharedInstallationFactor, ClientApplication = client },
					cancellationToken);

			var completed = await CompleteAsync(transaction, user, MfaEvidenceMethod.Totp, null, DateTime.UtcNow, cancellationToken);
			if (completed.Value?.Data != null)
				completed.Value.Data.RecoveryCodes = codes.ToList();
			return completed;
		}

		/// <summary>A pending transaction whose account has no authenticator yet: the only thing it may do is set one up.</summary>
		private async Task<(MfaLoginTransaction Transaction, Model.Identity.IdentityUser User, ObjectResult Refusal)> OpenForSetupAsync(
			LoginTransactionInput input, CancellationToken cancellationToken)
		{
			var opened = await _transactions.OpenAsync(input?.Transaction, ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]),
				cancellationToken);
			if (!opened.IsUsable)
				return (null, null, TransactionRefusal(opened.Outcome));

			var user = await _userManager.FindByIdAsync(opened.Transaction.UserId);
			if (user == null)
				return (null, null, TransactionRefusal(MfaLoginTransactionOutcome.SessionRevoked));
			if (await _userManager.IsLockedOutAsync(user))
				return (null, null, Refuse("too_many_attempts", "Too many failed attempts. Wait a few minutes and sign in again.",
					StatusCodes.Status429TooManyRequests));
			if (await _userManager.GetTwoFactorEnabledAsync(user))
				return (null, null, Refuse("mfa_method_not_allowed", "This account already has an authenticator. Use it to sign in.",
					StatusCodes.Status400BadRequest));

			return (opened.Transaction, user, null);
		}

		/// <summary>
		/// The pending transaction for this client and its user, or the refusal: an unusable transaction, a locked account,
		/// a method the department does not accept, or TOTP-based methods (TOTP and recovery codes) for an account without TOTP.
		/// </summary>
		private async Task<(MfaLoginTransaction Transaction, Model.Identity.IdentityUser User, ObjectResult Refusal)> OpenAsync(
			LoginTransactionInput input, MfaEvidenceMethod method, CancellationToken cancellationToken)
		{
			var opened = await _transactions.OpenAsync(input?.Transaction, ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]),
				cancellationToken);
			if (!opened.IsUsable)
				return (null, null, TransactionRefusal(opened.Outcome));

			var transaction = opened.Transaction;
			var user = await _userManager.FindByIdAsync(transaction.UserId);
			if (user == null)
				return (null, null, TransactionRefusal(MfaLoginTransactionOutcome.SessionRevoked));

			if (await _userManager.IsLockedOutAsync(user))
				return (null, null, Refuse("too_many_attempts", "Too many failed attempts. Wait a few minutes and sign in again.",
					StatusCodes.Status429TooManyRequests));

			if (((method is MfaEvidenceMethod.Totp or MfaEvidenceMethod.RecoveryCode) && !await _userManager.GetTwoFactorEnabledAsync(user)) ||
				!await _transactions.IsMethodAcceptedAsync(transaction, method, cancellationToken))
				return (null, null, Refuse("mfa_method_not_allowed", "That verification method is not available for this sign-in.",
					StatusCodes.Status400BadRequest));

			return (transaction, user, null);
		}

		private async Task<ObjectResult> FailedAsync(MfaLoginTransaction transaction, Model.Identity.IdentityUser user, MfaEvidenceMethod method,
			string type, string title, int status, CancellationToken cancellationToken)
		{
			await _userManager.AccessFailedAsync(user);
			await _transactions.RecordFailedAttemptAsync(transaction, cancellationToken);
			await AuditAsync(user, false, method, cancellationToken);
			await _activity.RecordAsync(new MfaActivityEntry
			{
				UserId = user.Id, Method = method, Purpose = MfaEvidencePurpose.Login, Successful = false,
				ClientApplication = (UserSessionClientApplication)transaction.ClientApplication, DepartmentId = transaction.DepartmentId,
				InstallationLabel = Request.Headers["X-Resgrid-Device-Name"].ToString()
			}, cancellationToken);
			return Refuse(type, title, status);
		}

		private async Task<ActionResult<LoginCompletionResult>> CompleteAsync(MfaLoginTransaction transaction, Model.Identity.IdentityUser user,
			MfaEvidenceMethod method, string factorReference, DateTime verifiedOnUtc, CancellationToken cancellationToken)
		{
			await _userManager.ResetAccessFailedCountAsync(user);

			MfaLoginCompletion completion;
			try
			{
				completion = await _transactions.CompleteAsync(transaction, method, factorReference, verifiedOnUtc, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Framework.Logging.LogException(ex, "A verified login second factor could not be recorded; the sign-in was refused.");
				return TransactionRefusal(MfaLoginTransactionOutcome.Unavailable);
			}

			if (!completion.Succeeded)
				return TransactionRefusal(completion.Outcome);

			await AuditAsync(user, true, method, cancellationToken);
			var result = new LoginCompletionResult
			{
				Data = new LoginCompletionResultData
				{
					CompletionCode = completion.CompletionCode,
					ExpiresIn = completion.ExpiresInSeconds,
					Recovery = method == MfaEvidenceMethod.RecoveryCode
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ObjectResult TransactionRefusal(MfaLoginTransactionOutcome outcome) => Refuse(MfaLoginTransactions.ErrorCode(outcome) ?? "mfa_transaction_invalid",
			outcome switch
			{
				MfaLoginTransactionOutcome.Expired => "The sign-in took too long. Sign in again.",
				MfaLoginTransactionOutcome.TooManyAttempts => "Too many failed attempts. Sign in again.",
				MfaLoginTransactionOutcome.PolicyChanged => "Your department's sign-in policy changed. Sign in again.",
				MfaLoginTransactionOutcome.SessionRevoked => "Your account's sign-in state changed. Sign in again.",
				MfaLoginTransactionOutcome.Unavailable => "Sign-in is temporarily unavailable. Try again.",
				_ => "This sign-in is no longer valid. Sign in again."
			},
			outcome switch
			{
				MfaLoginTransactionOutcome.TooManyAttempts => StatusCodes.Status429TooManyRequests,
				MfaLoginTransactionOutcome.PolicyChanged => StatusCodes.Status409Conflict,
				MfaLoginTransactionOutcome.SessionRevoked => StatusCodes.Status401Unauthorized,
				MfaLoginTransactionOutcome.Unavailable => StatusCodes.Status503ServiceUnavailable,
				_ => StatusCodes.Status400BadRequest
			});

		private ObjectResult Refuse(string type, string title, int status) => Problem(type: type, title: title, statusCode: status);

		private Task AuditAsync(Model.Identity.IdentityUser user, bool successful, MfaEvidenceMethod method, CancellationToken cancellationToken) =>
			_systemAudits.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)SystemAuditTypes.Login,
				UserId = user.Id,
				Username = user.UserName,
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = $"Login MFA transaction with {(method == MfaEvidenceMethod.RecoveryCode ? "a recovery code" : MfaMethodNames.From(method))}: " +
					(successful ? "second factor verified; completion code issued." : "verification failed.")
			}, cancellationToken);
	}
}
