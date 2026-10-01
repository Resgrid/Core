using System;
using System.Security.Claims;
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
using Resgrid.Web.Services.Models.v4.MfaApproval;
using Resgrid.Web.Services.Models.v4.Passkeys;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Responder approval (passkey plan section 7.9; workbook section 7.4). The requesting app asks with its login
	/// transaction (sign-in) or its signed-in session (step-up) and shows the returned number; the user's own Responder
	/// reviews the request, types that number and approves with its passkey; the requester then completes through
	/// <c>Authentication/CompleteApproval</c> or <c>Mfa/VerifyStepUp</c>. Nothing here issues a token, and nothing is issued
	/// to Responder.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[AllowAnonymous]
	// Authentication and session flows stay available during a department operation lock (ADP plan section 20.2): a locked
	// shared session must still unlock or end its shift, and Responder must still approve or deny.
	[Resgrid.Web.Services.Filters.AllowDuringDepartmentLock]
	public class MfaApprovalController : ControllerBase
	{
		private readonly IMfaApprovalService _approvals;
		private readonly IMfaLoginTransactionService _loginTransactions;
		private readonly IDepartmentsService _departments;
		private readonly UserManager<Model.Identity.IdentityUser> _userManager;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _policy;
		private readonly IAdpStepUpService _adpStepUp;

		public MfaApprovalController(IMfaApprovalService approvals, IMfaLoginTransactionService loginTransactions, IDepartmentsService departments,
			UserManager<Model.Identity.IdentityUser> userManager, IMfaEvidenceService evidence, IMfaPolicyService policy, IAdpStepUpService adpStepUp)
		{
			_adpStepUp = adpStepUp;
			_evidence = evidence;
			_policy = policy;
			_approvals = approvals;
			_loginTransactions = loginTransactions;
			_departments = departments;
			_userManager = userManager;
		}

		private string SessionUserId => User.FindFirst(ClaimTypes.PrimarySid)?.Value;
		private int? SessionDepartmentId => int.TryParse(User.FindFirst(ClaimTypes.PrimaryGroupSid)?.Value, out var id) ? id : null;
		private string IpAddress => IpAddressHelper.GetRequestIP(Request, true);

		// ── Requester ─────────────────────────────────────────────────────────────────

		/// <summary>Asks the user's Responder to approve; returns the number to show on this screen only.</summary>
		[HttpPost("Request")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MfaApprovalRequestResult>> RequestApproval([FromBody] MfaApprovalRequestInput input, CancellationToken cancellationToken)
		{
			if (!_approvals.IsEnabled)
				return Refuse(MfaApprovalOutcome.Unavailable);

			MfaApprovalRequester requester;
			var purpose = input?.Purpose?.Trim().ToLowerInvariant();
			if (purpose == "login")
			{
				var (login, refusal) = await OpenLoginAsync(input.Transaction, cancellationToken);
				if (refusal != null)
					return refusal;

				var user = await _userManager.FindByIdAsync(login.UserId);
				if (user == null || await _userManager.IsLockedOutAsync(user))
					return Problem(type: "too_many_attempts", title: "Too many failed attempts. Wait a few minutes and sign in again.",
						statusCode: StatusCodes.Status429TooManyRequests);
				if (!await _loginTransactions.IsMethodAcceptedAsync(login, MfaEvidenceMethod.PasskeyApproval, cancellationToken))
					return Refuse(MfaApprovalOutcome.Unavailable);

				requester = MfaApprovalRequester.ForLoginTransaction(login, user.UserName, IpAddress);
			}
			else if (purpose == "step_up")
			{
				var session = HttpProtectedGrantContext.SessionOf(HttpContext);
				if (session == null || string.IsNullOrWhiteSpace(SessionUserId))
					return Refuse(MfaApprovalOutcome.SessionRequired);

				requester = new MfaApprovalRequester
				{
					UserId = SessionUserId,
					Kind = MfaApprovalRequesterKind.Session,
					RequesterId = session.SessionId,
					ClientApplication = (UserSessionClientApplication)session.ClientApplication,
					AuthenticationGeneration = session.AuthenticationGeneration,
					DepartmentId = SessionDepartmentId,
					Purpose = MfaApprovalPurpose.StepUp,
					Operation = input.Operation,
					SharedMode = session.SharedMode,
					LockVersion = session.SessionLockVersion,
					IpAddress = IpAddress,
					UserName = User.FindFirst(ClaimTypes.Name)?.Value,
					AuditSystem = SystemAuditSystems.Api
				};
			}
			else if (purpose == "adp")
			{
				// Access to this department's protected data (passkey plan section 7.9): the ADP issuer checks the session, the
				// department's ADP switches and version 2 issuance before asking, and consumes it at DataProtection/CompleteApproval.
				var session = HttpProtectedGrantContext.SessionOf(HttpContext);
				if (session == null || string.IsNullOrWhiteSpace(SessionUserId) || SessionDepartmentId is not int adpDepartment)
					return Refuse(MfaApprovalOutcome.SessionRequired);

				var adpStart = await _adpStepUp.RequestApprovalAsync(new AdpStepUpCaller
				{
					UserId = SessionUserId,
					UserName = User.FindFirst(ClaimTypes.Name)?.Value,
					DepartmentId = adpDepartment,
					Session = session,
					ClientApplication = (UserSessionClientApplication)session.ClientApplication,
					IpAddress = IpAddress,
					AuditSystem = SystemAuditSystems.Api
				}, cancellationToken);
				return Started(adpStart);
			}
			else
			{
				return Refuse(MfaApprovalOutcome.InvalidRequest);
			}

			return Started(await _approvals.RequestAsync(requester, cancellationToken));
		}

		private ActionResult<MfaApprovalRequestResult> Started(MfaApprovalStart start)
		{
			if (!start.Succeeded)
				return Refuse(start.Outcome);

			return Wrap(new MfaApprovalRequestResult
			{
				Data = new MfaApprovalRequestResultData { ApprovalRequestId = start.ApprovalRequestId, MatchNumber = start.MatchNumber, ExpiresIn = start.ExpiresInSeconds }
			});
		}

		/// <summary>The request's state, for the requester that made it. Poll every 2 seconds.</summary>
		[HttpPost("Status")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MfaApprovalStatusResult>> Status([FromBody] MfaApprovalReferenceInput input, CancellationToken cancellationToken)
		{
			var (kind, requesterId, refusal) = await RequesterAsync(input?.Transaction, cancellationToken);
			if (refusal != null)
				return refusal;

			var found = await _approvals.GetForRequesterAsync(input.ApprovalRequestId, kind, requesterId, cancellationToken);
			if (!found.Succeeded)
				return Refuse(found.Outcome);

			return Wrap(new MfaApprovalStatusResult
			{
				Data = new MfaApprovalStatusResultData
				{
					State = MfaApprovalOutcomes.StateName(found.Request.EffectiveState(DateTime.UtcNow)),
					ExpiresAt = found.Request.ExpiresOnUtc.ToString("O")
				}
			});
		}

		[HttpPost("Cancel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MfaApprovalStatusResult>> Cancel([FromBody] MfaApprovalReferenceInput input, CancellationToken cancellationToken)
		{
			var (kind, requesterId, refusal) = await RequesterAsync(input?.Transaction, cancellationToken);
			if (refusal != null)
				return refusal;

			var outcome = await _approvals.CancelAsync(input.ApprovalRequestId, kind, requesterId, cancellationToken);
			if (outcome != MfaApprovalOutcome.Succeeded)
				return Refuse(outcome);

			return Wrap(new MfaApprovalStatusResult { Data = new MfaApprovalStatusResultData { State = MfaApprovalOutcomes.StateName(MfaApprovalRequestState.Canceled) } });
		}

		// ── Approver (Responder) ──────────────────────────────────────────────────────

		/// <summary>The request waiting for this user's approval, for their own signed-in Responder; empty when none.</summary>
		[HttpGet("Pending")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MfaApprovalPendingResult>> Pending(CancellationToken cancellationToken)
		{
			var pending = await _approvals.GetPendingForApproverAsync(Approver(), cancellationToken);
			if (!pending.Succeeded)
				return Refuse(pending.Outcome);

			var request = pending.Request;
			if (request == null)
				return Wrap(new MfaApprovalPendingResult());

			var department = request.DepartmentId is int departmentId ? await _departments.GetDepartmentByIdAsync(departmentId) : null;
			return Wrap(new MfaApprovalPendingResult
			{
				Data = new MfaApprovalPendingResultData
				{
					ApprovalRequestId = request.MfaApprovalRequestId,
					RequestingApp = ApiPasskeys.ClientName((UserSessionClientApplication)request.ClientApplication),
					InstallationLabel = request.InstallationLabel,
					Shared = request.SharedMode,
					Department = department?.Name,
					Purpose = MfaApprovalOutcomes.PurposeName(request.RequestPurpose),
					Operation = request.Operation,
					OriginRegion = request.OriginRegion,
					CreatedAt = request.CreatedOnUtc.ToString("O"),
					ExpiresAt = request.ExpiresOnUtc.ToString("O"),
					AttemptsRemaining = Math.Max(0, request.MaxAttempts - request.Attempts)
				}
			});
		}

		/// <summary>Assertion options for this Responder's approval passkeys, bound to the request.</summary>
		[HttpPost("Options")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<PasskeyCeremonyResult>> Options([FromBody] MfaApprovalOptionsInput input, CancellationToken cancellationToken)
		{
			var (outcome, ceremony) = await _approvals.BeginApprovalAsync(Approver(), input?.ApprovalRequestId, cancellationToken);
			if (outcome != MfaApprovalOutcome.Succeeded)
				return Refuse(outcome);
			if (!ceremony.Succeeded)
				return Problem(type: PasskeyOutcomes.ErrorCode(ceremony.Outcome), title: ApiPasskeys.TitleFor(ceremony.Outcome),
					statusCode: ApiPasskeys.StatusFor(ceremony.Outcome));

			return Wrap(new PasskeyCeremonyResult
			{
				Data = new PasskeyCeremonyResultData { RequestId = ceremony.RequestId, Options = ApiPasskeys.Options(ceremony.OptionsJson) }
			});
		}

		/// <summary>Approves with the number from the requesting screen and a Responder passkey assertion.</summary>
		[HttpPost("Approve")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MfaApprovalDecisionResult>> Approve([FromBody] MfaApprovalApproveInput input, CancellationToken cancellationToken)
		{
			var (result, passkey) = await _approvals.ApproveAsync(Approver(), input?.ApprovalRequestId, input?.MatchNumber, input?.RequestId,
				ApiPasskeys.CredentialJson(input?.Credential), cancellationToken);
			if (passkey != PasskeyOutcome.Succeeded)
				return Problem(type: PasskeyOutcomes.ErrorCode(passkey), title: ApiPasskeys.TitleFor(passkey), statusCode: ApiPasskeys.StatusFor(passkey));
			if (result.Outcome == MfaApprovalOutcome.NumberMismatch)
				return Problem(type: MfaApprovalOutcomes.ErrorCode(result.Outcome),
					title: $"That is not the number on the other screen. {Math.Max(0, result.Request.MaxAttempts - result.Request.Attempts)} attempt(s) left.",
					statusCode: StatusCodes.Status400BadRequest);
			if (!result.Succeeded)
				return Refuse(result.Outcome);

			return Wrap(new MfaApprovalDecisionResult { Data = new MfaApprovalDecisionResultData { State = MfaApprovalOutcomes.StateName(MfaApprovalRequestState.Approved) } });
		}

		/// <summary>Denies the request; <c>not_me</c> also ends that sign-in and suspends approval requests for 15 minutes.</summary>
		[HttpPost("Deny")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<MfaApprovalDecisionResult>> Deny([FromBody] MfaApprovalDenyInput input, CancellationToken cancellationToken)
		{
			var reason = input?.Reason?.Trim().ToLowerInvariant() switch
			{
				"declined" or null or "" => MfaApprovalEndReason.Declined,
				"not_me" => MfaApprovalEndReason.NotMe,
				_ => (MfaApprovalEndReason)0
			};

			var result = await _approvals.DenyAsync(Approver(), input?.ApprovalRequestId, reason, cancellationToken);
			if (!result.Succeeded)
				return Refuse(result.Outcome);

			return Wrap(new MfaApprovalDecisionResult { Data = new MfaApprovalDecisionResultData { State = MfaApprovalOutcomes.StateName(MfaApprovalRequestState.Denied) } });
		}

		// ── The account page (plan section 6.5) ───────────────────────────────────────

		/// <summary>
		/// Stops approval requests on one Responder installation (<c>InstallationId</c> from <c>AccountSecurity/Methods</c>), or
		/// on all of them with <c>All</c>, which also turns approval off on every Responder passkey. Works from any app; needs
		/// the authenticator or a passkey for this app within five minutes, never an approval (plan section 7.6 row 14).
		/// </summary>
		[HttpPost("Installations/Disable")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DisableApprovalInstallationsResult>> DisableInstallations([FromBody] DisableApprovalInstallationsInput input,
			CancellationToken cancellationToken)
		{
			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			if (session == null || string.IsNullOrWhiteSpace(SessionUserId))
				return Refuse(MfaApprovalOutcome.SessionRequired);

			var installationId = input?.InstallationId?.Trim();
			if (input == null || input.All == !string.IsNullOrEmpty(installationId))
				return Problem(type: "invalid_request", title: "Choose one installation, or all of them.", statusCode: StatusCodes.Status400BadRequest);

			if (!await ApiStepUpEvidence.HasRecentSecondFactorAsync(_evidence, _policy, SessionUserId, HttpContext, SessionDepartmentId, MfaMethodScope.Account,
					MfaStepUpOperations.WindowFor(MfaStepUpOperations.AccountSecurity), cancellationToken))
				return Problem(type: "step_up_required",
					title: "Verify with your authenticator or a passkey for this app first (Mfa/VerifyStepUp, operation account_security).",
					statusCode: StatusCodes.Status403Forbidden);

			var (installations, passkeys) = await _approvals.DisableInstallationsAsync(SessionUserId, input.All ? null : installationId,
				new SharedSessionRequestInfo { UserName = User.FindFirst(ClaimTypes.Name)?.Value, IpAddress = IpAddress, CorrelationId = HttpContext.TraceIdentifier },
				cancellationToken);
			return Wrap(new DisableApprovalInstallationsResult
			{
				Data = new DisableApprovalInstallationsResultData { InstallationsStopped = installations, PasskeysStopped = passkeys }
			});
		}

		// ── Helpers ───────────────────────────────────────────────────────────────────

		/// <summary>The approver: the validated session asking, if it is one (the service checks it is a personal Responder session).</summary>
		private PasskeyCaller Approver() =>
			string.IsNullOrWhiteSpace(SessionUserId)
				? null
				: ApiPasskeys.Caller(HttpContext, SessionUserId, User.FindFirst(ClaimTypes.Name)?.Value, SessionDepartmentId);

		private async Task<(MfaLoginTransaction Transaction, ObjectResult Refusal)> OpenLoginAsync(string secret, CancellationToken cancellationToken)
		{
			var opened = await _loginTransactions.OpenAsync(secret, ApiClientApplication.Resolve(Request.Headers[ApiClientApplication.Header]), cancellationToken);
			return opened.IsUsable
				? (opened.Transaction, null)
				: (null, Problem(type: MfaLoginTransactions.ErrorCode(opened.Outcome) ?? "mfa_transaction_invalid",
					title: "This sign-in is no longer valid. Sign in again.", statusCode: StatusCodes.Status400BadRequest));
		}

		/// <summary>The requester: the login transaction whose secret was sent, otherwise the signed-in session.</summary>
		private async Task<(MfaApprovalRequesterKind Kind, string RequesterId, ObjectResult Refusal)> RequesterAsync(string transactionSecret,
			CancellationToken cancellationToken)
		{
			if (!string.IsNullOrWhiteSpace(transactionSecret))
			{
				var (login, refusal) = await OpenLoginAsync(transactionSecret, cancellationToken);
				return refusal != null ? (default, null, refusal) : (MfaApprovalRequesterKind.LoginTransaction, login.MfaLoginTransactionId, null);
			}

			var session = HttpProtectedGrantContext.SessionOf(HttpContext);
			return session == null
				? (default, null, Refuse(MfaApprovalOutcome.SessionRequired))
				: (MfaApprovalRequesterKind.Session, session.SessionId, null);
		}

		private T Wrap<T>(T result) where T : Models.v4.StandardApiResponseV4Base
		{
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private ObjectResult Refuse(MfaApprovalOutcome outcome) => Problem(
			type: MfaApprovalOutcomes.ErrorCode(outcome) ?? "approval_unavailable",
			title: outcome switch
			{
				MfaApprovalOutcome.Unavailable => "Approve with Responder is not available here.",
				MfaApprovalOutcome.Suspended => "Approval requests are paused for a few minutes. Use another verification method.",
				MfaApprovalOutcome.TooManyRequests => "Too many approval requests. Wait a few minutes or use another verification method.",
				MfaApprovalOutcome.Pending => "The request has not been approved yet.",
				MfaApprovalOutcome.Denied => "The request was denied in Responder.",
				MfaApprovalOutcome.Expired or MfaApprovalOutcome.NotFound => "The request is no longer valid. Start again.",
				MfaApprovalOutcome.SessionRequired => "Sign in again to continue.",
				MfaApprovalOutcome.ServiceUnavailable => "Approval is temporarily unavailable. Try again.",
				_ => "The request could not be processed."
			},
			statusCode: outcome switch
			{
				MfaApprovalOutcome.Suspended or MfaApprovalOutcome.TooManyRequests => StatusCodes.Status429TooManyRequests,
				MfaApprovalOutcome.SessionRequired => StatusCodes.Status409Conflict,
				MfaApprovalOutcome.Denied => StatusCodes.Status403Forbidden,
				MfaApprovalOutcome.Pending => StatusCodes.Status409Conflict,
				MfaApprovalOutcome.ServiceUnavailable => StatusCodes.Status503ServiceUnavailable,
				_ => StatusCodes.Status400BadRequest
			});
	}
}
