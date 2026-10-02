using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 13 (sections 5.5, 10.5 and 12.5.3): shared vehicle tablet and workstation sessions. The
	/// server decides which sessions are shared, locks them at its own idle deadline, ends them at the shift ceiling, and
	/// makes nothing from before a lock usable after it.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SharedSessionTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 22;

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private Clock _clock;
		private InMemoryUserSessionsRepository _rows;
		private InMemoryMfaEvidenceRepository _evidenceRows;
		private DepartmentSecurityPolicy _policy;
		private IdentityUser _user;
		private List<SystemAudit> _audited;
		private DepartmentSsoConfig[] _ssoConfigs;
		private bool _gateOn;
		private string _sessionGate;
		private Mock<ISessionEventPublisher> _events;
		private InMemoryMfaActivityRepository _activityRows;

		[SetUp]
		public void SetUp()
		{
			_sessionGate = SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc;
			SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc = string.Empty;
			_clock = new Clock();
			_rows = new InMemoryUserSessionsRepository();
			_evidenceRows = new InMemoryMfaEvidenceRepository();
			_policy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId };
			_user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
			_audited = new List<SystemAudit>();
			_ssoConfigs = Array.Empty<DepartmentSsoConfig>();
			_gateOn = true;
			_events = new Mock<ISessionEventPublisher>();
			_activityRows = new InMemoryMfaActivityRepository();
		}

		[TearDown]
		public void TearDown() => SessionSecurityConfig.DepartmentSessionPolicyEnforcementAfterUtc = _sessionGate;

		// ---- Harness ---------------------------------------------------------------------------------------------------

		private Mock<IDepartmentSsoService> Sso()
		{
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _policy);
			sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _ssoConfigs);
			return sso;
		}

		private ISystemAuditsService Audits()
		{
			var audits = new Mock<ISystemAuditsService>();
			audits.Setup(a => a.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()))
				.Callback((SystemAudit audit, CancellationToken _) => { lock (_audited) _audited.Add(audit); })
				.ReturnsAsync((SystemAudit audit, CancellationToken _) => audit);
			return audits.Object;
		}

		private IPasskeyFeatureGates Gates()
		{
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(() => _gateOn);
			gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(true);
			return gates.Object;
		}

		private UserSessionService Sessions()
		{
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(UserId, DepartmentId, true))
				.ReturnsAsync(new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId });
			return new UserSessionService(_rows, identity.Object, Mock.Of<IIdentityRepository>(), departments.Object, Sso().Object,
				new ClientSessionMetadataParser(), Mock.Of<IIpLocationProvider>(), Gates(), Audits(), _events.Object, _clock);
		}

		private MfaEvidenceService Evidence() =>
			new(_evidenceRows, new InMemoryUserMfaStateRepository(), Mock.Of<IUserPasskeyRepository>(), _rows, _activityRows, _clock);

		private SharedSessionService Shared() => new(_rows, Sessions(), Evidence(), Sso().Object, Audits(),
			new MfaActivityService(_activityRows, _rows, Sessions(), Mock.Of<ISecurityNoticeService>(), Audits(), _clock), _clock);

		private async Task<UserSession> SignInAsync(UserSessionClientApplication client = UserSessionClientApplication.Unit, bool requested = true,
			UserSessionAuthenticationMethod method = UserSessionAuthenticationMethod.LocalPassword) =>
			await Sessions().CreateSessionAsync(new SessionIssueContext
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				AuthenticationGeneration = 4,
				ClientApplication = client,
				AuthenticationMethod = method,
				DeviceName = "Engine 7 tablet",
				SharedModeRequested = requested,
				ExpiresOn = _clock.Now.AddDays(365)
			});

		private Task<SessionValidationResult> ValidateAsync(string sessionId) =>
			Sessions().ValidateAsync(new SessionPrincipalContext
			{
				UserId = UserId, SessionId = sessionId, AuthenticationGeneration = 4, DepartmentId = DepartmentId, CredentialIssuedOn = _clock.Now
			});

		private static SharedSessionRequestInfo Request() => new() { UserName = "user1", IpAddress = "203.0.113.9", CorrelationId = "trace-1" };

		private int Audited(SystemAuditTypes type)
		{
			lock (_audited) return _audited.Count(a => a.Type == (int)type);
		}

		// ---- Which sessions are shared (plan section 10.5) ---------------------------------------------------------------

		[Test]
		public async Task An_installation_request_makes_a_shared_session_only_while_the_gate_is_on()
		{
			_gateOn = false;
			var personal = await SignInAsync();
			personal.SharedMode.Should().BeFalse("the deployment does not offer shared mode yet");
			personal.ExpiresOn.Should().Be(_clock.Now.AddDays(365));
			personal.SharedIdleLockMinutes.Should().BeNull();

			_gateOn = true;
			var shared = await SignInAsync();
			shared.SharedMode.Should().BeTrue();
			shared.SharedModeSource.Should().Be((int)SharedModeSource.InstallationRequested);
			shared.ExpiresOn.Should().Be(_clock.Now.AddHours(12), "a personal device's long refresh lifetime is not copied into a shared session");
			shared.SharedIdleLockMinutes.Should().Be(5);
			shared.LastOperatorActivityOn.Should().Be(_clock.Now);
			shared.IsLocked.Should().BeFalse();
			shared.LockVersion.Should().Be(0);
			_rows.Row(shared.UserSessionId).SharedMode.Should().BeTrue("it is stored, not re-derived from a label later");

			_policy.SharedShiftHours = 8;
			_policy.SharedIdleLockMinutes = 3;
			var tuned = await SignInAsync();
			tuned.ExpiresOn.Should().Be(_clock.Now.AddHours(8));
			tuned.SharedIdleLockMinutes.Should().Be(3);

			(await SignInAsync(requested: false)).SharedMode.Should().BeFalse();
		}

		[Test]
		public async Task A_department_requirement_applies_even_with_the_gate_off_and_to_sign_ins_that_do_not_name_their_app()
		{
			_gateOn = false;
			_policy.SharedModeRequiredApps = (int)(SharedModeApps.Unit | SharedModeApps.Dispatch);

			async Task<bool> Shared(UserSessionClientApplication client) => (await SignInAsync(client, requested: false)).SharedMode;

			(await Shared(UserSessionClientApplication.Unit)).Should().BeTrue("turning the gate off never waives a department requirement");
			(await Shared(UserSessionClientApplication.Dispatch)).Should().BeTrue();
			(await Shared(UserSessionClientApplication.Command)).Should().BeFalse("IC is not required here");
			(await Shared(UserSessionClientApplication.Api)).Should().BeTrue("leaving the app header out cannot relax the requirement");
			(await Shared(UserSessionClientApplication.UnknownLegacy)).Should().BeTrue();
			(await Shared(UserSessionClientApplication.Responder)).Should().BeFalse();
			(await Shared(UserSessionClientApplication.Web)).Should().BeFalse();
			_rows.Rows.Where(r => r.SharedMode).Should().OnlyContain(r => r.SharedModeSource == (int)SharedModeSource.DepartmentRequired);

			_policy.SharedModeRequiredApps = 0;
			(await Shared(UserSessionClientApplication.Api)).Should().BeFalse("with nothing required, an unlabeled sign-in is personal");
		}

		[Test]
		public void A_sign_in_is_shared_by_the_departments_requirement_or_by_the_installations_request_while_the_gate_is_on()
		{
			var unitRequired = new DepartmentSecurityPolicy { SharedModeRequiredApps = (int)SharedModeApps.Unit };

			SharedSessionRules.SourceFor(unitRequired, UserSessionClientApplication.Unit, false, false).Should().Be(SharedModeSource.DepartmentRequired,
				"turning the gate off never waives the department's requirement");
			SharedSessionRules.SourceFor(unitRequired, UserSessionClientApplication.Unit, true, true).Should().Be(SharedModeSource.DepartmentRequired);
			SharedSessionRules.SourceFor(null, UserSessionClientApplication.Unit, true, true).Should().Be(SharedModeSource.InstallationRequested);
			SharedSessionRules.SourceFor(null, UserSessionClientApplication.Unit, true, false).Should().Be(SharedModeSource.None,
				"an installation's own request needs the gate");
			SharedSessionRules.SourceFor(null, UserSessionClientApplication.Unit, false, true).Should().Be(SharedModeSource.None);
			SharedSessionRules.SourceFor(unitRequired, UserSessionClientApplication.Dispatch, false, true).Should().Be(SharedModeSource.None);
		}

		[Test]
		public void The_rules_for_requests_and_ranges_hold_at_their_edges()
		{
			SharedSessionRules.IsRequested("true").Should().BeTrue();
			SharedSessionRules.IsRequested(" 1 ").Should().BeTrue();
			SharedSessionRules.IsRequested("Shared").Should().BeTrue();
			SharedSessionRules.IsRequested("false").Should().BeFalse();
			SharedSessionRules.IsRequested(null).Should().BeFalse();

			SharedSessionRules.IdleLockMinutes(new DepartmentSecurityPolicy { SharedIdleLockMinutes = 99 }).Should().Be(15);
			SharedSessionRules.IdleLockMinutes(new DepartmentSecurityPolicy { SharedIdleLockMinutes = 0 }).Should().Be(1, "a bad value is stricter, never off");
			SharedSessionRules.ShiftHours(new DepartmentSecurityPolicy { SharedShiftHours = 48 }).Should().Be(24);
			SharedSessionRules.IdleLockMinutes(null).Should().Be(5);
			SharedSessionRules.ShiftHours(null).Should().Be(12);

			var valid = new DepartmentSecurityPolicy { SharedIdleLockMinutes = 15, SharedShiftHours = 24, SharedModeRequiredApps = 7 };
			DepartmentSecurityPolicyDecisions.SharedPolicyValid(valid).Should().BeTrue();
			DepartmentSecurityPolicyDecisions.SharedPolicyValid(new DepartmentSecurityPolicy { SharedIdleLockMinutes = 16 }).Should().BeFalse();
			DepartmentSecurityPolicyDecisions.SharedPolicyValid(new DepartmentSecurityPolicy { SharedIdleLockMinutes = 0 }).Should().BeFalse();
			DepartmentSecurityPolicyDecisions.SharedPolicyValid(new DepartmentSecurityPolicy { SharedShiftHours = 25 }).Should().BeFalse();
			DepartmentSecurityPolicyDecisions.SharedPolicyValid(new DepartmentSecurityPolicy { SharedShiftHours = 0 }).Should().BeFalse();
			DepartmentSecurityPolicyDecisions.SharedPolicyValid(new DepartmentSecurityPolicy { SharedModeRequiredApps = 8 }).Should().BeFalse();

			var before = new DepartmentSecurityPolicy { SharedModeRequiredApps = (int)SharedModeApps.Unit };
			DepartmentSecurityPolicyDecisions.AddsSharedRequirement(before, new DepartmentSecurityPolicy { SharedModeRequiredApps = 0 }).Should().BeFalse();
			DepartmentSecurityPolicyDecisions.AddsSharedRequirement(before, new DepartmentSecurityPolicy { SharedModeRequiredApps = 3 }).Should().BeTrue();
			DepartmentSecurityPolicyDecisions.SharedPolicyChanged(before, DepartmentSecurityPolicyDecisions.SnapshotMfaRules(before)).Should().BeFalse();
			DepartmentSecurityPolicyDecisions.SharedPolicyChanged(before, new DepartmentSecurityPolicy { SharedModeRequiredApps = 1, SharedShiftHours = 11 })
				.Should().BeTrue();
		}

		// ---- Idle deadline and shift ceiling (plan section 12.5.3) -------------------------------------------------------

		[Test]
		public async Task A_passed_idle_deadline_locks_the_session_once_and_durably()
		{
			var session = await SignInAsync();

			_clock.Now = _clock.Now.AddMinutes(5).AddSeconds(-1);
			(await ValidateAsync(session.UserSessionId)).IsValid.Should().BeTrue();

			_clock.Now = _clock.Now.AddSeconds(1);
			var locked = await ValidateAsync(session.UserSessionId);
			locked.IsValid.Should().BeFalse("the server locks at its own deadline, whatever the client shows");
			locked.IsLocked.Should().BeTrue();
			locked.FailureCode.Should().Be(SharedSessionRules.LockedFailureCode);
			locked.Session.LockVersion.Should().Be(1);

			var row = _rows.Row(session.UserSessionId);
			row.IsLocked.Should().BeTrue();
			row.LockVersion.Should().Be(1);
			row.LockReason.Should().Be((int)SharedSessionLockReason.Idle);
			row.LockedOnUtc.Should().Be(_clock.Now);
			Audited(SystemAuditTypes.SharedSessionLocked).Should().Be(1);

			_clock.Now = _clock.Now.AddMinutes(1);
			(await ValidateAsync(session.UserSessionId)).IsLocked.Should().BeTrue();
			_rows.Row(session.UserSessionId).LockVersion.Should().Be(1, "a locked session is not locked again");
			Audited(SystemAuditTypes.SharedSessionLocked).Should().Be(1);
		}

		[Test]
		public async Task Concurrent_validators_past_the_deadline_lock_it_exactly_once()
		{
			var session = await SignInAsync();
			_clock.Now = _clock.Now.AddMinutes(6);

			var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => ValidateAsync(session.UserSessionId))));

			results.Should().OnlyContain(r => !r.IsValid && r.IsLocked, "no request gets through on the stale row");
			results.Should().OnlyContain(r => r.Session.LockVersion == 1,
				"a request that lost the race reports the lock that won, so the unlock screen asks for the current version");
			_rows.Row(session.UserSessionId).LockVersion.Should().Be(1);
			Audited(SystemAuditTypes.SharedSessionLocked).Should().Be(1);
		}

		[TestCase("locked")]
		[TestCase("revoked")]
		[TestCase("unlocked-and-active")]
		public async Task A_validator_that_loses_the_idle_lock_race_reports_what_the_winner_did(string winner)
		{
			// The row this request read was unlocked and past its deadline; another request changed it first.
			var stale = new UserSession
			{
				UserSessionId = "s-1", UserId = UserId, DepartmentId = DepartmentId, AuthenticationGeneration = 4, State = (int)UserSessionState.Active,
				SharedMode = true, SharedIdleLockMinutes = 5, CreatedOn = _clock.Now.AddMinutes(-10), LastOperatorActivityOn = _clock.Now.AddMinutes(-10),
				ExpiresOn = _clock.Now.AddHours(2)
			};
			var current = new UserSession
			{
				UserSessionId = "s-1", UserId = UserId, DepartmentId = DepartmentId, AuthenticationGeneration = 4, SharedMode = true, SharedIdleLockMinutes = 5,
				CreatedOn = stale.CreatedOn, ExpiresOn = stale.ExpiresOn,
				State = winner == "revoked" ? (int)UserSessionState.Revoked : (int)UserSessionState.Active,
				IsLocked = winner == "locked", LockVersion = 1, LockedOnUtc = _clock.Now.AddSeconds(-1),
				LastOperatorActivityOn = winner == "unlocked-and-active" ? _clock.Now : stale.LastOperatorActivityOn
			};
			var rows = new Mock<IUserSessionsRepository>();
			rows.SetupSequence(r => r.GetByIdAsync("s-1")).ReturnsAsync(stale).ReturnsAsync(current);
			rows.Setup(r => r.TryLockAsync("s-1", 0, It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
			var identity = new Mock<IIdentityUserRepository>();
			identity.Setup(r => r.GetByIdAsync(UserId)).ReturnsAsync(() => _user);
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(UserId, DepartmentId, true))
				.ReturnsAsync(new DepartmentMember { UserId = UserId, DepartmentId = DepartmentId });
			var service = new UserSessionService(rows.Object, identity.Object, Mock.Of<IIdentityRepository>(), departments.Object, Sso().Object,
				new ClientSessionMetadataParser(), Mock.Of<IIpLocationProvider>(), Gates(), Audits(), _events.Object, _clock);

			var result = await service.ValidateAsync(new SessionPrincipalContext
			{
				UserId = UserId, SessionId = "s-1", AuthenticationGeneration = 4, DepartmentId = DepartmentId, CredentialIssuedOn = _clock.Now
			});

			switch (winner)
			{
				case "locked":
					result.IsLocked.Should().BeTrue();
					result.Session.LockVersion.Should().Be(1, "the lock that won, not this request's stale copy");
					break;
				case "revoked":
					result.IsValid.Should().BeFalse();
					result.FailureCode.Should().Be("session_revoked");
					break;
				default:
					result.IsValid.Should().BeTrue("the operator unlocked and was active before this request's write");
					result.Session.LastOperatorActivityOn.Should().Be(_clock.Now);
					break;
			}
			Audited(SystemAuditTypes.SharedSessionLocked).Should().Be(0, "only the winner audits");
		}

		[Test]
		public async Task Operator_activity_moves_the_idle_deadline_but_never_revives_a_lapsed_session()
		{
			var session = await SignInAsync();
			var service = Sessions();

			_clock.Now = _clock.Now.AddMinutes(4);
			await service.RecordOperatorActivityAsync(_rows.Row(session.UserSessionId));
			_rows.Row(session.UserSessionId).LastOperatorActivityOn.Should().Be(_clock.Now);

			var recorded = _clock.Now;
			_clock.Now = _clock.Now.AddSeconds(10);
			await service.RecordOperatorActivityAsync(_rows.Row(session.UserSessionId));
			_rows.Row(session.UserSessionId).LastOperatorActivityOn.Should().Be(recorded, "writes are bounded to one per interval");

			_clock.Now = recorded.AddMinutes(4);
			(await ValidateAsync(session.UserSessionId)).IsValid.Should().BeTrue("the deadline moved to nine minutes");

			// The operator's copy was read before the deadline; the write still refuses to bring a lapsed session back.
			var stale = _rows.Row(session.UserSessionId);
			_clock.Now = recorded.AddMinutes(5).AddSeconds(1);
			await service.RecordOperatorActivityAsync(stale);
			_rows.Row(session.UserSessionId).LastOperatorActivityOn.Should().Be(recorded);
			(await ValidateAsync(session.UserSessionId)).IsLocked.Should().BeTrue();

			await service.RecordOperatorActivityAsync(_rows.Row(session.UserSessionId));
			_rows.Row(session.UserSessionId).LastOperatorActivityOn.Should().Be(recorded, "a locked session records no activity");
		}

		[Test]
		public async Task Activity_never_passes_the_shift_ceiling()
		{
			_policy.SharedShiftHours = 1;
			_policy.SharedIdleLockMinutes = 15;
			var session = await SignInAsync();
			var service = Sessions();

			for (var minute = 10; minute < 60; minute += 10)
			{
				_clock.Now = _clock.Now.AddMinutes(10);
				(await ValidateAsync(session.UserSessionId)).IsValid.Should().BeTrue();
				await service.RecordOperatorActivityAsync(_rows.Row(session.UserSessionId));
			}

			_clock.Now = _clock.Now.AddMinutes(10);
			var ended = await ValidateAsync(session.UserSessionId);
			ended.IsValid.Should().BeFalse();
			ended.IsLocked.Should().BeFalse();
			ended.FailureCode.Should().Be(SharedSessionRules.ExpiredFailureCode);
		}

		[Test]
		public async Task A_stricter_policy_reaches_running_sessions_and_a_looser_one_never_extends_them()
		{
			var session = await SignInAsync();
			_policy.SharedIdleLockMinutes = 2;
			_clock.Now = _clock.Now.AddMinutes(2);
			(await ValidateAsync(session.UserSessionId)).IsLocked.Should().BeTrue("the stricter current idle lock applies at once");

			_clock.Now = _clock.Now.AddMinutes(-2);
			_policy.SharedIdleLockMinutes = 15;
			var second = await SignInAsync();
			_policy.SharedIdleLockMinutes = 5;
			_clock.Now = _clock.Now.AddMinutes(5);
			(await ValidateAsync(second.UserSessionId)).IsLocked.Should().BeTrue("the stricter of recorded and current wins");

			_clock.Now = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc);
			_policy.SharedIdleLockMinutes = 15;
			var third = await SignInAsync();
			_policy.SharedShiftHours = 2;
			_clock.Now = _clock.Now.AddHours(2);
			(await ValidateAsync(third.UserSessionId)).FailureCode.Should().Be(SharedSessionRules.ExpiredFailureCode, "a shorter shift ends it now");

			_clock.Now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
			_policy.SharedShiftHours = 1;
			var fourth = await SignInAsync();
			_policy.SharedShiftHours = 24;
			_clock.Now = _clock.Now.AddMinutes(61);
			(await ValidateAsync(fourth.UserSessionId)).FailureCode.Should().Be(SharedSessionRules.ExpiredFailureCode,
				"a longer shift never extends the ceiling set at sign-in");
		}

		[Test]
		public async Task A_personal_session_is_untouched_by_the_shared_rules()
		{
			var session = await SignInAsync(requested: false);
			_clock.Now = _clock.Now.AddHours(20);
			(await ValidateAsync(session.UserSessionId)).IsValid.Should().BeTrue();

			await Sessions().RecordOperatorActivityAsync(_rows.Row(session.UserSessionId));
			_rows.Row(session.UserSessionId).LastOperatorActivityOn.Should().BeNull();

			var shared = Shared();
			(await shared.LockAsync(_rows.Row(session.UserSessionId), Request())).Outcome.Should().Be(SharedSessionOutcome.NotShared);
			(await shared.EndShiftAsync(_rows.Row(session.UserSessionId), false, Request())).Outcome.Should().Be(SharedSessionOutcome.NotShared);
			(await shared.CanUnlockAsync(_rows.Row(session.UserSessionId))).Should().Be(SharedSessionOutcome.NotShared);
			_rows.Row(session.UserSessionId).State.Should().Be((int)UserSessionState.Active);
			ProtectedGrantSessionContext.From(_rows.Row(session.UserSessionId), null).SessionLockVersion.Should().BeNull();
		}

		// ---- Lock and unlock (plan section 12.5.3) ------------------------------------------------------------------------

		[Test]
		public async Task Lock_advances_the_version_once_and_locking_again_is_harmless()
		{
			var session = await SignInAsync();
			var shared = Shared();

			var stale = _rows.Row(session.UserSessionId);
			var locked = await shared.LockAsync(_rows.Row(session.UserSessionId), Request());
			locked.Succeeded.Should().BeTrue();
			locked.LockVersion.Should().Be(1);
			var row = _rows.Row(session.UserSessionId);
			row.IsLocked.Should().BeTrue();
			row.LockReason.Should().Be((int)SharedSessionLockReason.Explicit);
			var audit = _audited.Single(a => a.Type == (int)SystemAuditTypes.SharedSessionLocked);
			audit.UserId.Should().Be(UserId);
			audit.Data.Should().Contain("Installation=Engine 7 tablet").And.Contain("LockVersion=1");

			var again = await shared.LockAsync(stale, Request());
			again.Succeeded.Should().BeTrue("the stale copy still said unlocked; the row says locked");
			again.LockVersion.Should().Be(1);
			_rows.Row(session.UserSessionId).LockVersion.Should().Be(1);
			Audited(SystemAuditTypes.SharedSessionLocked).Should().Be(1);
		}

		[Test]
		public async Task Unlock_resumes_the_same_session_once_at_its_lock_version()
		{
			var session = await SignInAsync();
			var shared = Shared();
			await shared.LockAsync(_rows.Row(session.UserSessionId), Request());
			var expiresOn = _rows.Row(session.UserSessionId).ExpiresOn;

			_clock.Now = _clock.Now.AddMinutes(3);
			(await shared.CanUnlockAsync(_rows.Row(session.UserSessionId))).Should().Be(SharedSessionOutcome.Succeeded);
			(await shared.UnlockAsync(_rows.Row(session.UserSessionId), 0, MfaEvidenceMethod.Totp, null, _clock.Now, Request())).Outcome
				.Should().Be(SharedSessionOutcome.LockChanged, "an unlock begun before this lock does not open it");

			var unlocked = await shared.UnlockAsync(_rows.Row(session.UserSessionId), 1, MfaEvidenceMethod.Totp, null, _clock.Now, Request());
			unlocked.Succeeded.Should().BeTrue();
			var row = _rows.Row(session.UserSessionId);
			row.IsLocked.Should().BeFalse();
			row.LockVersion.Should().Be(1, "unlock never advances the version, so a pre-lock grant stays one version behind");
			row.LastOperatorActivityOn.Should().Be(_clock.Now, "the idle deadline restarts");
			row.ExpiresOn.Should().Be(expiresOn, "the shift is not extended");
			row.UserSessionId.Should().Be(session.UserSessionId, "the same session resumes");

			var evidence = _evidenceRows.Rows.Single();
			evidence.Kind.Should().Be((int)MfaEvidenceKind.SecondFactor);
			evidence.Purpose.Should().Be((int)MfaEvidencePurpose.SharedUnlock);
			evidence.SessionKey.Should().Be(MfaEvidence.TrackedSessionKey(session.UserSessionId));
			_evidenceRows.Rows.Should().NotContain(e => e.Kind == (int)MfaEvidenceKind.FirstFactor, "unlock never updates the first-factor time");
			Audited(SystemAuditTypes.SharedSessionUnlocked).Should().Be(1);

			(await shared.UnlockAsync(_rows.Row(session.UserSessionId), 1, MfaEvidenceMethod.Totp, null, _clock.Now, Request())).Outcome
				.Should().Be(SharedSessionOutcome.NotLocked, "one lock is unlocked at most once");
			(await ValidateAsync(session.UserSessionId)).IsValid.Should().BeTrue();
		}

		[Test]
		public async Task Concurrent_unlocks_of_one_lock_succeed_exactly_once()
		{
			var session = await SignInAsync();
			await Shared().LockAsync(_rows.Row(session.UserSessionId), Request());
			var copy = _rows.Row(session.UserSessionId);

			var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
				Task.Run(() => Shared().UnlockAsync(copy, 1, MfaEvidenceMethod.Totp, null, _clock.Now, Request()))));

			results.Count(r => r.Succeeded).Should().Be(1);
			_evidenceRows.Rows.Should().ContainSingle();
		}

		[Test]
		public async Task Nothing_verified_before_a_lock_counts_after_the_unlock()
		{
			var session = await SignInAsync();
			var key = MfaEvidence.TrackedSessionKey(session.UserSessionId);
			var evidence = Evidence();
			await evidence.RecordAsync(UserId, key, UserSessionClientApplication.Unit, MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password,
				MfaEvidencePurpose.Login, _clock.Now, 4, DepartmentId);
			await evidence.RecordAsync(UserId, key, UserSessionClientApplication.Unit, MfaEvidenceKind.SecondFactor, MfaEvidenceMethod.Totp,
				MfaEvidencePurpose.Login, _clock.Now, 4, DepartmentId);
			(await evidence.GetLatestSecondFactorAsync(UserId, key, 4)).Should().NotBeNull();

			_clock.Now = _clock.Now.AddMinutes(1);
			await Shared().LockAsync(_rows.Row(session.UserSessionId), Request());
			(await evidence.GetLatestSecondFactorAsync(UserId, key, 4)).Should().BeNull("nothing counts while locked");
			(await evidence.GetLatestFirstFactorAsync(UserId, key, 4)).Should().BeNull();

			_clock.Now = _clock.Now.AddMinutes(1);
			await Shared().UnlockAsync(_rows.Row(session.UserSessionId), 1, MfaEvidenceMethod.Totp, null, _clock.Now, Request());
			var latest = await evidence.GetLatestSecondFactorAsync(UserId, key, 4);
			latest.Should().NotBeNull();
			latest.Purpose.Should().Be((int)MfaEvidencePurpose.SharedUnlock, "only the unlock itself counts");
			(await evidence.GetLatestFirstFactorAsync(UserId, key, 4)).Should().BeNull("the pre-lock password does not count as fresh after it");
			(await evidence.HasFreshFirstFactorAsync(UserId, key, 4, TimeSpan.FromHours(1), _clock.Now)).Should().BeFalse();

			var personal = await SignInAsync(requested: false);
			var personalKey = MfaEvidence.TrackedSessionKey(personal.UserSessionId);
			await evidence.RecordAsync(UserId, personalKey, UserSessionClientApplication.Unit, MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password,
				MfaEvidencePurpose.Login, _clock.Now, 4, DepartmentId);
			(await evidence.GetLatestFirstFactorAsync(UserId, personalKey, 4)).Should().NotBeNull("a personal session on another device is untouched");
		}

		[Test]
		public async Task Unlock_is_refused_when_the_session_ended_or_the_department_now_requires_sso()
		{
			var shared = Shared();
			var session = await SignInAsync();
			(await shared.CanUnlockAsync(_rows.Row(session.UserSessionId))).Should().Be(SharedSessionOutcome.NotLocked);
			await shared.LockAsync(_rows.Row(session.UserSessionId), Request());

			_policy.RequireSso = true;
			(await shared.CanUnlockAsync(_rows.Row(session.UserSessionId))).Should().Be(SharedSessionOutcome.Succeeded, "no SSO is configured yet");
			_ssoConfigs = new[] { new DepartmentSsoConfig { DepartmentId = DepartmentId, IsEnabled = true } };
			(await shared.CanUnlockAsync(_rows.Row(session.UserSessionId))).Should().Be(SharedSessionOutcome.SsoReauthenticationRequired,
				"a password session cannot be resumed once the department requires SSO");

			var sso = await SignInAsync(method: UserSessionAuthenticationMethod.OidcSso);
			await shared.LockAsync(_rows.Row(sso.UserSessionId), Request());
			(await shared.CanUnlockAsync(_rows.Row(sso.UserSessionId))).Should().Be(SharedSessionOutcome.Succeeded);

			_clock.Now = _clock.Now.AddHours(12);
			(await shared.CanUnlockAsync(_rows.Row(sso.UserSessionId))).Should().Be(SharedSessionOutcome.SessionEnded, "the shift is over");
			(await shared.UnlockAsync(_rows.Row(sso.UserSessionId), 1, MfaEvidenceMethod.Totp, null, _clock.Now, Request())).Outcome
				.Should().Be(SharedSessionOutcome.SessionEnded);

			_clock.Now = _clock.Now.AddHours(-12);
			await Sessions().RevokeSessionAsync(UserId, UserId, sso.UserSessionId, UserSessionRevocationReason.AdministratorRevoked);
			(await shared.CanUnlockAsync(_rows.Row(sso.UserSessionId))).Should().Be(SharedSessionOutcome.SessionEnded);
		}

		[Test]
		public async Task End_shift_and_switch_operator_end_the_session()
		{
			var shared = Shared();
			var first = await SignInAsync();
			var second = await SignInAsync();

			(await shared.EndShiftAsync(_rows.Row(first.UserSessionId), switchOperator: true, Request())).Succeeded.Should().BeTrue();
			(await shared.EndShiftAsync(_rows.Row(second.UserSessionId), switchOperator: false, Request())).Succeeded.Should().BeTrue();

			_rows.Row(first.UserSessionId).State.Should().Be((int)UserSessionState.Revoked);
			_rows.Row(first.UserSessionId).RevocationReason.Should().Be((int)UserSessionRevocationReason.OperatorSwitched);
			_rows.Row(second.UserSessionId).RevocationReason.Should().Be((int)UserSessionRevocationReason.ShiftEnded);
			_audited.Where(a => a.Type == (int)SystemAuditTypes.SharedSessionEnded).Select(a => a.Data).Should()
				.Contain(d => d.Contains("switch operator")).And.Contain(d => d.Contains("end shift"));
			(await ValidateAsync(first.UserSessionId)).FailureCode.Should().Be("session_revoked");

			(await shared.EndShiftAsync(_rows.Row(first.UserSessionId), false, Request())).Succeeded.Should().BeTrue("ending an ended shift is harmless");
			Audited(SystemAuditTypes.SharedSessionEnded).Should().Be(2);
		}

		[Test]
		public async Task The_status_reports_the_deadlines_under_the_current_policy()
		{
			var session = await SignInAsync();
			var status = await Shared().GetStatusAsync(_rows.Row(session.UserSessionId));
			status.Shared.Should().BeTrue();
			status.Locked.Should().BeFalse();
			status.IdleLockMinutes.Should().Be(5);
			status.IdleLocksOnUtc.Should().Be(_clock.Now.AddMinutes(5));
			status.ShiftEndsOnUtc.Should().Be(_clock.Now.AddHours(12));
			status.InstallationLabel.Should().Be("Engine 7 tablet");

			_policy.SharedShiftHours = 6;
			await Shared().LockAsync(_rows.Row(session.UserSessionId), Request());
			status = await Shared().GetStatusAsync(_rows.Row(session.UserSessionId));
			status.Locked.Should().BeTrue();
			status.LockReason.Should().Be(SharedSessionLockReason.Explicit);
			status.IdleLocksOnUtc.Should().BeNull();
			status.ShiftEndsOnUtc.Should().Be(_clock.Now.AddHours(6));
		}

		// ---- Everything from before a lock (plan sections 5.5 and 7.9) ----------------------------------------------------

		[Test]
		public async Task Grants_follow_the_live_lock_version_and_a_shared_caller_refuses_version_one_grants()
		{
			var session = await SignInAsync();
			var before = ProtectedGrantSessionContext.From(_rows.Row(session.UserSessionId), _clock.Now);
			before.SessionLockVersion.Should().Be(0);
			before.SharedMode.Should().BeTrue();
			before.SessionLockedOnUtc.Should().BeNull();

			var grant = new ProtectedDataGrant
			{
				Version = 2, UserId = UserId, SessionId = session.UserSessionId, ClientApp = (int)UserSessionClientApplication.Unit,
				AuthenticationGeneration = 4, SessionLockVersion = 0, MfaMethod = ProtectedDataGrantMfaMethods.Totp
			};
			ProtectedGrantBinding.Check(grant, UserId, before).Should().Be(ProtectedGrantBindingOutcome.Bound);

			await Shared().LockAsync(_rows.Row(session.UserSessionId), Request());
			await Shared().UnlockAsync(_rows.Row(session.UserSessionId), 1, MfaEvidenceMethod.Totp, null, _clock.Now, Request());
			var after = ProtectedGrantSessionContext.From(_rows.Row(session.UserSessionId), _clock.Now);
			after.SessionLockVersion.Should().Be(1);
			after.SessionLockedOnUtc.Should().NotBeNull();
			ProtectedGrantBinding.Check(grant, UserId, after).Should().Be(ProtectedGrantBindingOutcome.SessionLocked, "the grant is from before the lock");
			ProtectedGrantBinding.ErrorCode(ProtectedGrantBindingOutcome.SessionLocked).Should().Be("grant_session_locked");

			var legacy = new ProtectedDataGrant { Version = 1, UserId = UserId };
			ProtectedGrantBinding.Check(legacy, UserId, after).Should().Be(ProtectedGrantBindingOutcome.SessionLocked,
				"a version 1 grant cannot prove it was issued since the lock");
			ProtectedGrantBinding.Check(legacy, UserId, ProtectedGrantSessionContext.From(await SignInAsync(requested: false), _clock.Now))
				.Should().Be(ProtectedGrantBindingOutcome.Bound, "personal sessions keep the bounded legacy reading");
			ProtectedGrantBinding.Check(legacy, UserId, null).Should().Be(ProtectedGrantBindingOutcome.Bound);
		}

		[Test]
		public async Task A_shared_responder_session_never_approves()
		{
			var shared = await SignInAsync(UserSessionClientApplication.Responder);
			var personal = await SignInAsync(UserSessionClientApplication.Responder, requested: false);

			ApprovalApprovers.IsEligibleSession(_rows.Row(shared.UserSessionId), UserId, 4, _clock.Now).Should().BeFalse();
			ApprovalApprovers.IsEligibleSession(_rows.Row(personal.UserSessionId), UserId, 4, _clock.Now).Should().BeTrue();
		}

		[Test]
		public async Task Unlock_evidence_serves_protected_data_only_where_the_department_allows_it()
		{
			var policy = new MfaPolicyService(Sso().Object, new InMemoryUserMfaStateRepository(), Gates());
			var unlock = new MfaEvidence { Method = (int)MfaEvidenceMethod.Totp, Purpose = (int)MfaEvidencePurpose.SharedUnlock };
			var login = new MfaEvidence { Method = (int)MfaEvidenceMethod.Totp, Purpose = (int)MfaEvidencePurpose.Login };

			_policy.AcceptRecentUnlockMfaForAdp = false;
			(await policy.IsEvidenceAcceptedAsync(DepartmentId, MfaMethodScope.Adp, unlock)).Should().BeFalse();
			(await policy.IsEvidenceAcceptedAsync(DepartmentId, MfaMethodScope.SecurityChange, unlock)).Should().BeTrue("only protected data has the switch");
			(await policy.IsEvidenceAcceptedAsync(DepartmentId, MfaMethodScope.Adp, login)).Should().BeTrue();

			_policy.AcceptRecentUnlockMfaForAdp = true;
			(await policy.IsEvidenceAcceptedAsync(DepartmentId, MfaMethodScope.Adp, unlock)).Should().BeTrue();
			(await policy.IsEvidenceAcceptedAsync(DepartmentId, MfaMethodScope.Adp, null)).Should().BeFalse();

			_policy.AllowPasskeysForAdp = false;
			(await policy.IsEvidenceAcceptedAsync(DepartmentId, MfaMethodScope.Adp,
				new MfaEvidence { Method = (int)MfaEvidenceMethod.Passkey, Purpose = (int)MfaEvidencePurpose.SharedUnlock })).Should().BeFalse(
				"the method still has to be accepted");
		}

		// ---- Realtime connections (slice 16) --------------------------------------------------------------------------------

		private void Closed(string sessionId, Times times) =>
			_events.Verify(e => e.PublishAsync(sessionId, It.Is<SessionEventMessage>(m => m.Name == SessionEvents.SessionClosed), It.IsAny<CancellationToken>()), times);

		[Test]
		public async Task The_connection_sweep_finds_every_session_that_can_no_longer_be_used()
		{
			_policy.SharedIdleLockMinutes = 15;
			var idleLong = await SignInAsync();
			_policy.SharedIdleLockMinutes = 5;
			var active = await SignInAsync();
			var idle = await SignInAsync();
			var personal = await SignInAsync(requested: false);
			var revoked = await SignInAsync(requested: false);
			await Sessions().RevokeSessionAsync(UserId, UserId, revoked.UserSessionId, UserSessionRevocationReason.UserRevoked);
			_rows.Add(new UserSession
			{
				UserSessionId = "expired", UserId = UserId, State = (int)UserSessionState.Active, CreatedOn = _clock.Now.AddHours(-2), ExpiresOn = _clock.Now.AddMinutes(1)
			});

			_clock.Now = _clock.Now.AddMinutes(4);
			await Sessions().RecordOperatorActivityAsync(_rows.Row(active.UserSessionId));
			var locked = await SignInAsync();
			await Shared().LockAsync(_rows.Row(locked.UserSessionId), Request());
			_clock.Now = _clock.Now.AddMinutes(2);

			var unusable = await Sessions().GetUnusableSessionIdsAsync(new[]
			{
				active.UserSessionId, idle.UserSessionId, idleLong.UserSessionId, locked.UserSessionId, personal.UserSessionId, revoked.UserSessionId, "expired",
				"missing"
			});
			unusable.Should().BeEquivalentTo(new[] { idle.UserSessionId, locked.UserSessionId, revoked.UserSessionId, "expired", "missing" },
				"each shared session keeps the idle lock it signed in with, and a locked one is out however recent its activity");

			_clock.Now = _clock.Now.AddHours(13);
			(await Sessions().GetUnusableSessionIdsAsync(new[] { active.UserSessionId })).Should().ContainSingle("past its shift");
			(await Sessions().GetUnusableSessionIdsAsync(Array.Empty<string>())).Should().BeEmpty();
		}

		[Test]
		public async Task A_lock_idle_lock_or_revocation_closes_the_sessions_connections_at_once()
		{
			var explicitLock = await SignInAsync();
			await Shared().LockAsync(_rows.Row(explicitLock.UserSessionId), Request());
			Closed(explicitLock.UserSessionId, Times.Once());
			await Shared().LockAsync(_rows.Row(explicitLock.UserSessionId), Request());
			Closed(explicitLock.UserSessionId, Times.Once()); // locking a locked session closes nothing new

			var idle = await SignInAsync();
			_clock.Now = _clock.Now.AddMinutes(6);
			await ValidateAsync(idle.UserSessionId);
			await ValidateAsync(idle.UserSessionId);
			Closed(idle.UserSessionId, Times.Once());

			var ended = await SignInAsync();
			await Shared().EndShiftAsync(_rows.Row(ended.UserSessionId), false, Request());
			Closed(ended.UserSessionId, Times.Once());
			await Sessions().RevokeSessionAsync(UserId, UserId, ended.UserSessionId, UserSessionRevocationReason.UserRevoked);
			Closed(ended.UserSessionId, Times.Once()); // an ended session is not closed again
		}

		[Test]
		public async Task A_failed_unlock_is_the_operators_recent_activity_on_that_shared_installation()
		{
			var shared = await SignInAsync();
			await Shared().LockAsync(_rows.Row(shared.UserSessionId), Request());

			await Shared().RecordFailedUnlockAsync(_rows.Row(shared.UserSessionId), MfaMethodNames.Passkey, Request());
			await Shared().RecordFailedUnlockAsync(null, MfaMethodNames.Totp, Request());

			var failed = _activityRows.Rows.Should().ContainSingle("nothing is recorded without a session").Subject;
			failed.Successful.Should().BeFalse();
			failed.Method.Should().Be((int)MfaEvidenceMethod.Passkey);
			failed.Purpose.Should().Be((int)MfaEvidencePurpose.SharedUnlock);
			failed.SharedMode.Should().BeTrue();
			failed.SessionId.Should().Be(shared.UserSessionId);
			failed.ClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			failed.DepartmentId.Should().Be(DepartmentId);
			_audited.Should().Contain(a => a.Type == (int)SystemAuditTypes.SharedSessionUnlocked && !a.Successful);
		}

		[Test]
		public void Shared_audits_carry_labels_but_nothing_from_the_session_credentials()
		{
			var session = new UserSession
			{
				UserSessionId = "0123456789abcdef", ClientApplication = (int)UserSessionClientApplication.Dispatch, LockVersion = 3,
				DeviceName = "Console 2;\r\nInjected=1"
			};
			SharedSessionAudit.SessionSuffix(session.UserSessionId).Should().Be("89abcdef");
			var text = SharedSessionAudit.Describe("locked", session, "idle");
			text.Should().Be("Shared session locked. Client=Dispatch; Installation=Console 2,  Injected=1; LockVersion=3; idle.");
		}
	}
}
