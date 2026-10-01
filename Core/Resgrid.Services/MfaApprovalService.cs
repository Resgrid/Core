using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IMfaApprovalService"/>
	public sealed class MfaApprovalService : IMfaApprovalService
	{
		internal const string PushTitle = "Sign-in approval requested";
		internal const string PushBody = "Open Resgrid Responder to review.";

		/// <summary>The Novu event code prefix Responder recognizes; the push carries nothing else (workbook section 7.4).</summary>
		internal const string PushEventPrefix = "NA:";

		private readonly IMfaApprovalRequestRepository _requests;
		private readonly IUserPasskeyRepository _passkeyRows;
		private readonly IUserSessionsRepository _sessions;
		private readonly IIdentityUserRepository _identityUsers;
		private readonly IMfaLoginTransactionRepository _loginTransactions;
		private readonly IPasskeyService _passkeys;
		private readonly IRelyingPartyRegistry _registry;
		private readonly IPasskeyFeatureGates _gates;
		private readonly IMfaPolicyService _policy;
		private readonly IDepartmentsService _departments;
		private readonly INovuProvider _novu;
		private readonly IIpLocationProvider _location;
		private readonly ISystemAuditsService _audits;
		private readonly ISecurityNoticeService _notices;
		private readonly ISessionEventPublisher _sessionEvents;
		private readonly IMfaActivityService _activity;
		private readonly TimeProvider _time;

		public MfaApprovalService(IMfaApprovalRequestRepository requests, IUserPasskeyRepository passkeyRows, IUserSessionsRepository sessions,
			IIdentityUserRepository identityUsers, IMfaLoginTransactionRepository loginTransactions, IPasskeyService passkeys, IRelyingPartyRegistry registry,
			IPasskeyFeatureGates gates, IMfaPolicyService policy, IDepartmentsService departments, INovuProvider novu, IIpLocationProvider location,
			ISystemAuditsService audits, ISecurityNoticeService notices, ISessionEventPublisher sessionEvents, IMfaActivityService activity, TimeProvider time)
		{
			_activity = activity;
			_sessionEvents = sessionEvents;
			_notices = notices;
			_requests = requests;
			_passkeyRows = passkeyRows;
			_sessions = sessions;
			_identityUsers = identityUsers;
			_loginTransactions = loginTransactions;
			_passkeys = passkeys;
			_registry = registry;
			_gates = gates;
			_policy = policy;
			_departments = departments;
			_novu = novu;
			_location = location;
			_audits = audits;
			_time = time;
		}

		private static TimeSpan Lifetime => TimeSpan.FromSeconds(Math.Max(30, PasskeyConfig.ApprovalRequestLifetimeSeconds));
		private static TimeSpan RateWindow => TimeSpan.FromMinutes(Math.Max(1, PasskeyConfig.ApprovalRateWindowMinutes));
		private static TimeSpan SuspensionWindow => TimeSpan.FromMinutes(Math.Max(1, PasskeyConfig.ApprovalSuspensionMinutes));
		private static TimeSpan ConsumeGrace => TimeSpan.FromSeconds(Math.Max(0, PasskeyConfig.ApprovalConsumeGraceSeconds));

		public bool IsEnabled => _gates.ResponderApprovalEnabled;

		public async Task<bool> IsAvailableAsync(string userId, UserSessionClientApplication requestingClient, CancellationToken cancellationToken = default)
		{
			if (!IsEnabled || requestingClient == UserSessionClientApplication.Responder || string.IsNullOrWhiteSpace(userId))
				return false;

			try
			{
				return (await EligibleApproverSessionsAsync(userId, null, cancellationToken)).Count > 0;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Only whether to offer the method: without an answer it is simply not offered.
				Logging.LogException(ex, "Responder approval availability could not be read.");
				return false;
			}
		}

		// ── Requester ─────────────────────────────────────────────────────────────────

		public async Task<MfaApprovalStart> RequestAsync(MfaApprovalRequester requester, CancellationToken cancellationToken = default)
		{
			if (!IsEnabled)
				return MfaApprovalStart.Of(MfaApprovalOutcome.Unavailable);
			if (requester == null || string.IsNullOrWhiteSpace(requester.UserId) || string.IsNullOrWhiteSpace(requester.RequesterId))
				return MfaApprovalStart.Of(MfaApprovalOutcome.InvalidRequest);
			if (requester.Purpose == MfaApprovalPurpose.StepUp && !MfaStepUpOperations.IsKnown(requester.Operation))
				return MfaApprovalStart.Of(MfaApprovalOutcome.InvalidRequest);

			// Responder approves; it never asks. Department entry arrives with its slice. An unlock is asked for by a locked shared
			// session and is bound to its lock version, so a lock or operator change voids it (plan section 7.9). Protected data is
			// asked for by a signed-in session in one department, never by a sign-in that has no session yet.
			if (requester.ClientApplication == UserSessionClientApplication.Responder ||
				requester.Purpose is not (MfaApprovalPurpose.Login or MfaApprovalPurpose.StepUp or MfaApprovalPurpose.Adp or MfaApprovalPurpose.Unlock))
				return MfaApprovalStart.Of(MfaApprovalOutcome.Unavailable);
			if (requester.Purpose == MfaApprovalPurpose.Unlock &&
				(requester.Kind != MfaApprovalRequesterKind.Session || !requester.SharedMode || requester.LockVersion == null))
				return MfaApprovalStart.Of(MfaApprovalOutcome.InvalidRequest);
			if (requester.Purpose == MfaApprovalPurpose.Adp && (requester.Kind != MfaApprovalRequesterKind.Session || requester.DepartmentId is not > 0))
				return MfaApprovalStart.Of(MfaApprovalOutcome.InvalidRequest);

			try
			{
				// The department must accept approval for this row: never security changes or account factors (plan section 7.6).
				if (!await _policy.IsMethodAcceptedAsync(requester.DepartmentId, requester.Scope, MfaEvidenceMethod.PasskeyApproval, cancellationToken))
					return MfaApprovalStart.Of(MfaApprovalOutcome.Unavailable);

				var user = await _identityUsers.GetByIdAsync(requester.UserId);
				if (user == null || user.AuthenticationGeneration != requester.AuthenticationGeneration)
					return MfaApprovalStart.Of(MfaApprovalOutcome.NotFound);

				var now = _time.GetUtcNow().UtcDateTime;
				if (await IsSuspendedAsync(requester.UserId, now, cancellationToken))
				{
					// Expiries only show up here, so the pause is announced once per window (plan section 7.9 abuse controls).
					await _notices.QueueOnceAsync(new SecurityNoticeRequest { UserId = requester.UserId, Kind = SecurityNoticeKind.ApprovalSuspended },
						SuspensionWindow, cancellationToken);
					return MfaApprovalStart.Of(MfaApprovalOutcome.Suspended);
				}
				if (await _requests.CountCreatedSinceAsync(requester.UserId, now - RateWindow, cancellationToken) >= Math.Max(1, PasskeyConfig.ApprovalMaxRequestsPerWindow))
					return MfaApprovalStart.Of(MfaApprovalOutcome.TooManyRequests);

				var approvers = await EligibleApproverSessionsAsync(requester.UserId,
					requester.Kind == MfaApprovalRequesterKind.Session ? requester.RequesterId : null, cancellationToken);
				if (approvers.Count == 0)
					return MfaApprovalStart.Of(MfaApprovalOutcome.Unavailable);

				// One pending request per user: a new one replaces the old, which can no longer be approved.
				await _requests.CancelPendingForUserAsync(requester.UserId, MfaApprovalEndReason.Superseded, now, cancellationToken);

				var (label, region) = await RequesterContextAsync(requester, cancellationToken);
				var id = Guid.NewGuid().ToString();
				var number = RandomNumberGenerator.GetInt32(10, 100).ToString(CultureInfo.InvariantCulture);
				var request = new MfaApprovalRequest
				{
					MfaApprovalRequestId = id,
					UserId = requester.UserId,
					RequesterKind = (int)requester.Kind,
					RequesterId = requester.RequesterId,
					ClientApplication = (int)requester.ClientApplication,
					InstallationLabel = Limit(label, 256),
					SharedMode = requester.SharedMode,
					DepartmentId = requester.DepartmentId,
					Purpose = (int)requester.Purpose,
					Operation = requester.Purpose == MfaApprovalPurpose.StepUp ? requester.Operation : null,
					LockVersion = requester.LockVersion,
					AuthenticationGeneration = requester.AuthenticationGeneration,
					MatchNumberHash = MfaApprovalRequest.HashMatchNumber(id, number),
					OriginRegion = Limit(region, 256),
					State = (int)MfaApprovalRequestState.Pending,
					Version = 1,
					MaxAttempts = Math.Max(1, PasskeyConfig.ApprovalMaxNumberAttempts),
					CreatedOnUtc = now,
					ExpiresOnUtc = now.Add(Lifetime)
				};

				// A concurrent request for the same user won the one pending slot.
				if (!await _requests.TryInsertPendingAsync(request, now, cancellationToken))
					return MfaApprovalStart.Of(MfaApprovalOutcome.TooManyRequests);

				await NotifyAsync(requester.UserId, approvers, id);
				await AuditAsync(requester.UserId, requester.UserName, requester.AuditSystem, requester.IpAddress, SystemAuditTypes.MfaApprovalRequested, true,
					$"Responder approval requested for {MfaApprovalOutcomes.PurposeName(requester.Purpose)}" +
					$"{(requester.Operation == null ? "" : $" ({requester.Operation})")} from {PasskeyService.ClientLabel(requester.ClientApplication)}; request {id}.",
					cancellationToken);

				return new MfaApprovalStart
				{
					Outcome = MfaApprovalOutcome.Succeeded,
					ApprovalRequestId = id,
					MatchNumber = number,
					ExpiresInSeconds = (int)Lifetime.TotalSeconds
				};
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Responder approval request could not be created.");
				return MfaApprovalStart.Of(MfaApprovalOutcome.ServiceUnavailable);
			}
		}

		public async Task<MfaApprovalResult> GetForRequesterAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidId(approvalRequestId) || string.IsNullOrWhiteSpace(requesterId))
				return MfaApprovalResult.Of(MfaApprovalOutcome.InvalidRequest);

			try
			{
				var request = await _requests.GetAsync(approvalRequestId, cancellationToken);
				return IsRequester(request, requesterKind, requesterId)
					? MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, request)
					: MfaApprovalResult.Of(MfaApprovalOutcome.NotFound);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Responder approval request could not be read.");
				return MfaApprovalResult.Of(MfaApprovalOutcome.ServiceUnavailable);
			}
		}

		public async Task<MfaApprovalOutcome> CancelAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId,
			CancellationToken cancellationToken = default)
		{
			if (!IsValidId(approvalRequestId) || string.IsNullOrWhiteSpace(requesterId))
				return MfaApprovalOutcome.InvalidRequest;

			try
			{
				return await _requests.TryCancelAsync(approvalRequestId, requesterKind, requesterId, _time.GetUtcNow().UtcDateTime, cancellationToken)
					? MfaApprovalOutcome.Succeeded
					: MfaApprovalOutcome.NotFound;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Responder approval request could not be canceled.");
				return MfaApprovalOutcome.ServiceUnavailable;
			}
		}

		public async Task<MfaApprovalResult> ConsumeAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId, string userId,
			long authenticationGeneration, CancellationToken cancellationToken = default)
		{
			if (!IsEnabled)
				return MfaApprovalResult.Of(MfaApprovalOutcome.Unavailable);
			if (!IsValidId(approvalRequestId) || string.IsNullOrWhiteSpace(requesterId))
				return MfaApprovalResult.Of(MfaApprovalOutcome.InvalidRequest);

			try
			{
				var request = await _requests.GetAsync(approvalRequestId, cancellationToken);
				if (!IsRequester(request, requesterKind, requesterId) || !SameUser(request.UserId, userId) ||
					request.AuthenticationGeneration != authenticationGeneration)
					return MfaApprovalResult.Of(MfaApprovalOutcome.NotFound);

				var now = _time.GetUtcNow().UtcDateTime;
				switch (request.EffectiveState(now))
				{
					case MfaApprovalRequestState.Pending:
						return MfaApprovalResult.Of(MfaApprovalOutcome.Pending, request);
					case MfaApprovalRequestState.Denied:
						return MfaApprovalResult.Of(MfaApprovalOutcome.Denied, request);
					case MfaApprovalRequestState.Approved:
						break;
					case MfaApprovalRequestState.Consumed:
						return MfaApprovalResult.Of(MfaApprovalOutcome.NotFound);
					default:
						return MfaApprovalResult.Of(MfaApprovalOutcome.Expired, request);
				}

				// The approving passkey and Responder session must still count when the approval is used.
				var reference = MfaApprovalRequest.FactorReferenceFor(request.ApproverPasskeyId, request.ApproverSessionId);
				if (!await ApprovalApprovers.IsValidAsync(_passkeyRows, _sessions, request.UserId, reference, authenticationGeneration, now, cancellationToken))
					return MfaApprovalResult.Of(MfaApprovalOutcome.Expired, request);

				if (!await _requests.TryConsumeAsync(approvalRequestId, requesterKind, requesterId, now, ConsumeGrace, cancellationToken))
					return MfaApprovalResult.Of(MfaApprovalOutcome.Expired, request);

				request.State = (int)MfaApprovalRequestState.Consumed;
				request.ConsumedOnUtc = now;
				return MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, request);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Responder approval could not be used; the verification was refused.");
				return MfaApprovalResult.Of(MfaApprovalOutcome.ServiceUnavailable);
			}
		}

		public Task<bool> IsApproverValidAsync(string userId, string factorReference, long authenticationGeneration, CancellationToken cancellationToken = default) =>
			ApprovalApprovers.IsValidAsync(_passkeyRows, _sessions, userId, factorReference, authenticationGeneration, _time.GetUtcNow().UtcDateTime,
				cancellationToken);

		// ── Approver (Responder) ──────────────────────────────────────────────────────

		public async Task<MfaApprovalResult> GetPendingForApproverAsync(PasskeyCaller approver, CancellationToken cancellationToken = default)
		{
			if (!IsEnabled)
				return MfaApprovalResult.Of(MfaApprovalOutcome.Unavailable);
			if (!IsApproverCaller(approver))
				return MfaApprovalResult.Of(MfaApprovalOutcome.SessionRequired);

			try
			{
				if (!await HasApprovingPasskeyAsync(approver.UserId, cancellationToken) || !await IsEligibleApproverSessionAsync(approver, cancellationToken))
					return MfaApprovalResult.Of(MfaApprovalOutcome.Unavailable);

				var request = await _requests.GetPendingForUserAsync(approver.UserId, _time.GetUtcNow().UtcDateTime, cancellationToken);
				return MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded,
					request != null && request.AuthenticationGeneration == approver.AuthenticationGeneration ? request : null);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Pending Responder approval requests could not be read.");
				return MfaApprovalResult.Of(MfaApprovalOutcome.ServiceUnavailable);
			}
		}

		public async Task<(MfaApprovalOutcome Outcome, PasskeyCeremonyStart Ceremony)> BeginApprovalAsync(PasskeyCaller approver, string approvalRequestId,
			CancellationToken cancellationToken = default)
		{
			var open = await OpenForApproverAsync(approver, approvalRequestId, cancellationToken);
			if (!open.Succeeded)
				return (open.Outcome, null);

			return (MfaApprovalOutcome.Succeeded,
				await _passkeys.BeginAssertionAsync(approver.ForApprovalRequest(approvalRequestId), AuthenticationChallengePurpose.ApprovalResponse, cancellationToken));
		}

		public async Task<(MfaApprovalResult Result, PasskeyOutcome Passkey)> ApproveAsync(PasskeyCaller approver, string approvalRequestId,
			string matchNumber, string requestId, string credentialJson, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(matchNumber) || matchNumber.Trim().Length != 2 || !matchNumber.Trim().All(char.IsAsciiDigit))
				return (MfaApprovalResult.Of(MfaApprovalOutcome.InvalidRequest), PasskeyOutcome.Succeeded);

			var open = await OpenForApproverAsync(approver, approvalRequestId, cancellationToken);
			if (!open.Succeeded)
				return (open, PasskeyOutcome.Succeeded);

			var request = open.Request;
			try
			{
				// The number first: a wrong one counts against the request, and the passkey ceremony stays usable for a retry.
				var now = _time.GetUtcNow().UtcDateTime;
				if (!CryptographicOperations.FixedTimeEquals(MfaApprovalRequest.HashMatchNumber(approvalRequestId, matchNumber), request.MatchNumberHash))
				{
					var state = await _requests.RecordWrongNumberAsync(approvalRequestId, now, cancellationToken);
					if (state == MfaApprovalRequestState.Denied)
					{
						await AuditAsync(approver.UserId, approver.UserName, approver.AuditSystem, approver.IpAddress, SystemAuditTypes.MfaApprovalDenied, false,
							$"Responder approval request {approvalRequestId} denied after too many wrong numbers.", cancellationToken);
						await NotifyRequesterAsync(request, MfaApprovalRequestState.Denied, cancellationToken);
						await RecordDeniedAsync(request, approver.SessionId, cancellationToken);
						return (MfaApprovalResult.Of(MfaApprovalOutcome.Denied, request), PasskeyOutcome.Succeeded);
					}

					if (state == null)
						return (MfaApprovalResult.Of(MfaApprovalOutcome.Expired), PasskeyOutcome.Succeeded);

					request.Attempts++;
					return (MfaApprovalResult.Of(MfaApprovalOutcome.NumberMismatch, request), PasskeyOutcome.Succeeded);
				}

				var assertion = await _passkeys.CompleteAssertionAsync(approver.ForApprovalRequest(approvalRequestId),
					AuthenticationChallengePurpose.ApprovalResponse, requestId, credentialJson, cancellationToken);
				if (!assertion.Succeeded)
					return (MfaApprovalResult.Of(MfaApprovalOutcome.InvalidRequest, request), assertion.Outcome);

				if (!await _requests.TryApproveAsync(approvalRequestId, approver.SessionId, assertion.Passkey.UserPasskeyId, _time.GetUtcNow().UtcDateTime,
						cancellationToken))
					return (MfaApprovalResult.Of(MfaApprovalOutcome.Expired), PasskeyOutcome.Succeeded);

				await AuditAsync(approver.UserId, approver.UserName, approver.AuditSystem, approver.IpAddress, SystemAuditTypes.MfaApprovalApproved, true,
					$"Responder approval request {approvalRequestId} approved with passkey {assertion.Passkey.UserPasskeyId} from session {approver.SessionId}.",
					cancellationToken);
				request.State = (int)MfaApprovalRequestState.Approved;
				await NotifyRequesterAsync(request, MfaApprovalRequestState.Approved, cancellationToken);
				return (MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, request), PasskeyOutcome.Succeeded);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Responder approval could not be recorded.");
				return (MfaApprovalResult.Of(MfaApprovalOutcome.ServiceUnavailable), PasskeyOutcome.Succeeded);
			}
		}

		public async Task<MfaApprovalResult> DenyAsync(PasskeyCaller approver, string approvalRequestId, MfaApprovalEndReason reason,
			CancellationToken cancellationToken = default)
		{
			if (reason is not (MfaApprovalEndReason.Declined or MfaApprovalEndReason.NotMe))
				return MfaApprovalResult.Of(MfaApprovalOutcome.InvalidRequest);

			var open = await OpenForApproverAsync(approver, approvalRequestId, cancellationToken);
			if (!open.Succeeded)
				return open;

			var request = open.Request;
			try
			{
				if (!await _requests.TryDenyAsync(approvalRequestId, reason, _time.GetUtcNow().UtcDateTime, cancellationToken))
					return MfaApprovalResult.Of(MfaApprovalOutcome.Expired);

				// "Not me": the first factor was used by someone else, so that sign-in ends here (plan section 7.9 step 5).
				var abandoned = false;
				if (reason == MfaApprovalEndReason.NotMe && request.Requester == MfaApprovalRequesterKind.LoginTransaction)
					abandoned = await _loginTransactions.TryAbandonAsync(request.RequesterId, cancellationToken);

				await AuditAsync(approver.UserId, approver.UserName, approver.AuditSystem, approver.IpAddress, SystemAuditTypes.MfaApprovalDenied, false,
					reason == MfaApprovalEndReason.NotMe
						? $"Responder approval request {approvalRequestId} denied: the user did not request it. " +
							(abandoned ? "The sign-in was ended; change the password, because it was used. " : "") +
							"Approval requests are suspended."
						: $"Responder approval request {approvalRequestId} declined.",
					cancellationToken);

				// "Not me" is its own notice (it also pauses requests); a second plain denial in a row pauses them too.
				var notice = new SecurityNoticeRequest
				{
					UserId = request.UserId,
					Kind = reason == MfaApprovalEndReason.NotMe ? SecurityNoticeKind.ApprovalNotMe : SecurityNoticeKind.ApprovalSuspended,
					ClientApplication = (UserSessionClientApplication)request.ClientApplication,
					InstallationLabel = request.InstallationLabel,
					Region = request.OriginRegion
				};
				if (reason == MfaApprovalEndReason.NotMe)
					await _notices.QueueAsync(notice, cancellationToken);
				else if (await IsSuspendedAsync(request.UserId, _time.GetUtcNow().UtcDateTime, cancellationToken))
					await _notices.QueueOnceAsync(notice, SuspensionWindow, cancellationToken);

				request.State = (int)MfaApprovalRequestState.Denied;
				request.EndReason = (int)reason;
				await NotifyRequesterAsync(request, MfaApprovalRequestState.Denied, cancellationToken);
				await RecordDeniedAsync(request, approver.SessionId, cancellationToken);
				return MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, request);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Responder approval could not be denied.");
				return MfaApprovalResult.Of(MfaApprovalOutcome.ServiceUnavailable);
			}
		}

		// ── Rules ─────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Two denials or expiries in a row, or one "not me" denial, suspend new requests until the suspension window has
		/// passed since the last of them (plan section 7.9 abuse controls). Canceled and superseded requests do not count.
		/// </summary>
		private async Task<bool> IsSuspendedAsync(string userId, DateTime utcNow, CancellationToken cancellationToken)
		{
			var since = utcNow - SuspensionWindow;
			var decided = (await _requests.GetRecentForUserAsync(userId, 10, cancellationToken))
				.Where(r => r.EffectiveState(utcNow) is not (MfaApprovalRequestState.Pending or MfaApprovalRequestState.Canceled))
				.OrderByDescending(r => r.CreatedOnUtc)
				.ToList();

			DateTime Ended(MfaApprovalRequest r) => r.DecidedOnUtc ?? r.ExpiresOnUtc;
			bool Failed(MfaApprovalRequest r) => r.EffectiveState(utcNow) is MfaApprovalRequestState.Denied or MfaApprovalRequestState.Expired;

			if (decided.Any(r => r.RequestState == MfaApprovalRequestState.Denied && r.EndReason == (int)MfaApprovalEndReason.NotMe && Ended(r) > since))
				return true;

			return decided.Count >= 2 && Failed(decided[0]) && Failed(decided[1]) && Ended(decided[0]) > since;
		}

		/// <summary>The user's Responder sessions that can approve now, excluding the requester's own session.</summary>
		/// <summary>A denied approval is a denied verification in the requester's recent activity (plan section 6.5).</summary>
		private Task RecordDeniedAsync(MfaApprovalRequest request, string approverSessionId, CancellationToken cancellationToken) =>
			_activity.RecordAsync(new MfaActivityEntry
			{
				UserId = request.UserId,
				Method = MfaEvidenceMethod.PasskeyApproval,
				Purpose = request.RequestPurpose switch
				{
					MfaApprovalPurpose.StepUp => MfaEvidencePurpose.StepUp,
					MfaApprovalPurpose.Adp => MfaEvidencePurpose.AdpStepUp,
					MfaApprovalPurpose.Unlock => MfaEvidencePurpose.SharedUnlock,
					_ => MfaEvidencePurpose.Login
				},
				Successful = false,
				ClientApplication = (UserSessionClientApplication)request.ClientApplication,
				InstallationLabel = request.InstallationLabel,
				SharedMode = request.SharedMode,
				DepartmentId = request.DepartmentId,
				SessionId = request.Requester == MfaApprovalRequesterKind.Session ? request.RequesterId : null,
				ApproverSessionId = approverSessionId
			}, cancellationToken);

		/// <summary>
		/// Tells a signed-in requester its request was decided, over its own realtime connection, so it need not wait for the
		/// next poll (workbook section 7.4). A sign-in in progress has no connection and keeps polling. Best effort: the
		/// decision is already committed, and the state is all the event carries.
		/// </summary>
		private async Task NotifyRequesterAsync(MfaApprovalRequest request, MfaApprovalRequestState state, CancellationToken cancellationToken)
		{
			if (request?.Requester != MfaApprovalRequesterKind.Session)
				return;

			try
			{
				await _sessionEvents.PublishAsync(request.RequesterId, new SessionEventMessage
				{
					Name = SessionEvents.MfaApprovalChanged,
					ApprovalRequestId = request.MfaApprovalRequestId,
					State = MfaApprovalOutcomes.StateName(state)
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "An approval decision event could not be sent; the requester keeps polling.");
			}
		}

		public async Task<(int Installations, int Passkeys)> DisableInstallationsAsync(string userId, string installationId, SharedSessionRequestInfo request,
			CancellationToken cancellationToken = default)
		{
			var now = _time.GetUtcNow().UtcDateTime;
			var installations = await _sessions.DisableApprovalsAsync(userId, installationId, now, cancellationToken);
			var passkeys = 0;
			if (installationId == null)
				foreach (var passkey in await _passkeyRows.GetActiveForUserAsync(userId, cancellationToken) ?? Array.Empty<UserPasskey>())
					if (ApprovalApprovers.IsApprovingPasskey(passkey, userId) && await _passkeyRows.TrySetApprovalEnabledAsync(passkey.UserPasskeyId, userId, false,
							cancellationToken))
						passkeys++;

			if (installations + passkeys == 0)
				return (0, 0);

			// A request waiting for a stopped installation ends now; approvals it gave stop counting at their next read.
			await _requests.CancelPendingForUserAsync(userId, MfaApprovalEndReason.ApproverRevoked, now, cancellationToken);
			await AuditAsync(userId, request?.UserName, SystemAuditSystems.Api, request?.IpAddress, SystemAuditTypes.ApprovalInstallationsDisabled, true,
				installationId == null
					? $"Approvals stopped on every Responder installation ({installations}) and passkey ({passkeys})."
					: $"Approvals stopped on Responder installation {SharedSessionAudit.SessionSuffix(installationId)}.",
				cancellationToken);
			await _notices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = userId, Kind = SecurityNoticeKind.ApprovalTurnedOff, ClientApplication = UserSessionClientApplication.Responder
			}, cancellationToken);
			return (installations, passkeys);
		}

		private async Task<List<UserSession>> EligibleApproverSessionsAsync(string userId, string excludeSessionId, CancellationToken cancellationToken)
		{
			var user = await _identityUsers.GetByIdAsync(userId);
			if (user == null || !await HasApprovingPasskeyAsync(userId, cancellationToken))
				return new List<UserSession>();

			var now = _time.GetUtcNow().UtcDateTime;
			return (await _sessions.GetActiveByUserAsync(userId, now) ?? Array.Empty<UserSession>())
				.Where(s => ApprovalApprovers.IsEligibleSession(s, userId, user.AuthenticationGeneration, now) &&
					!string.Equals(s.UserSessionId, excludeSessionId, StringComparison.Ordinal))
				.ToList();
		}

		/// <summary>An active Responder passkey with approval on, at Responder's current relying party.</summary>
		private async Task<bool> HasApprovingPasskeyAsync(string userId, CancellationToken cancellationToken)
		{
			var party = _registry.Get(UserSessionClientApplication.Responder);
			return party != null && (await _passkeyRows.GetActiveForUserAsync(userId, cancellationToken) ?? Array.Empty<UserPasskey>())
				.Any(p => ApprovalApprovers.IsApprovingPasskey(p, userId) && string.Equals(p.RpId, party.RpId, StringComparison.Ordinal));
		}

		/// <summary>A pending, unexpired request of the approver's own account under its current generation.</summary>
		private async Task<MfaApprovalResult> OpenForApproverAsync(PasskeyCaller approver, string approvalRequestId, CancellationToken cancellationToken)
		{
			if (!IsEnabled)
				return MfaApprovalResult.Of(MfaApprovalOutcome.Unavailable);
			if (!IsApproverCaller(approver))
				return MfaApprovalResult.Of(MfaApprovalOutcome.SessionRequired);
			if (!IsValidId(approvalRequestId))
				return MfaApprovalResult.Of(MfaApprovalOutcome.InvalidRequest);

			try
			{
				var request = await _requests.GetAsync(approvalRequestId, cancellationToken);
				if (request == null || !SameUser(request.UserId, approver.UserId) || request.AuthenticationGeneration != approver.AuthenticationGeneration)
					return MfaApprovalResult.Of(MfaApprovalOutcome.NotFound);
				if (!await IsEligibleApproverSessionAsync(approver, cancellationToken))
					return MfaApprovalResult.Of(MfaApprovalOutcome.Unavailable);

				return request.EffectiveState(_time.GetUtcNow().UtcDateTime) switch
				{
					MfaApprovalRequestState.Pending => MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, request),
					MfaApprovalRequestState.Denied => MfaApprovalResult.Of(MfaApprovalOutcome.Denied, request),
					_ => MfaApprovalResult.Of(MfaApprovalOutcome.Expired, request)
				};
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A Responder approval request could not be read.");
				return MfaApprovalResult.Of(MfaApprovalOutcome.ServiceUnavailable);
			}
		}

		/// <summary>
		/// The approver's own session record still takes requests: active, personal, current, and not stopped from the account
		/// page (plan section 6.5). A stopped installation can neither see, approve nor deny a request.
		/// </summary>
		private async Task<bool> IsEligibleApproverSessionAsync(PasskeyCaller approver, CancellationToken cancellationToken) =>
			ApprovalApprovers.IsEligibleSession(await _sessions.GetByIdAsync(approver.SessionId), approver.UserId, approver.AuthenticationGeneration,
				_time.GetUtcNow().UtcDateTime);

		/// <summary>An approver is a personal Responder session asking for itself (shared sessions arrive in slice 13).</summary>
		private static bool IsApproverCaller(PasskeyCaller approver) =>
			approver != null && !string.IsNullOrWhiteSpace(approver.SessionId) && !string.IsNullOrWhiteSpace(approver.UserId) &&
			approver.ClientApplication == UserSessionClientApplication.Responder && !approver.SharedMode &&
			string.IsNullOrWhiteSpace(approver.LoginTransactionId);

		/// <summary>
		/// What the approver is shown about the requester: its installation label and a coarse origin (region and country,
		/// never the city or address). Labels only, so a failed lookup leaves them empty.
		/// </summary>
		private async Task<(string Label, string Region)> RequesterContextAsync(MfaApprovalRequester requester, CancellationToken cancellationToken)
		{
			try
			{
				if (requester.Kind == MfaApprovalRequesterKind.Session)
				{
					var session = await _sessions.GetByIdAsync(requester.RequesterId);
					return (requester.InstallationLabel ?? session?.DeviceName, Coarse(session?.LastRegion, session?.LastCountry));
				}

				var location = string.IsNullOrWhiteSpace(requester.IpAddress)
					? null
					: await _location.GetApproximateLocationAsync(requester.IpAddress, cancellationToken);
				return (requester.InstallationLabel, Coarse(location?.Region, location?.Country));
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Requester context for a Responder approval could not be read.");
				return (requester.InstallationLabel, null);
			}
		}

		private static string Coarse(string region, string country) =>
			string.Join(", ", new[] { region, country }.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim())) is { Length: > 0 } text
				? text
				: null;

		/// <summary>
		/// A generic push to the user's Responder, once per department it signs in to, through the Responder-only Novu
		/// subscriber (workbook section 1). It names no department, app or number. Responder also shows pending requests when
		/// opened, so a lost push never blocks the user, and a push failure never fails the request.
		/// </summary>
		private async Task NotifyAsync(string userId, IEnumerable<UserSession> approvers, string approvalRequestId)
		{
			try
			{
				var codes = new HashSet<string>(StringComparer.Ordinal);
				foreach (var departmentId in approvers.Select(s => s.DepartmentId).Distinct())
				{
					var department = departmentId is int id
						? await _departments.GetDepartmentByIdAsync(id)
						: await _departments.GetDepartmentByUserIdAsync(userId);
					if (!string.IsNullOrWhiteSpace(department?.Code))
						codes.Add(department.Code);
				}

				foreach (var code in codes)
					await _novu.SendUserNotification(PushTitle, PushBody, userId, code, PushEventPrefix + approvalRequestId,
						((int)PushSoundTypes.Notifiation).ToString(CultureInfo.InvariantCulture));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The Responder approval push could not be sent; Responder shows the request when opened.");
			}
		}

		private static bool IsRequester(MfaApprovalRequest request, MfaApprovalRequesterKind kind, string requesterId) =>
			request != null && request.RequesterKind == (int)kind && string.Equals(request.RequesterId, requesterId, StringComparison.Ordinal);

		private static bool IsValidId(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 64;

		private static bool SameUser(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		private static string Limit(string value, int length) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];

		private async Task AuditAsync(string userId, string userName, SystemAuditSystems system, string ipAddress, SystemAuditTypes type, bool successful,
			string data, CancellationToken cancellationToken)
		{
			try
			{
				await _audits.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)system,
					Type = (int)type,
					UserId = userId,
					Username = userName,
					Successful = successful,
					IpAddress = ipAddress,
					ServerName = Environment.MachineName,
					Data = data
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, $"Responder approval audit ({type}) could not be saved.");
			}
		}
	}
}
