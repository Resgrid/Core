using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Authentication;
using Resgrid.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 11 (section 7.9): Responder approval. A requesting app asks; the user's own personal
	/// Responder session approves with its approval-enabled passkey after typing the number from the requesting screen;
	/// the requester uses the approval once. Abuse limits, "not me", and revocation of the approver are enforced here.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class MfaApprovalServiceTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string ResponderSession = "responder-1";
		private const string UnitSession = "unit-1";

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private Clock _clock;
		private IdentityUser _user;
		private DepartmentSecurityPolicy _policy;
		private InMemoryUserPasskeyRepository _passkeyRows;
		private InMemoryMfaApprovalRequestRepository _rows;
		private InMemoryMfaLoginTransactionRepository _logins;
		private InMemoryMfaEvidenceRepository _evidenceRows;
		private List<UserSession> _sessions;
		private Mock<IPasskeyFeatureGates> _gates;
		private Mock<INovuProvider> _novu;
		private Mock<ISystemAuditsService> _audits;
		private Mock<ISecurityNoticeService> _notices;
		private Mock<ISessionEventPublisher> _sessionEvents;
		private Mock<IUserSessionService> _userSessions;
		private InMemoryMfaActivityRepository _activityRows;
		private RelyingPartyRegistry _registry;
		private PasskeyService _passkeys;
		private MfaEvidenceService _evidence;
		private MfaApprovalService _service;
		private UserPasskey _responderPasskey;
		private SoftPasskeyAuthenticator _authenticator;
		private int _maxAttempts;

		[SetUp]
		public async Task SetUp()
		{
			_maxAttempts = PasskeyConfig.ApprovalMaxNumberAttempts;
			_clock = new Clock();
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_policy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId };
			_passkeyRows = new InMemoryUserPasskeyRepository();
			_rows = new InMemoryMfaApprovalRequestRepository();
			_logins = new InMemoryMfaLoginTransactionRepository();
			_evidenceRows = new InMemoryMfaEvidenceRepository();
			_sessions = new List<UserSession>
			{
				Session(ResponderSession, UserSessionClientApplication.Responder),
				Session(UnitSession, UserSessionClientApplication.Unit)
			};
			_gates = new Mock<IPasskeyFeatureGates>();
			_gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			_gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(true);
			_novu = new Mock<INovuProvider>();
			_audits = new Mock<ISystemAuditsService>();
			_notices = new Mock<ISecurityNoticeService>();
			_sessionEvents = new Mock<ISessionEventPublisher>();
			_userSessions = new Mock<IUserSessionService>();
			_userSessions.Setup(s => s.RevokeSessionAsync(UserId, UserId, It.IsAny<string>(), UserSessionRevocationReason.MfaChanged, It.IsAny<CancellationToken>()))
				.ReturnsAsync((string _, string _, string id, UserSessionRevocationReason _, CancellationToken _) =>
				{
					var ended = _sessions.Where(s => s.UserSessionId == id).ToList();
					ended.ForEach(s => s.State = (int)UserSessionState.Revoked);
					return new RevocationResult { RevokedSessionCount = ended.Count };
				});
			_registry = new RelyingPartyRegistry(PasskeyProviderTests.Layout);
			Build();
			(_responderPasskey, _authenticator) = await RegisterResponderPasskey(approval: true);
		}

		[TearDown]
		public void TearDown() => PasskeyConfig.ApprovalMaxNumberAttempts = _maxAttempts;

		private UserSession Session(string id, UserSessionClientApplication client, long generation = 4) => new()
		{
			UserSessionId = id, UserId = UserId, DepartmentId = DepartmentId, ClientApplication = (int)client, State = (int)UserSessionState.Active,
			ExpiresOn = _clock.Now.AddDays(1), AuthenticationGeneration = generation, DeviceName = client == UserSessionClientApplication.Unit ? "Engine 7 tablet" : "Pixel 8",
			LastRegion = "Ontario", LastCountry = "Canada", LastCity = "Toronto"
		};

		private void Build()
		{
			var sessions = new Mock<IUserSessionsRepository>();
			sessions.Setup(s => s.GetActiveByUserAsync(UserId, It.IsAny<DateTime>()))
				.ReturnsAsync(() => _sessions.Where(s => s.State == (int)UserSessionState.Active).ToList());
			sessions.Setup(s => s.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => _sessions.SingleOrDefault(s => s.UserSessionId == (string)id));
			sessions.Setup(s => s.DisableApprovalsAsync(UserId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((string _, string id, DateTime at, CancellationToken _) =>
				{
					var stopped = _sessions.Where(s => s.State == (int)UserSessionState.Active && s.ClientApplication == (int)UserSessionClientApplication.Responder &&
						s.ApprovalsDisabledOnUtc == null && (id == null || s.UserSessionId == id)).ToList();
					stopped.ForEach(s => s.ApprovalsDisabledOnUtc = at);
					return stopped.Count;
				});
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "DEPT" });
			var location = new Mock<IIpLocationProvider>();
			location.Setup(l => l.GetApproximateLocationAsync("198.51.100.7", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IpLocationResult { Region = "Quebec", Country = "Canada", City = "Montreal" });

			var policy = new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), _gates.Object);
			_activityRows = new InMemoryMfaActivityRepository();
			_evidence = new MfaEvidenceService(_evidenceRows, new InMemoryUserMfaStateRepository(), _passkeyRows, sessions.Object, _activityRows, _clock);
			_passkeys = new PasskeyService(_passkeyRows, new Fido2PasskeyProvider(_registry), _registry, _gates.Object,
				new AuthenticationChallengeService(new InMemoryAuthenticationChallengeRepository(), _clock), _evidence, policy, sessions.Object,
				_userSessions.Object, _audits.Object, _rows, _notices.Object, _clock);
			_service = new MfaApprovalService(_rows, _passkeyRows, sessions.Object, identity.Object, _logins, _passkeys, _registry, _gates.Object, policy,
				departments.Object, _novu.Object, location.Object, _audits.Object, _notices.Object, _sessionEvents.Object,
				new MfaActivityService(_activityRows, sessions.Object, _userSessions.Object, _notices.Object, _audits.Object, _clock), _clock);
		}

		private async Task<(UserPasskey Passkey, SoftPasskeyAuthenticator Authenticator)> RegisterResponderPasskey(bool approval, string id = "pk-responder")
		{
			var provider = new Fido2PasskeyProvider(_registry);
			var authenticator = new SoftPasskeyAuthenticator();
			var options = provider.CreateRegistrationOptions(UserSessionClientApplication.Responder, RandomNumberGenerator.GetBytes(32), "user1", "user1",
				Array.Empty<byte[]>(), false);
			var registered = await provider.VerifyRegistrationAsync(UserSessionClientApplication.Responder, options,
				authenticator.Register(options, PasskeyProviderTests.Origin(UserSessionClientApplication.Responder)));
			var passkey = new UserPasskey
			{
				UserPasskeyId = id, UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Responder, RpId = "responder.resgrid.test",
				CredentialId = registered.CredentialId, CredentialIdHash = SHA256.HashData(registered.CredentialId), PublicKey = registered.PublicKey,
				UserHandle = registered.UserHandle, DisplayName = "Responder passkey", CreatedOnUtc = _clock.Now, StateVersion = 1
			};
			await _passkeyRows.TryInsertAsync(passkey);
			if (approval)
				await _passkeyRows.TrySetApprovalEnabledAsync(id, UserId, true);
			return (await _passkeyRows.GetAsync(id), authenticator);
		}

		private MfaLoginTransaction Login(UserSessionClientApplication client = UserSessionClientApplication.Unit)
		{
			var transaction = new MfaLoginTransaction
			{
				MfaLoginTransactionId = Guid.NewGuid().ToString(), SecretHash = RandomNumberGenerator.GetBytes(32), UserId = UserId, DepartmentId = DepartmentId,
				ClientApplication = (int)client, FirstFactorMethod = (int)MfaEvidenceMethod.Password, FirstFactorVerifiedOnUtc = _clock.Now,
				AuthenticationGeneration = 4, CreatedOnUtc = _clock.Now, ExpiresOnUtc = _clock.Now.AddMinutes(5), MaxAttempts = 5,
				State = (int)MfaLoginTransactionState.Pending
			};
			_logins.Rows.Add(transaction);
			return transaction;
		}

		private static MfaApprovalRequester ForLogin(MfaLoginTransaction login) => MfaApprovalRequester.ForLoginTransaction(login, "user1", "198.51.100.7");

		private MfaApprovalRequester ForStepUp(string operation = MfaStepUpOperations.ChatExport, string sessionId = UnitSession,
			UserSessionClientApplication client = UserSessionClientApplication.Unit) => new()
		{
			UserId = UserId, Kind = MfaApprovalRequesterKind.Session, RequesterId = sessionId, ClientApplication = client, AuthenticationGeneration = 4,
			DepartmentId = DepartmentId, Purpose = MfaApprovalPurpose.StepUp, Operation = operation, IpAddress = "198.51.100.7", AuditSystem = SystemAuditSystems.Api
		};

		private static PasskeyCaller Approver(string sessionId = ResponderSession, UserSessionClientApplication client = UserSessionClientApplication.Responder,
			long generation = 4, bool shared = false) => new()
		{
			UserId = UserId, UserName = "user1", SessionId = sessionId, ClientApplication = client, AuthenticationGeneration = generation,
			DepartmentId = DepartmentId, SharedMode = shared, AuditSystem = SystemAuditSystems.Api
		};

		private async Task<(MfaApprovalResult Result, PasskeyOutcome Passkey)> Approve(MfaApprovalStart start, string number = null,
			PasskeyCaller approver = null, SoftPasskeyAuthenticator authenticator = null, UserPasskey passkey = null)
		{
			approver ??= Approver();
			var (outcome, ceremony) = await _service.BeginApprovalAsync(approver, start.ApprovalRequestId);
			if (outcome != MfaApprovalOutcome.Succeeded)
				return (MfaApprovalResult.Of(outcome), PasskeyOutcome.Succeeded);
			if (!ceremony.Succeeded)
				return (MfaApprovalResult.Of(MfaApprovalOutcome.InvalidRequest), ceremony.Outcome);

			var assertion = (authenticator ?? _authenticator).Assert(ceremony.OptionsJson, PasskeyProviderTests.Origin(UserSessionClientApplication.Responder),
				(passkey ?? _responderPasskey).UserHandle);
			return await _service.ApproveAsync(approver, start.ApprovalRequestId, number ?? start.MatchNumber, ceremony.RequestId, assertion);
		}

		private static string WrongNumber(string number) => number == "42" ? "43" : "42";

		// ---- Who can be asked --------------------------------------------------------------------------------------

		[Test]
		public async Task Approval_is_offered_only_to_other_apps_of_a_user_with_an_eligible_personal_responder()
		{
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeTrue();
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Responder)).Should().BeFalse("Responder approves; it never asks");

			_sessions.Single(s => s.UserSessionId == ResponderSession).State = (int)UserSessionState.Revoked;
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeFalse("no signed-in Responder");

			_sessions.Add(Session("responder-old", UserSessionClientApplication.Responder, generation: 3));
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeFalse("a Responder session from before a password change");

			_sessions.Add(Session("responder-2", UserSessionClientApplication.Responder));
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeTrue();

			await _passkeyRows.TrySetApprovalEnabledAsync(_responderPasskey.UserPasskeyId, UserId, false);
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeFalse("no Responder passkey with approval on");

			await _passkeyRows.TrySetApprovalEnabledAsync(_responderPasskey.UserPasskeyId, UserId, true);
			_gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(false);
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeFalse("the deployment gate is off");
			(await _service.RequestAsync(ForLogin(Login()))).Outcome.Should().Be(MfaApprovalOutcome.Unavailable);
		}

		[Test]
		public async Task An_unlock_is_asked_for_only_by_a_locked_shared_session_at_its_lock_version()
		{
			// Plan sections 7.9 and 12.5.3: the request is bound to the lock version, so a lock or operator change voids it.
			MfaApprovalRequester Unlock(bool shared = true, long? lockVersion = 3, MfaApprovalRequesterKind kind = MfaApprovalRequesterKind.Session) => new()
			{
				UserId = UserId, Kind = kind, RequesterId = UnitSession, ClientApplication = UserSessionClientApplication.Unit, AuthenticationGeneration = 4,
				DepartmentId = DepartmentId, Purpose = MfaApprovalPurpose.Unlock, SharedMode = shared, LockVersion = lockVersion, IpAddress = "198.51.100.7",
				AuditSystem = SystemAuditSystems.Api
			};

			(await _service.RequestAsync(Unlock(shared: false))).Outcome.Should().Be(MfaApprovalOutcome.InvalidRequest, "only a shared session locks");
			(await _service.RequestAsync(Unlock(lockVersion: null))).Outcome.Should().Be(MfaApprovalOutcome.InvalidRequest);
			(await _service.RequestAsync(Unlock(kind: MfaApprovalRequesterKind.LoginTransaction))).Outcome.Should().Be(MfaApprovalOutcome.InvalidRequest);
			_rows.Rows.Should().BeEmpty();

			var start = await _service.RequestAsync(Unlock());
			start.Succeeded.Should().BeTrue();
			var row = _rows.Rows.Single();
			row.Purpose.Should().Be((int)MfaApprovalPurpose.Unlock);
			row.LockVersion.Should().Be(3);
			row.SharedMode.Should().BeTrue();

			(await Approve(start)).Result.Succeeded.Should().BeTrue();
			var consumed = await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.Session, UnitSession, UserId, 4);
			consumed.Succeeded.Should().BeTrue();
			consumed.Request.LockVersion.Should().Be(3, "the unlock endpoint compares it with the session's current lock version");
		}

		[Test]
		public async Task A_signed_in_requester_hears_the_decision_over_its_own_connection_and_a_sign_in_keeps_polling()
		{
			// Workbook section 7.4: SignalR mfaApprovalChanged on the requesting session's group, carrying only the id and state.
			void Told(string sessionId, string requestId, string state, Times times) =>
				_sessionEvents.Verify(e => e.PublishAsync(sessionId, It.Is<SessionEventMessage>(m => m.Name == SessionEvents.MfaApprovalChanged &&
					m.ApprovalRequestId == requestId && m.State == state), It.IsAny<CancellationToken>()), times);

			var approved = await _service.RequestAsync(ForStepUp());
			(await Approve(approved)).Result.Succeeded.Should().BeTrue();
			Told(UnitSession, approved.ApprovalRequestId, "approved", Times.Once());

			_clock.Now = _clock.Now.AddMinutes(1);
			var denied = await _service.RequestAsync(ForStepUp());
			(await _service.DenyAsync(Approver(), denied.ApprovalRequestId, MfaApprovalEndReason.Declined)).Succeeded.Should().BeTrue();
			Told(UnitSession, denied.ApprovalRequestId, "denied", Times.Once());

			_clock.Now = _clock.Now.AddMinutes(1);
			var wrong = await _service.RequestAsync(ForStepUp());
			for (var i = 0; i < PasskeyConfig.ApprovalMaxNumberAttempts; i++)
				await Approve(wrong, WrongNumber(wrong.MatchNumber));
			Told(UnitSession, wrong.ApprovalRequestId, "denied", Times.Once());

			_clock.Now = _clock.Now.AddMinutes(20);
			var signIn = await _service.RequestAsync(ForLogin(Login()));
			(await Approve(signIn)).Result.Succeeded.Should().BeTrue();
			_sessionEvents.Verify(e => e.PublishAsync(It.IsAny<string>(), It.Is<SessionEventMessage>(m => m.ApprovalRequestId == signIn.ApprovalRequestId),
				It.IsAny<CancellationToken>()), Times.Never, "a sign-in in progress has no connection");
		}

		[Test]
		public async Task A_request_carries_only_a_hash_of_its_number_and_pushes_nothing_identifying()
		{
			var start = await _service.RequestAsync(ForLogin(Login()));

			start.Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
			start.MatchNumber.Should().MatchRegex("^[1-9][0-9]$");
			start.ExpiresInSeconds.Should().Be(PasskeyConfig.ApprovalRequestLifetimeSeconds);
			var row = _rows.Rows.Single();
			row.MatchNumberHash.Should().Equal(MfaApprovalRequest.HashMatchNumber(start.ApprovalRequestId, start.MatchNumber));
			typeof(MfaApprovalRequest).GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (string)p.GetValue(row))
				.Should().NotContain(start.MatchNumber);
			row.OriginRegion.Should().Be("Quebec, Canada", "region and country only, never the city");
			row.ExpiresOnUtc.Should().Be(_clock.Now.AddSeconds(PasskeyConfig.ApprovalRequestLifetimeSeconds));

			_novu.Verify(n => n.SendUserNotification("Sign-in approval requested", "Open Resgrid Responder to review.", UserId, "DEPT",
				"NA:" + start.ApprovalRequestId, It.IsAny<string>()), Times.Once);
			_novu.Verify(n => n.SendUserNotification(It.IsAny<string>(), It.Is<string>(body => body.Contains(start.MatchNumber)), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task A_push_failure_never_fails_the_request()
		{
			_novu.Setup(n => n.SendUserNotification(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<string>())).ThrowsAsync(new TimeoutException());

			(await _service.RequestAsync(ForLogin(Login()))).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
		}

		[TestCase(MfaStepUpOperations.SecurityChange, TestName = "Security changes never accept approval")]
		[TestCase(MfaStepUpOperations.AccountSecurity, TestName = "Account factors never accept approval")]
		public async Task Approval_is_refused_where_the_plan_requires_a_direct_factor(string operation) =>
			(await _service.RequestAsync(ForStepUp(operation))).Outcome.Should().Be(MfaApprovalOutcome.Unavailable);

		[Test]
		public async Task The_department_switches_decide_where_approval_counts()
		{
			(await _service.RequestAsync(ForStepUp(MfaStepUpOperations.ChatExport))).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
			(await _service.RequestAsync(ForStepUp(MfaStepUpOperations.AdpManagement))).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);

			_policy.AllowResponderApproval = false;
			(await _service.RequestAsync(ForStepUp())).Outcome.Should().Be(MfaApprovalOutcome.Unavailable);

			_policy.AllowResponderApproval = true;
			_policy.AllowPasskeysForLoginMfa = false;
			(await _service.RequestAsync(ForLogin(Login()))).Outcome.Should().Be(MfaApprovalOutcome.Unavailable, "approval follows the row's passkey switch");
			(await _service.RequestAsync(ForStepUp(MfaStepUpOperations.AdpManagement))).Outcome.Should().Be(MfaApprovalOutcome.Succeeded,
				"the ADP row has its own passkey switch");

			// The ADP row accepts approval and another Responder could approve, so only the rule itself refuses a Responder requester.
			_sessions.Add(Session("responder-2", UserSessionClientApplication.Responder));
			(await _service.RequestAsync(ForStepUp(MfaStepUpOperations.AdpManagement, client: UserSessionClientApplication.Responder,
				sessionId: ResponderSession))).Outcome.Should().Be(MfaApprovalOutcome.Unavailable, "Responder never asks");
		}

		[Test]
		public async Task A_new_request_replaces_the_pending_one()
		{
			var first = await _service.RequestAsync(ForLogin(Login()));
			var second = await _service.RequestAsync(ForStepUp());

			(await _rows.GetAsync(first.ApprovalRequestId)).EndReason.Should().Be((int)MfaApprovalEndReason.Superseded);
			(await _service.GetPendingForApproverAsync(Approver())).Request.MfaApprovalRequestId.Should().Be(second.ApprovalRequestId);
			(await Approve(first)).Result.Outcome.Should().Be(MfaApprovalOutcome.Expired, "a replaced request can no longer be approved");
		}

		[Test]
		public async Task Requests_are_limited_per_user()
		{
			for (var i = 0; i < PasskeyConfig.ApprovalMaxRequestsPerWindow; i++)
				(await _service.RequestAsync(ForStepUp())).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);

			(await _service.RequestAsync(ForStepUp())).Outcome.Should().Be(MfaApprovalOutcome.TooManyRequests);

			_clock.Now = _clock.Now.AddMinutes(PasskeyConfig.ApprovalRateWindowMinutes + 1);
			(await _service.RequestAsync(ForStepUp())).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
		}

		[Test]
		public async Task Two_denials_or_expiries_in_a_row_suspend_requests_for_fifteen_minutes()
		{
			var denied = await _service.RequestAsync(ForStepUp());
			(await _service.DenyAsync(Approver(), denied.ApprovalRequestId, MfaApprovalEndReason.Declined)).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
			await _service.RequestAsync(ForStepUp());
			_clock.Now = _clock.Now.AddSeconds(PasskeyConfig.ApprovalRequestLifetimeSeconds + 1);

			(await _service.RequestAsync(ForStepUp())).Outcome.Should().Be(MfaApprovalOutcome.Suspended, "a denial and then an expiry");
			_notices.Verify(n => n.QueueOnceAsync(It.Is<SecurityNoticeRequest>(r => r.UserId == UserId && r.Kind == SecurityNoticeKind.ApprovalSuspended),
				TimeSpan.FromMinutes(PasskeyConfig.ApprovalSuspensionMinutes), It.IsAny<CancellationToken>()), Times.Once);

			_clock.Now = _clock.Now.AddMinutes(PasskeyConfig.ApprovalSuspensionMinutes);
			(await _service.RequestAsync(ForStepUp())).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
		}

		[Test]
		public async Task An_approval_breaks_the_streak_and_a_requester_cancel_does_not_count()
		{
			var declined = await _service.RequestAsync(ForStepUp());
			await _service.DenyAsync(Approver(), declined.ApprovalRequestId, MfaApprovalEndReason.Declined);
			var approved = await _service.RequestAsync(ForStepUp());
			(await Approve(approved)).Result.Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
			var canceled = await _service.RequestAsync(ForStepUp());
			(await _service.CancelAsync(canceled.ApprovalRequestId, MfaApprovalRequesterKind.Session, UnitSession)).Should().Be(MfaApprovalOutcome.Succeeded);
			var declinedAgain = await _service.RequestAsync(ForStepUp());
			await _service.DenyAsync(Approver(), declinedAgain.ApprovalRequestId, MfaApprovalEndReason.Declined);

			(await _service.RequestAsync(ForStepUp())).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
		}

		// ---- Approving ---------------------------------------------------------------------------------------------

		[Test]
		public async Task A_sign_in_on_a_shared_workstation_shows_the_approver_that_it_is_shared_and_the_stations_label()
		{
			var shared = Login(UserSessionClientApplication.Dispatch);
			shared.SharedMode = true;
			shared.InstallationLabel = "Dispatch desk 2";
			(await _service.RequestAsync(ForLogin(shared))).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);

			var pending = (await _service.GetPendingForApproverAsync(Approver())).Request;
			pending.ClientApplication.Should().Be((int)UserSessionClientApplication.Dispatch);
			pending.SharedMode.Should().BeTrue();
			pending.InstallationLabel.Should().Be("Dispatch desk 2");

			// A personal sign-in names its device, and is not shown as shared.
			var personal = Login();
			personal.InstallationLabel = "Pat's iPhone";
			await _service.RequestAsync(ForLogin(personal));
			pending = (await _service.GetPendingForApproverAsync(Approver())).Request;
			pending.SharedMode.Should().BeFalse();
			pending.InstallationLabel.Should().Be("Pat's iPhone");
		}

		[Test]
		public async Task The_approver_sees_what_is_asking_and_approves_with_the_number_and_its_passkey()
		{
			var start = await _service.RequestAsync(ForStepUp(MfaStepUpOperations.AdpManagement));

			var pending = (await _service.GetPendingForApproverAsync(Approver())).Request;
			pending.MfaApprovalRequestId.Should().Be(start.ApprovalRequestId);
			pending.ClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			pending.InstallationLabel.Should().Be("Engine 7 tablet");
			pending.Operation.Should().Be(MfaStepUpOperations.AdpManagement);
			pending.OriginRegion.Should().Be("Ontario, Canada");

			var (result, passkey) = await Approve(start);
			passkey.Should().Be(PasskeyOutcome.Succeeded);
			result.Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
			var row = _rows.Rows.Single();
			row.RequestState.Should().Be(MfaApprovalRequestState.Approved);
			row.ApproverSessionId.Should().Be(ResponderSession);
			row.ApproverPasskeyId.Should().Be(_responderPasskey.UserPasskeyId);
			(await _passkeyRows.GetAsync(_responderPasskey.UserPasskeyId)).LastUsedClientApplication.Should().Be((int)UserSessionClientApplication.Responder);
			(await _service.GetPendingForApproverAsync(Approver())).Request.Should().BeNull();
		}

		[Test]
		public async Task Wrong_numbers_count_and_the_last_one_denies_the_request()
		{
			PasskeyConfig.ApprovalMaxNumberAttempts = 3;
			var start = await _service.RequestAsync(ForStepUp());
			var wrong = WrongNumber(start.MatchNumber);

			var first = await Approve(start, wrong);
			first.Result.Outcome.Should().Be(MfaApprovalOutcome.NumberMismatch);
			first.Result.Request.Attempts.Should().Be(1);
			(await Approve(start, wrong)).Result.Outcome.Should().Be(MfaApprovalOutcome.NumberMismatch);
			(await Approve(start, wrong)).Result.Outcome.Should().Be(MfaApprovalOutcome.Denied);
			_rows.Rows.Single().EndReason.Should().Be((int)MfaApprovalEndReason.TooManyAttempts);

			(await Approve(start)).Result.Outcome.Should().Be(MfaApprovalOutcome.Denied, "the right number is too late");
			(await _service.ApproveAsync(Approver(), start.ApprovalRequestId, "4", "request", "{}")).Result.Outcome
				.Should().Be(MfaApprovalOutcome.InvalidRequest, "not two digits: refused unread");
		}

		[Test]
		public async Task Only_the_users_own_personal_responder_session_with_an_approval_passkey_can_approve()
		{
			var start = await _service.RequestAsync(ForStepUp());

			(await Approve(start, approver: Approver(client: UserSessionClientApplication.Unit, sessionId: UnitSession))).Result.Outcome
				.Should().Be(MfaApprovalOutcome.SessionRequired, "another app cannot approve");
			(await Approve(start, approver: Approver(shared: true))).Result.Outcome.Should().Be(MfaApprovalOutcome.SessionRequired, "never a shared installation");
			(await Approve(start, approver: Approver(generation: 5))).Result.Outcome.Should().Be(MfaApprovalOutcome.NotFound,
				"a request from before the account's state changed");

			var other = new PasskeyCaller
			{
				UserId = "someone-else", SessionId = "their-responder", ClientApplication = UserSessionClientApplication.Responder, AuthenticationGeneration = 4
			};
			(await Approve(start, approver: other)).Result.Outcome.Should().Be(MfaApprovalOutcome.NotFound, "another account's request");

			var (plain, plainAuthenticator) = await RegisterResponderPasskey(approval: false, id: "pk-plain");
			var (_, begun) = await _service.BeginApprovalAsync(Approver(), start.ApprovalRequestId);
			var assertion = plainAuthenticator.Assert(begun.OptionsJson, PasskeyProviderTests.Origin(UserSessionClientApplication.Responder), plain.UserHandle);
			(await _service.ApproveAsync(Approver(), start.ApprovalRequestId, start.MatchNumber, begun.RequestId, assertion)).Passkey
				.Should().Be(PasskeyOutcome.NotRegisteredForClient, "a Responder passkey without approval on cannot approve");

			(await Approve(start)).Result.Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
		}

		[Test]
		public async Task An_expired_request_cannot_be_approved()
		{
			var start = await _service.RequestAsync(ForStepUp());
			_clock.Now = _clock.Now.AddSeconds(PasskeyConfig.ApprovalRequestLifetimeSeconds);

			(await Approve(start)).Result.Outcome.Should().Be(MfaApprovalOutcome.Expired);
		}

		[Test]
		public async Task Not_me_ends_the_sign_in_and_suspends_approval()
		{
			var login = Login();
			var start = await _service.RequestAsync(ForLogin(login));

			(await _service.DenyAsync(Approver(), start.ApprovalRequestId, MfaApprovalEndReason.NotMe)).Outcome.Should().Be(MfaApprovalOutcome.Succeeded);

			_logins.Rows.Single().TransactionState.Should().Be(MfaLoginTransactionState.Exhausted, "the password was used by someone else");
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.UserId == UserId && r.Kind == SecurityNoticeKind.ApprovalNotMe &&
				r.ClientApplication == UserSessionClientApplication.Unit), It.IsAny<CancellationToken>()), Times.Once);
			(await _service.RequestAsync(ForStepUp())).Outcome.Should().Be(MfaApprovalOutcome.Suspended, "one \"not me\" is enough");
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.MfaApprovalDenied && x.Data.Contains("did not request")),
				It.IsAny<CancellationToken>()), Times.Once);

			var declinedLogin = Login();
			_clock.Now = _clock.Now.AddMinutes(PasskeyConfig.ApprovalSuspensionMinutes + 1);
			var declined = await _service.RequestAsync(ForLogin(declinedLogin));
			await _service.DenyAsync(Approver(), declined.ApprovalRequestId, MfaApprovalEndReason.Declined);
			_logins.Rows.Single(t => t.MfaLoginTransactionId == declinedLogin.MfaLoginTransactionId).TransactionState
				.Should().Be(MfaLoginTransactionState.Pending, "a plain decline leaves the sign-in to use another method");
		}

		[Test]
		public async Task Protected_data_is_approved_for_a_signed_in_session_through_the_real_service()
		{
			// Slice 18's ADP issuer asks with purpose Adp; the service must accept it from a session in one department (plan section 7.9).
			MfaApprovalRequester ForAdp(MfaApprovalRequesterKind kind = MfaApprovalRequesterKind.Session, int? department = DepartmentId) => new()
			{
				UserId = UserId, Kind = kind, RequesterId = UnitSession, ClientApplication = UserSessionClientApplication.Unit, AuthenticationGeneration = 4,
				DepartmentId = department, Purpose = MfaApprovalPurpose.Adp, IpAddress = "198.51.100.7", AuditSystem = SystemAuditSystems.Api
			};

			var start = await _service.RequestAsync(ForAdp());
			start.Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
			await Approve(start);
			var used = await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.Session, UnitSession, UserId, 4);
			used.Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
			used.Request.RequestPurpose.Should().Be(MfaApprovalPurpose.Adp);
			used.Request.DepartmentId.Should().Be(DepartmentId);

			(await _service.RequestAsync(ForAdp(MfaApprovalRequesterKind.LoginTransaction))).Outcome
				.Should().Be(MfaApprovalOutcome.InvalidRequest, "a sign-in without a session has no protected data to open");
			(await _service.RequestAsync(ForAdp(department: null))).Outcome.Should().Be(MfaApprovalOutcome.InvalidRequest);

			_policy.AllowPasskeysForAdp = false;
			(await _service.RequestAsync(ForAdp())).Outcome.Should().Be(MfaApprovalOutcome.Unavailable, "the department's ADP switches decide");
		}

		// ---- Using an approval ---------------------------------------------------------------------------------------

		[Test]
		public async Task An_approval_is_used_once_by_the_requester_that_made_it()
		{
			var login = Login();
			var start = await _service.RequestAsync(ForLogin(login));
			(await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.LoginTransaction, login.MfaLoginTransactionId, UserId, 4))
				.Outcome.Should().Be(MfaApprovalOutcome.Pending);

			await Approve(start);

			(await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.LoginTransaction, "another-login", UserId, 4))
				.Outcome.Should().Be(MfaApprovalOutcome.NotFound);
			(await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.Session, login.MfaLoginTransactionId, UserId, 4))
				.Outcome.Should().Be(MfaApprovalOutcome.NotFound);
			(await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.LoginTransaction, login.MfaLoginTransactionId, UserId, 5))
				.Outcome.Should().Be(MfaApprovalOutcome.NotFound, "the account's generation moved");

			var used = await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.LoginTransaction, login.MfaLoginTransactionId, UserId, 4);
			used.Outcome.Should().Be(MfaApprovalOutcome.Succeeded);
			used.Request.ApproverPasskeyId.Should().Be(_responderPasskey.UserPasskeyId);
			used.Request.DecidedOnUtc.Should().Be(_clock.Now);

			(await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.LoginTransaction, login.MfaLoginTransactionId, UserId, 4))
				.Outcome.Should().Be(MfaApprovalOutcome.NotFound, "once");
		}

		[Test]
		public async Task An_approval_is_usable_only_briefly_after_its_request_expires()
		{
			var start = await _service.RequestAsync(ForStepUp());
			await Approve(start);
			_clock.Now = _clock.Now.AddSeconds(PasskeyConfig.ApprovalRequestLifetimeSeconds + PasskeyConfig.ApprovalConsumeGraceSeconds);

			(await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.Session, UnitSession, UserId, 4)).Outcome
				.Should().Be(MfaApprovalOutcome.Expired);
		}

		private static IEnumerable<TestCaseData> ApproverChanges()
		{
			yield return new TestCaseData((Func<MfaApprovalServiceTests, Task>)(t =>
				t._passkeyRows.TryRevokeAsync(t._responderPasskey.UserPasskeyId, UserId, PasskeyRevocationReason.RemovedByUser, UserId, t._clock.Now)))
				.SetName("The approving passkey was removed");
			yield return new TestCaseData((Func<MfaApprovalServiceTests, Task>)(t =>
				t._passkeyRows.TrySetApprovalEnabledAsync(t._responderPasskey.UserPasskeyId, UserId, false)))
				.SetName("Approval was turned off on the approving passkey");
			yield return new TestCaseData((Func<MfaApprovalServiceTests, Task>)(t =>
			{
				t._sessions.Single(s => s.UserSessionId == ResponderSession).State = (int)UserSessionState.Revoked;
				return Task.CompletedTask;
			})).SetName("The approving Responder session ended");
			yield return new TestCaseData((Func<MfaApprovalServiceTests, Task>)(t =>
			{
				t._sessions.Single(s => s.UserSessionId == ResponderSession).ExpiresOn = t._clock.Now;
				return Task.CompletedTask;
			})).SetName("The approving Responder session expired");
		}

		[TestCaseSource(nameof(ApproverChanges))]
		public async Task An_approval_stops_counting_when_its_approver_no_longer_does(Func<MfaApprovalServiceTests, Task> change)
		{
			var start = await _service.RequestAsync(ForStepUp());
			await Approve(start);

			// Evidence already recorded from this approval stops counting on the next read, too.
			var reference = MfaApprovalRequest.FactorReferenceFor(_responderPasskey.UserPasskeyId, ResponderSession);
			await _evidence.RecordAsync(UserId, MfaEvidence.TrackedSessionKey(UnitSession), UserSessionClientApplication.Unit, MfaEvidenceKind.SecondFactor,
				MfaEvidenceMethod.PasskeyApproval, MfaEvidencePurpose.StepUp, _clock.Now, 4, DepartmentId, reference);
			(await _evidence.GetLatestSecondFactorAsync(UserId, MfaEvidence.TrackedSessionKey(UnitSession), 4)).Should().NotBeNull();
			(await _service.IsApproverValidAsync(UserId, reference, 4)).Should().BeTrue();

			await change(this);

			(await _service.ConsumeAsync(start.ApprovalRequestId, MfaApprovalRequesterKind.Session, UnitSession, UserId, 4)).Outcome
				.Should().Be(MfaApprovalOutcome.Expired);
			(await _evidence.GetLatestSecondFactorAsync(UserId, MfaEvidence.TrackedSessionKey(UnitSession), 4)).Should().BeNull();
			(await _evidence.GetLatestStepUpAsync(UserId, MfaEvidence.TrackedSessionKey(UnitSession), 4)).Should().BeNull();
			(await _service.IsApproverValidAsync(UserId, reference, 4)).Should().BeFalse();
		}

		[Test]
		public async Task Removing_or_disabling_the_approving_passkey_ends_what_is_waiting_and_the_sign_ins_it_approved()
		{
			var start = await _service.RequestAsync(ForStepUp());
			var signedIn = Session("unit-approved", UserSessionClientApplication.Unit);
			signedIn.LoginMfaMethod = (int)MfaEvidenceMethod.PasskeyApproval;
			signedIn.LoginMfaFactorReference = MfaApprovalRequest.FactorReferenceFor(_responderPasskey.UserPasskeyId, ResponderSession);
			_sessions.Add(signedIn);
			var responderCaller = Approver();
			await _evidence.RecordAsync(UserId, MfaEvidence.TrackedSessionKey(ResponderSession), UserSessionClientApplication.Responder,
				MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp, MfaEvidencePurpose.StepUp, _clock.Now, 4);

			(await _passkeys.SetApprovalEnabledAsync(responderCaller, _responderPasskey.UserPasskeyId, false)).Should().Be(PasskeyOutcome.Succeeded);

			_rows.Rows.Single().EndReason.Should().Be((int)MfaApprovalEndReason.ApproverRevoked);
			signedIn.State.Should().Be((int)UserSessionState.Revoked, "a sign-in approved with it ends, as removing it would");
			_sessions.Single(s => s.UserSessionId == UnitSession).State.Should().Be((int)UserSessionState.Active, "other sign-ins are untouched");
			(await Approve(start)).Result.Outcome.Should().Be(MfaApprovalOutcome.Expired);
		}

		// ---- Recent activity and approval installations (plan section 6.5; slice 17) ------------------------------------

		[Test]
		public async Task A_denied_approval_is_the_requesters_recent_activity_and_names_the_installation_that_denied_it()
		{
			var stepUp = await _service.RequestAsync(ForStepUp());
			await _service.DenyAsync(Approver(), stepUp.ApprovalRequestId, MfaApprovalEndReason.Declined);

			var denied = _activityRows.Rows.Should().ContainSingle().Subject;
			denied.Successful.Should().BeFalse();
			denied.Method.Should().Be((int)MfaEvidenceMethod.PasskeyApproval);
			denied.Purpose.Should().Be((int)MfaEvidencePurpose.StepUp);
			denied.ClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			denied.InstallationLabel.Should().Be("Engine 7 tablet");
			denied.DepartmentId.Should().Be(DepartmentId);
			denied.SessionId.Should().Be(UnitSession, "the session that asked, so the user can see which app it was");
			denied.ApproverSessionId.Should().Be(ResponderSession);

			// A sign-in has no session yet; a wrong number that ends the request is a denial too.
			PasskeyConfig.ApprovalMaxNumberAttempts = 1;
			var login = await _service.RequestAsync(ForLogin(Login()));
			(await Approve(login, WrongNumber(login.MatchNumber))).Result.Outcome.Should().Be(MfaApprovalOutcome.Denied);
			var wrongNumber = _activityRows.Rows.Should().HaveCount(2).And.ContainSingle(a => a.Purpose == (int)MfaEvidencePurpose.Login).Subject;
			wrongNumber.Successful.Should().BeFalse();
			wrongNumber.SessionId.Should().BeNull();
			wrongNumber.ApproverSessionId.Should().Be(ResponderSession);

			// An approval the requester uses is recorded with its evidence, naming the approver from the factor reference.
			_clock.Now = _clock.Now.AddMinutes(PasskeyConfig.ApprovalSuspensionMinutes + 1);
			await _evidence.RecordAsync(UserId, MfaEvidence.TrackedSessionKey(UnitSession), UserSessionClientApplication.Unit, MfaEvidenceKind.SecondFactor,
				MfaEvidenceMethod.PasskeyApproval, MfaEvidencePurpose.StepUp, _clock.Now, 4, DepartmentId,
				MfaApprovalRequest.FactorReferenceFor(_responderPasskey.UserPasskeyId, ResponderSession));
			var approved = _activityRows.Rows.Single(a => a.Successful);
			approved.ApproverSessionId.Should().Be(ResponderSession);
			approved.InstallationLabel.Should().Be("Engine 7 tablet", "the label comes from the requester's session");
		}

		[Test]
		public async Task Stopping_one_installation_ends_its_approvals_and_what_is_waiting_and_leaves_the_others()
		{
			_sessions.Add(Session("responder-2", UserSessionClientApplication.Responder));
			_sessions.Add(Session("unit-2", UserSessionClientApplication.Unit));
			var start = await _service.RequestAsync(ForStepUp());
			await Approve(start);
			var reference = MfaApprovalRequest.FactorReferenceFor(_responderPasskey.UserPasskeyId, ResponderSession);
			(await _service.IsApproverValidAsync(UserId, reference, 4)).Should().BeTrue();
			var waiting = await _service.RequestAsync(ForStepUp(sessionId: "unit-2"));

			(await _service.DisableInstallationsAsync(UserId, ResponderSession, new SharedSessionRequestInfo { UserName = "user1" }))
				.Should().Be((1, 0));

			_sessions.Single(s => s.UserSessionId == ResponderSession).ApprovalsDisabledOnUtc.Should().Be(_clock.Now);
			_sessions.Single(s => s.UserSessionId == "responder-2").ApprovalsDisabledOnUtc.Should().BeNull();
			(await _service.IsApproverValidAsync(UserId, reference, 4)).Should().BeFalse("an approval it gave stops counting");
			_rows.Rows.Single(r => r.MfaApprovalRequestId == waiting.ApprovalRequestId).EndReason.Should().Be((int)MfaApprovalEndReason.ApproverRevoked);
			(await _passkeyRows.GetAsync(_responderPasskey.UserPasskeyId)).ApprovalEnabled.Should().BeTrue("one installation leaves the passkey alone");

			var next = await _service.RequestAsync(ForStepUp());
			(await _service.GetPendingForApproverAsync(Approver())).Outcome.Should().Be(MfaApprovalOutcome.Unavailable, "the stopped installation is no longer asked");
			(await Approve(next)).Result.Outcome.Should().Be(MfaApprovalOutcome.Unavailable, "and cannot approve");
			(await _service.DenyAsync(Approver(), next.ApprovalRequestId, MfaApprovalEndReason.NotMe)).Outcome
				.Should().Be(MfaApprovalOutcome.Unavailable, "or deny, so a stopped installation cannot pause approvals either");
			(await _service.GetPendingForApproverAsync(Approver("responder-2"))).Request.MfaApprovalRequestId.Should().Be(next.ApprovalRequestId);

			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.ApprovalInstallationsDisabled && x.Successful),
				It.IsAny<CancellationToken>()), Times.Once);
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.UserId == UserId && r.Kind == SecurityNoticeKind.ApprovalTurnedOff),
				It.IsAny<CancellationToken>()), Times.Once);

			(await _service.DisableInstallationsAsync(UserId, ResponderSession, null)).Should().Be((0, 0), "already stopped");
			(await _service.DisableInstallationsAsync(UserId, UnitSession, null)).Should().Be((0, 0), "not a Responder installation");
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.ApprovalTurnedOff), It.IsAny<CancellationToken>()),
				Times.Once, "nothing changed, so nothing to tell");
		}

		[Test]
		public async Task Stopping_all_approvals_stops_every_installation_and_turns_approval_off_on_every_responder_passkey()
		{
			_sessions.Add(Session("responder-2", UserSessionClientApplication.Responder));
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeTrue();

			(await _service.DisableInstallationsAsync(UserId, null, null)).Should().Be((2, 1));

			(await _passkeyRows.GetAsync(_responderPasskey.UserPasskeyId)).ApprovalEnabled.Should().BeFalse();
			_sessions.Where(s => s.ClientApplication == (int)UserSessionClientApplication.Responder).Should().OnlyContain(s => s.ApprovalsDisabledOnUtc != null);
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeFalse();

			// Turning the passkey back on does not bring back installations that were stopped.
			await _passkeyRows.TrySetApprovalEnabledAsync(_responderPasskey.UserPasskeyId, UserId, true);
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeFalse();
			_sessions.Add(Session("responder-3", UserSessionClientApplication.Responder));
			(await _service.IsAvailableAsync(UserId, UserSessionClientApplication.Unit)).Should().BeTrue("a new sign-in to Responder can be asked again");
		}
	}
}
