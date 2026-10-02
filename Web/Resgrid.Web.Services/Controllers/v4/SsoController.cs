using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Sso;
using static OpenIddict.Abstractions.OpenIddictConstants;
using SsoBeginResult = Resgrid.Web.Services.Models.v4.Sso.SsoBeginResult;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Server-brokered SSO for every client (passkey plan section 7.7.2; workbook section 7.3). <c>Begin</c> returns the
	/// IdP URL; the IdP returns to the server, which sends a one-time code to the client's registered return target;
	/// <c>Redeem</c> exchanges that code and the PKCE verifier for the login MFA transaction (or reauthentication). No IdP
	/// token or assertion ever reaches the client, and SAML + TOTP works because nothing is resent.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[AllowAnonymous]
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): a locked
	// shared session must still unlock or end its shift, and Responder must still approve or deny.
	[Resgrid.Web.Services.Filters.AllowDuringDepartmentLock]
	public class SsoController : ControllerBase
	{
		private static readonly string[] LoginScopes = { Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.OfflineAccess, Scopes.Roles };

		private readonly ISsoBrokerService _broker;
		private readonly IMfaLoginTransactionService _loginTransactions;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IMfaEvidenceService _mfaEvidence;
		private readonly IDepartmentsService _departments;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly ISystemAuditsService _systemAudits;
		private readonly IDepartmentSsoService _departmentSso;

		public SsoController(ISsoBrokerService broker, IMfaLoginTransactionService loginTransactions, IMfaPolicyService mfaPolicy,
			IMfaEvidenceService mfaEvidence, IDepartmentsService departments, UserManager<Model.Identity.IdentityUser> userManager,
			ISystemAuditsService systemAudits, IDepartmentSsoService departmentSso)
		{
			_departmentSso = departmentSso;
			_broker = broker;
			_loginTransactions = loginTransactions;
			_mfaPolicy = mfaPolicy;
			_mfaEvidence = mfaEvidence;
			_departments = departments;
			_userManager = userManager;
			_systemAudits = systemAudits;
		}

		[HttpPost("Begin")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<SsoBeginResult>> Begin([FromBody] SsoBeginInput input, CancellationToken cancellationToken)
		{
			if (!_broker.IsEnabled)
				return Refuse(SsoBrokerOutcome.Unavailable);

			var purpose = SsoBrokerOutcomes.PurposeFrom(input?.Purpose);
			var client = ApiClientApplication.Resolve(string.IsNullOrWhiteSpace(input?.ClientApp) ? Request.Headers[ApiClientApplication.Header] : input.ClientApp);
			if (input == null || purpose == null)
				return Refuse(SsoBrokerOutcome.InvalidRequest);

			SsoBeginRequest request;
			if (purpose == SsoTransactionPurpose.StepUp && !string.IsNullOrWhiteSpace(input.Transaction))
			{
				// Provider step-up that completes a password sign-in (plan section 7.6 row 3): bound to that login transaction,
				// its account and generation, and redeemed only at Authentication/CompleteFederated.
				var opened = await _loginTransactions.OpenAsync(input.Transaction, client, cancellationToken);
				if (!opened.IsUsable)
					return Problem(type: MfaLoginTransactions.ErrorCode(opened.Outcome) ?? "mfa_transaction_invalid",
						title: "This sign-in is no longer valid. Sign in again.", statusCode: StatusCodes.Status400BadRequest);

				var login = opened.Transaction;
				if (login.DepartmentId is not int loginDepartment ||
					!await _loginTransactions.IsMethodAcceptedAsync(login, MfaEvidenceMethod.Federated, cancellationToken) ||
					!await _departmentSso.IsFederatedMfaAvailableAsync(loginDepartment, login.UserId, cancellationToken))
					return Problem(type: "mfa_method_not_allowed", title: "That verification method is not available for this sign-in.",
						statusCode: StatusCodes.Status400BadRequest);

				var department = await _departments.GetDepartmentByIdAsync(loginDepartment);
				request = NewRequest(input, department, purpose.Value, client, null, login.UserId, login.AuthenticationGeneration,
					SsoLoginTransaction.LoginOperation, login.MfaLoginTransactionId, await SharedInstallationAsync(loginDepartment, client, cancellationToken));
			}
			else if (purpose is SsoTransactionPurpose.Reauthentication or SsoTransactionPurpose.StepUp or SsoTransactionPurpose.AdpStepUp)
			{
				// Reauthentication and step-up are for the signed-in session asking, in its own department; never for another
				// account. A step-up names the operation it serves and is redeemed only at Mfa/VerifyStepUp; a Protected Data
				// Grant step-up is for the session's department and is redeemed only at DataProtection/CompleteFederated.
				var session = HttpProtectedGrantContext.SessionOf(HttpContext);
				var userId = User.FindFirst(ClaimTypes.PrimarySid)?.Value;
				if (session == null || string.IsNullOrWhiteSpace(userId) || !int.TryParse(User.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value, out var sessionDepartment))
					return Problem(type: "session_required", title: "Sign in again to reauthenticate.", statusCode: StatusCodes.Status409Conflict);

				var department = await _departments.GetDepartmentByIdAsync(sessionDepartment);
				request = NewRequest(input, department, purpose.Value, (UserSessionClientApplication)session.ClientApplication,
					session.SessionId, userId, session.AuthenticationGeneration, purpose == SsoTransactionPurpose.StepUp ? input.Operation : null, null,
					session.SharedMode || SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]));
			}
			else
			{
				// An unknown account or department reads the same as one without SSO: nothing about either is revealed.
				var department = await _broker.ResolveDepartmentAsync(input.DepartmentToken, input.DepartmentCode, input.Username, cancellationToken);
				if (department == null)
					return Refuse(SsoBrokerOutcome.Unavailable);

				request = NewRequest(input, department, purpose.Value, client, null, null, null, null, null,
					await SharedInstallationAsync(department.DepartmentId, client, cancellationToken));
			}

			var begun = await _broker.BeginAsync(request, cancellationToken);
			if (!begun.Succeeded)
				return Refuse(begun.Outcome);

			var result = new SsoBeginResult
			{
				Data = new SsoBeginResultData { AuthorizeUrl = begun.AuthorizeUrl, SsoTransactionId = begun.TransactionId, ExpiresIn = begun.ExpiresInSeconds },
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Redeems the one-time code with the PKCE verifier. A login continues exactly like a password sign-in: MFA through
		/// the login transaction when the account has it, a direct completion code when it needs none (plan section 7.6 row
		/// 4), and <c>mfa_enrollment_required</c> when the department requires MFA the account lacks.
		/// </summary>
		[HttpPost("Redeem")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<SsoRedeemResult>> Redeem([FromBody] SsoRedeemInput input, CancellationToken cancellationToken)
		{
			var client = ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]);
			var redeemed = await _broker.RedeemAsync(input?.SsoTransactionId, input?.SsoCode, input?.CodeVerifier, client, cancellationToken);
			if (!redeemed.Succeeded)
				return Refuse(redeemed.Outcome);

			var transaction = redeemed.Transaction;
			return transaction.TransactionPurpose == SsoTransactionPurpose.Reauthentication
				? await ReauthenticatedAsync(transaction, cancellationToken)
				: await LoginAsync(transaction, client, input.ClientId, cancellationToken);
		}

		private async Task<ActionResult<SsoRedeemResult>> LoginAsync(SsoLoginTransaction transaction, UserSessionClientApplication client, string clientId,
			CancellationToken cancellationToken)
		{
			var user = await _userManager.FindByIdAsync(transaction.UserId);
			if (user == null)
				return Refuse(SsoBrokerOutcome.AccessDenied);
			if (await _userManager.IsLockedOutAsync(user))
				return Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and sign in again.",
					statusCode: StatusCodes.Status429TooManyRequests);

			var membership = await _departments.GetDepartmentMemberAsync(user.Id, transaction.DepartmentId, bypassCache: true);
			if (membership == null || membership.IsDeleted || membership.IsDisabled == true)
				return Refuse(SsoBrokerOutcome.AccessDenied);

			var totpEnrolled = await _userManager.GetTwoFactorEnabledAsync(user);
			var federated = await ProviderMfaSatisfiedAsync(transaction, cancellationToken);
			var request = new MfaLoginTransactionRequest
			{
				UserId = user.Id,
				DepartmentId = transaction.DepartmentId,
				ClientApplication = client,
				ClientId = clientId,
				FirstFactorMethod = MfaEvidenceMethod.Sso,
				FirstFactorVerifiedOnUtc = transaction.AuthenticatedOnUtc ?? DateTime.UtcNow,
				DepartmentSsoConfigId = transaction.DepartmentSsoConfigId,
				AuthenticationGeneration = user.AuthenticationGeneration,
				Scopes = LoginScopes,
				TotpEnrolled = totpEnrolled,
				SharedModeRequested = SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]),
				InstallationLabel = Request.Headers["X-Resgrid-Device-Name"]
			};

			SsoRedeemResultData data;
			if (federated != null)
			{
				// The sign-in's own round trip carried the department's mapped provider MFA (plan section 7.8 flow 1): that
				// satisfies login MFA and RequireMfa with no Resgrid prompt, verified at the provider's authentication time.
				var completion = await _loginTransactions.BeginCompletedAsync(request, MfaEvidenceMethod.Federated,
					FederatedMfaMapping.FactorReferenceFor(federated.DepartmentSsoConfigId, federated.FederatedMfaMappingVersion),
					request.FirstFactorVerifiedOnUtc, cancellationToken);
				if (!completion.Succeeded)
					return Refuse(SsoBrokerOutcome.ServiceUnavailable);

				data = new SsoRedeemResultData
				{
					Outcome = "completed",
					Transaction = completion.Transaction,
					CompletionCode = completion.CompletionCode,
					ExpiresIn = completion.ExpiresInSeconds,
					MfaSatisfiedBy = MfaMethodNames.Federated
				};
			}
			else if (totpEnrolled)
			{
				var start = await _loginTransactions.BeginAsync(request, cancellationToken);
				data = new SsoRedeemResultData
				{
					Outcome = "mfa_required",
					Transaction = start.Secret,
					ExpiresIn = start.ExpiresInSeconds,
					MfaMethods = start.Choice.AllowedMethods.ToList(),
					MfaEnrolled = start.Choice.EnrolledMethods.ToList(),
					MfaPreferred = start.Choice.Preferred
				};
			}
			else if (await _mfaPolicy.DepartmentRequiresMfaAsync(transaction.DepartmentId, cancellationToken))
			{
				// RequireMfa has always applied to SSO sign-in (plan section 7.6 row 4): no tokens without enrolled MFA. The app
				// gets a setup transaction to enroll right here (plan section 6.2); it permits nothing else.
				await AuditAsync(user.Id, user.UserName, SystemAuditTypes.SsoLoginFailed, false, "Brokered SSO: mfa_enrollment_required.", cancellationToken);
				var enrollment = Problem(type: "mfa_enrollment_required",
					title: "Your department requires multi-factor authentication. Set up an authenticator app, then continue.",
					statusCode: StatusCodes.Status409Conflict);
				if (_loginTransactions.IsEnabled && enrollment.Value is ProblemDetails details)
				{
					try
					{
						var setup = await _loginTransactions.BeginAsync(request, cancellationToken);
						details.Extensions["mfa_setup_transaction"] = setup.Secret;
						details.Extensions["mfa_expires_in"] = setup.ExpiresInSeconds;
					}
					catch (Exception ex) when (!(ex is OperationCanceledException))
					{
						Framework.Logging.LogException(ex, "An MFA setup transaction could not be started.");
					}
				}

				return enrollment;
			}
			else
			{
				var completion = await _loginTransactions.BeginCompletedAsync(request, cancellationToken);
				if (!completion.Succeeded)
					return Refuse(SsoBrokerOutcome.ServiceUnavailable);

				data = new SsoRedeemResultData
				{
					Outcome = "completed",
					Transaction = completion.Transaction,
					CompletionCode = completion.CompletionCode,
					ExpiresIn = completion.ExpiresInSeconds
				};
			}

			await AuditAsync(user.Id, user.UserName, SystemAuditTypes.SsoLogin, true, $"Brokered SSO sign-in: {data.Outcome}.", cancellationToken);
			return Result(data);
		}

		/// <summary>
		/// Fresh SSO proof for the session that asked for it (plan section 6.2): the same session, account and generation,
		/// recorded as first-factor evidence at the IdP's own authentication time. It signs nobody in.
		/// </summary>
		private async Task<ActionResult<SsoRedeemResult>> ReauthenticatedAsync(SsoLoginTransaction transaction, CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			var userId = User.FindFirst(ClaimTypes.PrimarySid)?.Value;
			if (session == null || !string.Equals(session.SessionId, transaction.SessionId, StringComparison.Ordinal) ||
				!string.Equals(userId, transaction.ExpectedUserId, StringComparison.OrdinalIgnoreCase) ||
				session.AuthenticationGeneration != transaction.AuthenticationGeneration)
				return Refuse(SsoBrokerOutcome.IdentityMismatch);

			var verifiedAt = transaction.AuthenticatedOnUtc ?? DateTime.UtcNow;
			try
			{
				await _mfaEvidence.RecordAsync(userId, MfaEvidence.TrackedSessionKey(session.SessionId), (UserSessionClientApplication)session.ClientApplication,
					MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Sso, MfaEvidencePurpose.Reauthentication, verifiedAt, session.AuthenticationGeneration,
					transaction.DepartmentId, cancellationToken: cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Framework.Logging.LogException(ex, "SSO reauthentication evidence could not be recorded.");
				return Refuse(SsoBrokerOutcome.ServiceUnavailable);
			}

			await AuditAsync(userId, User.FindFirst(ClaimTypes.Name)?.Value, SystemAuditTypes.AccountReauthenticated, true,
				"Reauthenticated through the department's SSO provider.", cancellationToken);
			return Result(new SsoRedeemResultData { Outcome = "reauthenticated", VerifiedAt = verifiedAt.ToString("O") });
		}

		/// <summary>
		/// The tested SSO configuration when the sign-in's round trip carried provider MFA the department accepts for login
		/// under its current mapping; null otherwise, and the sign-in continues as any SSO first factor.
		/// </summary>
		private async Task<DepartmentSsoConfig> ProviderMfaSatisfiedAsync(SsoLoginTransaction transaction, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(transaction.FederatedMfaValue))
				return null;

			var config = await _departmentSso.GetTestedFederatedMfaConfigAsync(transaction.DepartmentId, cancellationToken);
			return FederatedMfaMapping.Satisfies(transaction, config) &&
				await _mfaPolicy.IsMethodAcceptedAsync(transaction.DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Federated, cancellationToken)
					? config
					: null;
		}

		/// <summary>
		/// Whether a sign-in is from a shared installation: it says so, or the department makes this app's sessions shared.
		/// Either way the provider is asked to let the operator choose the account (plan section 12.5.2).
		/// </summary>
		private async Task<bool> SharedInstallationAsync(int departmentId, UserSessionClientApplication client, CancellationToken cancellationToken) =>
			SharedSessionRules.IsRequested(Request.Headers[SharedSessionRules.InstallationHeader]) ||
			SharedSessionRules.IsRequiredFor(await _departmentSso.GetSecurityPolicyForDepartmentAsync(departmentId, cancellationToken), client);

		private static SsoBeginRequest NewRequest(SsoBeginInput input, Model.Department department, SsoTransactionPurpose purpose,
			UserSessionClientApplication client, string sessionId, string userId, long? generation, string operation, string loginTransactionId,
			bool sharedInstallation) => new()
		{
			DepartmentId = department?.DepartmentId ?? 0,
			DepartmentCode = department?.Code,
			Purpose = purpose,
			ClientApplication = client,
			Platform = input.Platform,
			ReturnTarget = input.ReturnTarget,
			ClientState = input.State,
			CodeChallenge = input.CodeChallenge,
			CodeChallengeMethod = input.CodeChallengeMethod,
			SessionId = sessionId,
			UserId = userId,
			AuthenticationGeneration = generation,
			Operation = operation,
			LoginTransactionId = loginTransactionId,
			SharedInstallation = sharedInstallation
		};

		private ActionResult<SsoRedeemResult> Result(SsoRedeemResultData data)
		{
			var result = new SsoRedeemResult { Data = data, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ObjectResult Refuse(SsoBrokerOutcome outcome) => Problem(
			type: SsoBrokerOutcomes.ErrorCode(outcome) ?? "sso_failed",
			title: outcome switch
			{
				SsoBrokerOutcome.Unavailable => "Single sign-on is not available for this sign-in.",
				SsoBrokerOutcome.ReturnTargetNotAllowed => "That return address is not registered for this app.",
				SsoBrokerOutcome.Expired => "The sign-in took too long. Start again.",
				SsoBrokerOutcome.IdentityMismatch => "The identity provider signed in a different account. Start again.",
				SsoBrokerOutcome.AccessDenied => "This account cannot sign in here.",
				SsoBrokerOutcome.ServiceUnavailable => "Sign-in is temporarily unavailable. Try again.",
				SsoBrokerOutcome.FederatedNotSatisfied => "Your identity provider did not confirm multi-factor authentication. Start again.",
				_ => "The sign-in could not be completed. Start again."
			},
			statusCode: outcome switch
			{
				SsoBrokerOutcome.IdentityMismatch or SsoBrokerOutcome.AccessDenied => StatusCodes.Status403Forbidden,
				SsoBrokerOutcome.VerificationFailed or SsoBrokerOutcome.ReauthenticationNotFresh or SsoBrokerOutcome.FederatedNotSatisfied =>
					StatusCodes.Status401Unauthorized,
				SsoBrokerOutcome.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
				_ => StatusCodes.Status400BadRequest
			});

		private Task AuditAsync(string userId, string userName, SystemAuditTypes type, bool successful, string data, CancellationToken cancellationToken) =>
			_systemAudits.SaveSystemAuditAsync(new SystemAudit
			{
				System = (int)SystemAuditSystems.Api,
				Type = (int)type,
				UserId = userId,
				Username = userName,
				Successful = successful,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				Data = data
			}, cancellationToken);
	}
}
