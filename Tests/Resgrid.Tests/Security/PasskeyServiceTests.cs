using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fido2NetLib;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Authentication;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 7: enrollment, inventory, revocation and step-up assertions end to end, with the real
	/// Fido2 adapter, challenge service and evidence service over in-memory stores that keep the SQL repositories' rules.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PasskeyServiceTests
	{
		private const string UserId = "user-1";
		private const string OtherUserId = "user-2";
		private const string SessionId = "session-1";
		private const int DepartmentId = 42;
		private static readonly DateTime Start = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = Start;
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private Clock _clock;
		private InMemoryUserPasskeyRepository _passkeys;
		private InMemoryMfaApprovalRequestRepository _approvals;
		private Mock<ISecurityNoticeService> _notices;
		private InMemoryAuthenticationChallengeRepository _challenges;
		private InMemoryMfaEvidenceRepository _evidenceRows;
		private MfaEvidenceService _evidence;
		private Mock<IPasskeyFeatureGates> _gates;
		private Mock<ISystemAuditsService> _audits;
		private Mock<IUserSessionService> _userSessions;
		private List<UserSession> _activeSessions;
		private PasskeyService _service;
		private int _cap;

		[SetUp]
		public void SetUp()
		{
			_cap = PasskeyConfig.MaxActiveCredentialsPerClient;
			_clock = new Clock();
			_passkeys = new InMemoryUserPasskeyRepository();
			_approvals = new InMemoryMfaApprovalRequestRepository();
			_notices = new Mock<ISecurityNoticeService>();
			_challenges = new InMemoryAuthenticationChallengeRepository();
			_evidenceRows = new InMemoryMfaEvidenceRepository();
			_evidence = new MfaEvidenceService(_evidenceRows, new InMemoryUserMfaStateRepository(), _passkeys, Mock.Of<IUserSessionsRepository>(),
				new InMemoryMfaActivityRepository(), _clock);
			_gates = new Mock<IPasskeyFeatureGates>();
			_gates.SetupGet(g => g.RegistrationEnabled).Returns(true);
			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			_audits = new Mock<ISystemAuditsService>();
			_activeSessions = new List<UserSession>();
			_userSessions = new Mock<IUserSessionService>();
			_userSessions.Setup(s => s.RevokeSessionAsync(UserId, UserId, It.IsAny<string>(), UserSessionRevocationReason.MfaChanged, It.IsAny<CancellationToken>()))
				.ReturnsAsync((string _, string _, string id, UserSessionRevocationReason _, CancellationToken _) =>
					new RevocationResult { RevokedSessionCount = _activeSessions.RemoveAll(x => x.UserSessionId == id) });
			_service = Service(_passkeys);
		}

		[TearDown]
		public void TearDown() => PasskeyConfig.MaxActiveCredentialsPerClient = _cap;

		private PasskeyService Service(IUserPasskeyRepository passkeys)
		{
			var registry = new RelyingPartyRegistry(PasskeyProviderTests.Layout);
			var sessions = new Mock<IUserSessionsRepository>();
			sessions.Setup(s => s.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync(new UserSession { DeviceName = "Engine 7 tablet", OperatingSystem = "Android", Browser = "Chrome" });
			sessions.Setup(s => s.GetActiveByUserAsync(UserId, It.IsAny<DateTime>()))
				.ReturnsAsync(() => _activeSessions.ToList());
			var policy = new MfaPolicyService(Mock.Of<IDepartmentSsoService>(), new InMemoryUserMfaStateRepository(), _gates.Object);
			return new PasskeyService(passkeys, new Fido2PasskeyProvider(registry), registry, _gates.Object,
				new AuthenticationChallengeService(_challenges, _clock), _evidence, policy, sessions.Object, _userSessions.Object, _audits.Object, _approvals,
				_notices.Object, _clock);
		}

		private static PasskeyCaller Caller(UserSessionClientApplication client = UserSessionClientApplication.Unit, string userId = UserId,
			string sessionId = SessionId, long generation = 4) => new()
		{
			UserId = userId,
			UserName = userId + "@example.test",
			SessionId = sessionId,
			ClientApplication = client,
			AuthenticationGeneration = generation,
			DepartmentId = DepartmentId,
			AuditSystem = SystemAuditSystems.Api
		};

		private Task FirstFactor(PasskeyCaller caller, int minutesAgo = 1) =>
			_evidence.RecordAsync(caller.UserId, caller.SessionKey, caller.ClientApplication, MfaEvidenceKind.FirstFactor, MfaEvidenceMethod.Password,
				MfaEvidencePurpose.Reauthentication, _clock.Now.AddMinutes(-minutesAgo), caller.AuthenticationGeneration);

		private Task SecondFactor(PasskeyCaller caller, int minutesAgo = 1, MfaEvidenceMethod method = MfaEvidenceMethod.Totp,
			UserSessionClientApplication? client = null, string factorReference = null) =>
			_evidence.RecordAsync(caller.UserId, caller.SessionKey, client ?? caller.ClientApplication, MfaEvidenceKind.SecondFactor, method,
				MfaEvidencePurpose.StepUp, _clock.Now.AddMinutes(-minutesAgo), caller.AuthenticationGeneration, factorReference: factorReference);

		private async Task Verified(PasskeyCaller caller)
		{
			await FirstFactor(caller);
			await SecondFactor(caller);
		}

		private static string Origin(PasskeyCaller caller) => PasskeyProviderTests.Origin(caller.ClientApplication);

		private async Task<(UserPasskey Passkey, SoftPasskeyAuthenticator Authenticator)> Register(PasskeyCaller caller,
			SoftPasskeyAuthenticator authenticator = null, string displayName = null)
		{
			await Verified(caller);
			authenticator ??= new SoftPasskeyAuthenticator();
			var start = await _service.BeginRegistrationAsync(caller, true, 10);
			start.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			var done = await _service.CompleteRegistrationAsync(caller, start.RequestId, authenticator.Register(start.OptionsJson, Origin(caller)), displayName);
			done.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			return (done.Passkey, authenticator);
		}

		private void Audited(SystemAuditTypes type, Times times) =>
			_audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)type && x.UserId == UserId), It.IsAny<CancellationToken>()), times);

		// ---- Registration --------------------------------------------------------------------------------------------

		[Test]
		public async Task Registration_is_refused_while_the_gate_is_off_or_the_app_has_no_relying_party()
		{
			var caller = Caller();
			await Verified(caller);

			_gates.SetupGet(g => g.RegistrationEnabled).Returns(false);
			(await _service.BeginRegistrationAsync(caller, true, 10)).Outcome.Should().Be(PasskeyOutcome.Unavailable);
			_service.IsRegistrationAvailable(UserSessionClientApplication.Unit).Should().BeFalse();

			_gates.SetupGet(g => g.RegistrationEnabled).Returns(true);
			(await _service.BeginRegistrationAsync(Caller(UserSessionClientApplication.Dispatch), true, 10)).Outcome.Should().Be(PasskeyOutcome.Unavailable);
			(await _service.BeginRegistrationAsync(null, true, 10)).Outcome.Should().Be(PasskeyOutcome.SessionRequired);
			(await _service.BeginRegistrationAsync(Caller(sessionId: null), true, 10)).Outcome.Should().Be(PasskeyOutcome.SessionRequired);
		}

		[Test]
		public async Task Registration_needs_an_authenticator_app_and_recovery_codes_first()
		{
			var caller = Caller();
			await Verified(caller);

			(await _service.BeginRegistrationAsync(caller, totpEnrolled: false, recoveryCodesRemaining: 10)).Outcome.Should().Be(PasskeyOutcome.EnrollmentRequired);
			(await _service.BeginRegistrationAsync(caller, totpEnrolled: true, recoveryCodesRemaining: 0)).Outcome.Should().Be(PasskeyOutcome.EnrollmentRequired);
			(await _service.BeginRegistrationAsync(caller, totpEnrolled: true, recoveryCodesRemaining: 1)).Outcome.Should().Be(PasskeyOutcome.Succeeded);
		}

		[Test]
		public async Task Registration_needs_a_fresh_first_factor_and_a_recent_accepted_second_factor()
		{
			var caller = Caller();
			async Task<PasskeyOutcome> Begin() => (await _service.BeginRegistrationAsync(caller, true, 10)).Outcome;

			(await Begin()).Should().Be(PasskeyOutcome.ReauthenticationRequired);
			await FirstFactor(caller, minutesAgo: 6);
			(await Begin()).Should().Be(PasskeyOutcome.ReauthenticationRequired, "a password confirmed six minutes ago is too old");

			await FirstFactor(caller, minutesAgo: 1);
			(await Begin()).Should().Be(PasskeyOutcome.StepUpRequired);

			await SecondFactor(caller, minutesAgo: 6);
			(await Begin()).Should().Be(PasskeyOutcome.StepUpRequired);

			await SecondFactor(caller, minutesAgo: 1, method: MfaEvidenceMethod.PasskeyApproval);
			(await Begin()).Should().Be(PasskeyOutcome.StepUpRequired, "Responder approval never manages account factors");

			_clock.Now = _clock.Now.AddSeconds(1);
			await SecondFactor(caller, minutesAgo: 1, method: MfaEvidenceMethod.Federated);
			(await Begin()).Should().Be(PasskeyOutcome.StepUpRequired, "nor does provider step-up");

			_clock.Now = _clock.Now.AddSeconds(1);
			await SecondFactor(caller, minutesAgo: 1, method: MfaEvidenceMethod.Passkey, client: UserSessionClientApplication.Responder);
			(await Begin()).Should().Be(PasskeyOutcome.StepUpRequired, "a passkey counts only when bound to the app asking");

			_clock.Now = _clock.Now.AddSeconds(1);
			await SecondFactor(caller, minutesAgo: 1, method: MfaEvidenceMethod.Passkey);
			(await Begin()).Should().Be(PasskeyOutcome.Succeeded);

			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(false);
			(await Begin()).Should().Be(PasskeyOutcome.StepUpRequired, "passkey evidence counts only while the deployment accepts passkeys");

			_clock.Now = _clock.Now.AddSeconds(1);
			await SecondFactor(caller, minutesAgo: 1);
			(await Begin()).Should().Be(PasskeyOutcome.Succeeded, "TOTP is always accepted");
		}

		[Test]
		public async Task A_registered_passkey_is_bound_to_its_app_with_server_observed_context()
		{
			var (passkey, authenticator) = await Register(Caller());

			var row = _passkeys.Rows.Single();
			row.UserId.Should().Be(UserId);
			row.ClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			row.RpId.Should().Be("unit.resgrid.test");
			row.CredentialId.Should().Equal(authenticator.CredentialId);
			row.CredentialIdHash.Should().Equal(System.Security.Cryptography.SHA256.HashData(authenticator.CredentialId));
			row.Algorithm.Should().Be(-7);
			row.UserHandle.Should().HaveCount(32);
			row.UserHandle.Should().NotEqual(System.Text.Encoding.UTF8.GetBytes(UserId));
			row.DisplayName.Should().Be("Unit passkey (2026-09-28)");
			row.RegistrationPlatform.Should().Be("Android");
			row.RegistrationInstallation.Should().Be("Engine 7 tablet");
			row.RegistrationUserAgentFamily.Should().Be("Chrome");
			row.RegistrationAttachment.Should().Be("platform");
			row.RegisteredInSharedMode.Should().BeFalse();
			row.StateVersion.Should().Be(1);
			passkey.UserPasskeyId.Should().Be(row.UserPasskeyId);
			Audited(SystemAuditTypes.PasskeyRegistered, Times.Once());
		}

		[Test]
		public async Task A_registration_request_is_single_use_and_bound_to_its_session()
		{
			var caller = Caller();
			await Verified(caller);
			var start = await _service.BeginRegistrationAsync(caller, true, 10);
			var response = new SoftPasskeyAuthenticator().Register(start.OptionsJson, Origin(caller));

			var other = Caller(sessionId: "session-2");
			await Verified(other);
			(await _service.CompleteRegistrationAsync(other, start.RequestId, response, null)).Outcome
				.Should().Be(PasskeyOutcome.ChallengeExpired, "another session cannot complete this session's request");
			(await _service.CompleteRegistrationAsync(Caller(UserSessionClientApplication.Responder), start.RequestId, response, null)).Outcome
				.Should().Be(PasskeyOutcome.ChallengeExpired, "nor can another app");

			(await _service.CompleteRegistrationAsync(caller, start.RequestId, response, null)).Outcome.Should().Be(PasskeyOutcome.Succeeded);
			(await _service.CompleteRegistrationAsync(caller, start.RequestId, response, null)).Outcome.Should().Be(PasskeyOutcome.ChallengeConsumed);
			_passkeys.Rows.Should().ContainSingle();
		}

		[Test]
		public async Task The_evidence_that_started_a_registration_must_still_hold_when_it_completes()
		{
			var caller = Caller();
			await Verified(caller);
			var start = await _service.BeginRegistrationAsync(caller, true, 10);
			var response = new SoftPasskeyAuthenticator().Register(start.OptionsJson, Origin(caller));

			await _evidence.RevokeForUserAsync(UserId);
			(await _service.CompleteRegistrationAsync(caller, start.RequestId, response, null)).Outcome.Should().Be(PasskeyOutcome.ReauthenticationRequired);

			(await _service.CompleteRegistrationAsync(Caller(generation: 5), start.RequestId, response, null)).Outcome
				.Should().Be(PasskeyOutcome.ChallengeExpired, "a password change or revocation since the request retires it");
			_passkeys.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_completion_minutes_later_is_judged_by_when_the_ceremony_started()
		{
			var caller = Caller();
			await FirstFactor(caller, minutesAgo: 4);
			await SecondFactor(caller, minutesAgo: 4);
			var start = await _service.BeginRegistrationAsync(caller, true, 10);
			var response = new SoftPasskeyAuthenticator().Register(start.OptionsJson, Origin(caller));

			_clock.Now = _clock.Now.AddMinutes(3);
			(await _service.CompleteRegistrationAsync(caller, start.RequestId, response, null)).Outcome.Should().Be(PasskeyOutcome.Succeeded);
		}

		[Test]
		public async Task Failed_verifications_spend_the_request()
		{
			var caller = Caller();
			await Verified(caller);
			var start = await _service.BeginRegistrationAsync(caller, true, 10);
			var wrongOrigin = new SoftPasskeyAuthenticator().Register(start.OptionsJson, "https://responder.resgrid.test");

			for (var i = 0; i < PasskeyConfig.ChallengeMaxAttempts; i++)
				(await _service.CompleteRegistrationAsync(caller, start.RequestId, wrongOrigin, null)).Outcome.Should().Be(PasskeyOutcome.VerificationFailed);

			(await _service.CompleteRegistrationAsync(caller, start.RequestId, new SoftPasskeyAuthenticator().Register(start.OptionsJson, Origin(caller)), null))
				.Outcome.Should().Be(PasskeyOutcome.TooManyAttempts);
			_passkeys.Rows.Should().BeEmpty();
		}

		[Test]
		public async Task A_credential_can_belong_to_only_one_account()
		{
			var (_, authenticator) = await Register(Caller());

			var other = Caller(userId: OtherUserId, sessionId: "session-2");
			await Verified(other);
			var start = await _service.BeginRegistrationAsync(other, true, 10);
			var cloned = new SoftPasskeyAuthenticator(credentialId: authenticator.CredentialId);
			(await _service.CompleteRegistrationAsync(other, start.RequestId, cloned.Register(start.OptionsJson, Origin(other)), null)).Outcome
				.Should().Be(PasskeyOutcome.VerificationFailed);

			_passkeys.Rows.Should().ContainSingle().Which.UserId.Should().Be(UserId);
		}

		[Test]
		public async Task Each_app_has_its_own_cap()
		{
			PasskeyConfig.MaxActiveCredentialsPerClient = 2;
			await Register(Caller());
			await Register(Caller());

			var caller = Caller();
			(await _service.BeginRegistrationAsync(caller, true, 10)).Outcome.Should().Be(PasskeyOutcome.LimitReached);
			await Register(Caller(UserSessionClientApplication.Responder));
		}

		[Test]
		public async Task The_user_handle_is_stable_per_relying_party_and_existing_credentials_are_excluded()
		{
			var (first, _) = await Register(Caller());
			var (second, _) = await Register(Caller());
			var (responder, _) = await Register(Caller(UserSessionClientApplication.Responder));

			second.UserHandle.Should().Equal(first.UserHandle);
			responder.UserHandle.Should().NotEqual(first.UserHandle);

			var start = await _service.BeginRegistrationAsync(Caller(), true, 10);
			CredentialCreateOptions.FromJson(start.OptionsJson).ExcludeCredentials.Select(c => Convert.ToBase64String(c.Id))
				.Should().BeEquivalentTo(new[] { first.CredentialId, second.CredentialId }.Select(Convert.ToBase64String),
					"only this app's passkeys are excluded");
		}

		[Test]
		public async Task Display_names_are_cleaned_and_bounded()
		{
			var (named, _) = await Register(Caller(), displayName: "  Work‮phone\t\n key\u0000 ");
			named.DisplayName.Should().Be("Workphone key");

			var caller = Caller();
			await Verified(caller);
			var start = await _service.BeginRegistrationAsync(caller, true, 10);
			(await _service.CompleteRegistrationAsync(caller, start.RequestId, new SoftPasskeyAuthenticator().Register(start.OptionsJson, Origin(caller)),
				new string('x', PasskeyConfig.MaxDisplayNameLength + 1))).Outcome.Should().Be(PasskeyOutcome.InvalidRequest);

			(await _service.RenameAsync(caller, named.UserPasskeyId, " ​ ")).Should().Be(PasskeyOutcome.InvalidRequest);
			(await _service.RenameAsync(Caller(userId: OtherUserId), named.UserPasskeyId, "Mine now")).Should().Be(PasskeyOutcome.NotFound);
			(await _service.RenameAsync(caller, named.UserPasskeyId, "Engine 7")).Should().Be(PasskeyOutcome.Succeeded);
			_passkeys.Rows.Single().DisplayName.Should().Be("Engine 7");
			Audited(SystemAuditTypes.PasskeyRenamed, Times.Once());
		}

		// ---- Step-up assertions --------------------------------------------------------------------------------------

		[Test]
		public async Task A_step_up_verifies_only_this_apps_passkeys_and_records_the_use()
		{
			var (unit, authenticator) = await Register(Caller());
			var (responder, _) = await Register(Caller(UserSessionClientApplication.Responder));

			var caller = Caller();
			var start = await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation);
			start.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			AssertionOptions.FromJson(start.OptionsJson).AllowCredentials.Select(c => Convert.ToBase64String(c.Id))
				.Should().Equal(Convert.ToBase64String(unit.CredentialId));

			_clock.Now = _clock.Now.AddMinutes(1);
			var verified = await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, start.RequestId,
				authenticator.Assert(start.OptionsJson, Origin(caller), unit.UserHandle));

			verified.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			verified.Passkey.UserPasskeyId.Should().Be(unit.UserPasskeyId);
			verified.VerifiedOnUtc.Should().Be(_clock.Now);
			var row = _passkeys.Rows.Single(r => r.UserPasskeyId == unit.UserPasskeyId);
			row.SignCount.Should().Be(1);
			row.LastUsedOnUtc.Should().Be(_clock.Now);
			row.LastUsedClientApplication.Should().Be((int)UserSessionClientApplication.Unit);
			row.LastUsedInstallation.Should().Be("Engine 7 tablet");
			_passkeys.Rows.Single(r => r.UserPasskeyId == responder.UserPasskeyId).LastUsedOnUtc.Should().BeNull();
		}

		[Test]
		public async Task An_assertion_is_single_use()
		{
			var (unit, authenticator) = await Register(Caller());
			var caller = Caller();
			var start = await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation);
			var assertion = authenticator.Assert(start.OptionsJson, Origin(caller), unit.UserHandle);

			(await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, start.RequestId, assertion))
				.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			(await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, start.RequestId, assertion))
				.Outcome.Should().Be(PasskeyOutcome.ChallengeConsumed);

			var next = await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation);
			(await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, next.RequestId, assertion))
				.Outcome.Should().Be(PasskeyOutcome.VerificationFailed, "a captured assertion answers only its own challenge");
		}

		[Test]
		public async Task Another_apps_or_another_users_passkey_cannot_complete_a_step_up()
		{
			var (unit, _) = await Register(Caller());
			var (responder, responderAuthenticator) = await Register(Caller(UserSessionClientApplication.Responder));
			var (stranger, strangerAuthenticator) = await Register(Caller(userId: OtherUserId, sessionId: "session-2"));

			var caller = Caller();
			var start = await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation);

			(await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, start.RequestId,
				responderAuthenticator.Assert(start.OptionsJson, Origin(caller), responder.UserHandle))).Outcome
				.Should().Be(PasskeyOutcome.NotRegisteredForClient, "a Responder passkey never unlocks Unit");
			(await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, start.RequestId,
				strangerAuthenticator.Assert(start.OptionsJson, Origin(caller), stranger.UserHandle))).Outcome
				.Should().Be(PasskeyOutcome.NotRegisteredForClient, "another user's passkey is never this user's factor");

			_challenges.Rows[start.RequestId].Attempts.Should().Be(2);
			_passkeys.Rows.Should().OnlyContain(r => r.LastUsedOnUtc == null);
			unit.Should().NotBeNull();
		}

		[Test]
		public async Task Step_up_needs_its_gate_and_a_passkey_for_this_app()
		{
			await Register(Caller());
			var caller = Caller();

			(await _service.BeginAssertionAsync(Caller(UserSessionClientApplication.Web), AuthenticationChallengePurpose.SensitiveOperation))
				.Outcome.Should().Be(PasskeyOutcome.NotRegisteredForClient);
			(await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.LoginSecondFactor))
				.Outcome.Should().Be(PasskeyOutcome.InvalidRequest, "a login second factor belongs to a login transaction, never a session");
			(await _service.BeginAssertionAsync(new PasskeyCaller
			{
				UserId = UserId, LoginTransactionId = "tx-1", ClientApplication = UserSessionClientApplication.Unit, AuthenticationGeneration = 4
			}, AuthenticationChallengePurpose.SensitiveOperation)).Outcome.Should().Be(PasskeyOutcome.InvalidRequest, "and a step-up never a transaction");

			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(false);
			(await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation)).Outcome.Should().Be(PasskeyOutcome.Unavailable);
			(await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.AdpStepUp)).Outcome.Should().Be(PasskeyOutcome.Unavailable);

			_gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(true);
			(await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.AdpStepUp)).Outcome.Should().Be(PasskeyOutcome.Succeeded);
		}

		// ---- Revocation ----------------------------------------------------------------------------------------------

		[Test]
		public async Task Removing_a_passkey_needs_recent_mfa_and_retires_what_it_verified()
		{
			var (unit, _) = await Register(Caller());
			var caller = Caller();
			await SecondFactor(caller, minutesAgo: 0, method: MfaEvidenceMethod.Passkey, factorReference: UserPasskey.FactorReferenceFor(unit.UserPasskeyId));
			var pending = await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation);

			_clock.Now = _clock.Now.AddMinutes(6);
			(await _service.RevokeAsync(caller, unit.UserPasskeyId)).Outcome.Should().Be(PasskeyOutcome.StepUpRequired);
			_passkeys.Rows.Single().IsActive.Should().BeTrue();

			await SecondFactor(caller, minutesAgo: 0);
			(await _service.RevokeAsync(Caller(userId: OtherUserId, sessionId: "session-2"), unit.UserPasskeyId))
				.Outcome.Should().Be(PasskeyOutcome.StepUpRequired);
			(await _service.RevokeAsync(caller, unit.UserPasskeyId)).Outcome.Should().Be(PasskeyOutcome.Succeeded);

			var row = _passkeys.Rows.Single();
			row.IsActive.Should().BeFalse();
			row.RevocationReason.Should().Be((int)PasskeyRevocationReason.RemovedByUser);
			row.RevokedByUserId.Should().Be(UserId);
			row.StateVersion.Should().Be(2);
			_evidenceRows.Rows.Where(e => e.FactorReference == UserPasskey.FactorReferenceFor(unit.UserPasskeyId)).Should().OnlyContain(e => e.RevokedOnUtc != null);
			_evidenceRows.Rows.Where(e => e.Method == (int)MfaEvidenceMethod.Totp).Should().OnlyContain(e => e.RevokedOnUtc == null);
			_challenges.Rows[pending.RequestId].State.Should().Be((int)AuthenticationChallengeState.Canceled);
			Audited(SystemAuditTypes.PasskeyRevoked, Times.Once());

			(await _service.RevokeAsync(caller, unit.UserPasskeyId)).Outcome.Should().Be(PasskeyOutcome.NotFound);
		}

		[Test]
		public async Task Removing_a_passkey_ends_the_sessions_that_signed_in_with_it()
		{
			var (unit, _) = await Register(Caller());
			var (second, _) = await Register(Caller());
			_activeSessions.AddRange(new[]
			{
				new UserSession { UserSessionId = SessionId, UserId = UserId, LoginMfaFactorReference = UserPasskey.FactorReferenceFor(unit.UserPasskeyId) },
				new UserSession { UserSessionId = "tablet", UserId = UserId, LoginMfaFactorReference = UserPasskey.FactorReferenceFor(unit.UserPasskeyId) },
				new UserSession { UserSessionId = "phone", UserId = UserId, LoginMfaFactorReference = UserPasskey.FactorReferenceFor(second.UserPasskeyId) },
				new UserSession { UserSessionId = "desk", UserId = UserId, LoginMfaMethod = (int)MfaEvidenceMethod.Totp }
			});
			var caller = Caller();
			await SecondFactor(caller);

			var removed = await _service.RevokeAsync(caller, unit.UserPasskeyId);

			removed.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			removed.SessionsEnded.Should().Be(2);
			removed.CurrentSessionEnded.Should().BeTrue("the caller signed in with that passkey and must sign in again");
			_activeSessions.Select(s => s.UserSessionId).Should().BeEquivalentTo(new[] { "phone", "desk" });

			var desk = Caller(sessionId: "desk");
			await SecondFactor(desk);
			var all = await _service.RevokeAllForClientAsync(desk, UserSessionClientApplication.Unit);
			all.SessionsEnded.Should().Be(1);
			all.CurrentSessionEnded.Should().BeFalse();
			_activeSessions.Select(s => s.UserSessionId).Should().Equal("desk");
			_userSessions.Verify(s => s.RevokeSessionAsync(UserId, UserId, "desk", It.IsAny<UserSessionRevocationReason>(), It.IsAny<CancellationToken>()),
				Times.Never, "a session that signed in with TOTP is untouched");
		}

		[Test]
		public async Task Another_users_passkey_is_not_found()
		{
			var (stranger, _) = await Register(Caller(userId: OtherUserId, sessionId: "session-2"));
			var caller = Caller();
			await SecondFactor(caller);

			(await _service.RevokeAsync(caller, stranger.UserPasskeyId)).Outcome.Should().Be(PasskeyOutcome.NotFound);
			(await _service.SetApprovalEnabledAsync(caller, stranger.UserPasskeyId, false)).Should().Be(PasskeyOutcome.NotFound);
			_passkeys.Rows.Single().IsActive.Should().BeTrue();
		}

		[Test]
		public async Task A_passkey_made_in_one_app_can_be_removed_from_another()
		{
			var (responder, _) = await Register(Caller(UserSessionClientApplication.Responder));

			var web = Caller(UserSessionClientApplication.Web, sessionId: "web-session");
			await SecondFactor(web);
			(await _service.RevokeAsync(web, responder.UserPasskeyId)).Outcome.Should().Be(PasskeyOutcome.Succeeded);
		}

		[Test]
		public async Task Removing_every_passkey_for_one_app_leaves_the_others()
		{
			await Register(Caller());
			await Register(Caller());
			var (responder, _) = await Register(Caller(UserSessionClientApplication.Responder));
			var caller = Caller();

			var removed = await _service.RevokeAllForClientAsync(caller, UserSessionClientApplication.Unit);

			removed.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			removed.Revoked.Should().Be(2);
			(await _service.GetActiveForUserAsync(UserId)).Should().ContainSingle().Which.UserPasskeyId.Should().Be(responder.UserPasskeyId);
			(await _service.HasActiveForClientAsync(UserId, UserSessionClientApplication.Unit)).Should().BeFalse();
			(await _service.HasActiveForClientAsync(UserId, UserSessionClientApplication.Responder)).Should().BeTrue();
		}

		[Test]
		public async Task A_step_up_started_before_a_removal_cannot_finish_after_it()
		{
			var (unit, authenticator) = await Register(Caller());
			var caller = Caller();
			var start = await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation);
			var assertion = authenticator.Assert(start.OptionsJson, Origin(caller), unit.UserHandle);

			await SecondFactor(caller);
			(await _service.RevokeAsync(caller, unit.UserPasskeyId)).Outcome.Should().Be(PasskeyOutcome.Succeeded);

			(await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, start.RequestId, assertion))
				.Succeeded.Should().BeFalse();
			_passkeys.Rows.Single().LastUsedOnUtc.Should().BeNull();
		}

		private void Announced(SecurityNoticeKind kind, UserSessionClientApplication client, Times times) =>
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.UserId == UserId && r.Kind == kind && r.ClientApplication == client &&
				r.InstallationLabel == "Engine 7 tablet"), It.IsAny<CancellationToken>()), times);

		[Test]
		public async Task Passkey_changes_are_announced_to_the_account_holder()
		{
			var caller = Caller();
			var (unit, _) = await Register(caller);
			Announced(SecurityNoticeKind.PasskeyRegistered, UserSessionClientApplication.Unit, Times.Once());

			var responderCaller = Caller(UserSessionClientApplication.Responder);
			var (responder, _) = await Register(responderCaller);
			_gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			_clock.Now = _clock.Now.AddSeconds(10);
			await SecondFactor(responderCaller, factorReference: UserPasskey.FactorReferenceFor(responder.UserPasskeyId), method: MfaEvidenceMethod.Passkey);
			(await _service.SetApprovalEnabledAsync(responderCaller, responder.UserPasskeyId, true)).Should().Be(PasskeyOutcome.Succeeded);
			(await _service.SetApprovalEnabledAsync(responderCaller, responder.UserPasskeyId, false)).Should().Be(PasskeyOutcome.Succeeded);
			Announced(SecurityNoticeKind.ApprovalTurnedOn, UserSessionClientApplication.Responder, Times.Once());
			Announced(SecurityNoticeKind.ApprovalTurnedOff, UserSessionClientApplication.Responder, Times.Once());

			_clock.Now = _clock.Now.AddSeconds(10);
			await SecondFactor(caller);
			(await _service.RevokeAsync(caller, unit.UserPasskeyId)).Outcome.Should().Be(PasskeyOutcome.Succeeded);
			(await _service.RevokeAllForClientAsync(caller, UserSessionClientApplication.Responder)).Outcome.Should().Be(PasskeyOutcome.Succeeded);
			Announced(SecurityNoticeKind.PasskeyRemoved, UserSessionClientApplication.Unit, Times.Exactly(2));
		}

		// ---- Shared installations (plan sections 6.5 and 12.5) --------------------------------------------------------

		private static PasskeyCaller SharedCaller(long lockVersion = 2) => new()
		{
			UserId = UserId, UserName = UserId + "@example.test", SessionId = SessionId, ClientApplication = UserSessionClientApplication.Unit,
			AuthenticationGeneration = 4, DepartmentId = DepartmentId, SessionLockVersion = lockVersion, SharedMode = true, AuditSystem = SystemAuditSystems.Api
		};

		[Test]
		public async Task A_shared_session_unlocks_with_a_passkey_only_at_the_lock_version_it_asked_at()
		{
			var (unit, authenticator) = await Register(Caller());
			var caller = SharedCaller();

			var start = await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SharedDeviceUnlock);
			start.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			var assertion = authenticator.Assert(start.OptionsJson, Origin(caller), unit.UserHandle);

			(await _service.CompleteAssertionAsync(SharedCaller(lockVersion: 3), AuthenticationChallengePurpose.SharedDeviceUnlock, start.RequestId, assertion))
				.Succeeded.Should().BeFalse("the session locked again after the options were issued");
			(await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation, start.RequestId, assertion))
				.Succeeded.Should().BeFalse("an unlock challenge answers nothing else");

			var verified = await _service.CompleteAssertionAsync(caller, AuthenticationChallengePurpose.SharedDeviceUnlock, start.RequestId, assertion);
			verified.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			_passkeys.Rows.Single(r => r.UserPasskeyId == unit.UserPasskeyId).LastUsedInSharedMode.Should().BeTrue();

			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(false);
			(await _service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SharedDeviceUnlock)).Outcome
				.Should().Be(PasskeyOutcome.Unavailable, "passkey unlock follows the sign-in passkey gate");
		}

		[Test]
		public async Task A_passkey_registered_in_a_shared_session_asks_for_a_roaming_key_and_warns_the_owner()
		{
			var caller = SharedCaller();
			await Verified(caller);
			var start = await _service.BeginRegistrationAsync(caller, true, 10);
			start.OptionsJson.Should().Contain("cross-platform", "a shared profile should not hold a personal passkey");
			var done = await _service.CompleteRegistrationAsync(caller, start.RequestId, new SoftPasskeyAuthenticator().Register(start.OptionsJson, Origin(caller)), null);
			done.Outcome.Should().Be(PasskeyOutcome.Succeeded);
			done.Passkey.RegisteredInSharedMode.Should().BeTrue();
			Announced(SecurityNoticeKind.PasskeyRegistered, UserSessionClientApplication.Unit, Times.Once());
			Announced(SecurityNoticeKind.SharedInstallationFactor, UserSessionClientApplication.Unit, Times.Once());

			await Register(Caller(UserSessionClientApplication.Responder));
			_notices.Verify(n => n.QueueAsync(It.Is<SecurityNoticeRequest>(r => r.Kind == SecurityNoticeKind.SharedInstallationFactor), It.IsAny<CancellationToken>()),
				Times.Once(), "a personal registration carries no shared warning");
		}

		[Test]
		public async Task Only_a_responder_passkey_can_be_allowed_to_approve()
		{
			var (unit, _) = await Register(Caller());
			var (responder, _) = await Register(Caller(UserSessionClientApplication.Responder));
			var caller = Caller(UserSessionClientApplication.Responder);
			await SecondFactor(caller);

			(await _service.SetApprovalEnabledAsync(caller, unit.UserPasskeyId, true)).Should().Be(PasskeyOutcome.Unavailable);
			_gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			(await _service.SetApprovalEnabledAsync(caller, responder.UserPasskeyId, true)).Should().Be(PasskeyOutcome.StepUpRequired,
				"turning approval on needs a fresh assertion with that passkey, not just an authenticator code");
			_clock.Now = _clock.Now.AddSeconds(10);
			await SecondFactor(caller, factorReference: UserPasskey.FactorReferenceFor("another-passkey"), method: MfaEvidenceMethod.Passkey);
			(await _service.SetApprovalEnabledAsync(caller, responder.UserPasskeyId, true)).Should().Be(PasskeyOutcome.StepUpRequired,
				"an assertion with another passkey does not prove this one");
			_clock.Now = _clock.Now.AddSeconds(10);
			await SecondFactor(caller, factorReference: UserPasskey.FactorReferenceFor(unit.UserPasskeyId), method: MfaEvidenceMethod.Passkey);
			(await _service.SetApprovalEnabledAsync(caller, unit.UserPasskeyId, true)).Should().Be(PasskeyOutcome.InvalidRequest);
			_clock.Now = _clock.Now.AddSeconds(10);
			await SecondFactor(caller, factorReference: UserPasskey.FactorReferenceFor(responder.UserPasskeyId), method: MfaEvidenceMethod.Passkey);
			(await _service.SetApprovalEnabledAsync(caller, responder.UserPasskeyId, true)).Should().Be(PasskeyOutcome.Succeeded);
			_passkeys.Rows.Single(r => r.UserPasskeyId == responder.UserPasskeyId).ApprovalEnabled.Should().BeTrue();

			_gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(false);
			_clock.Now = _clock.Now.AddSeconds(10);
			await SecondFactor(caller);
			(await _service.SetApprovalEnabledAsync(caller, responder.UserPasskeyId, false)).Should().Be(PasskeyOutcome.Succeeded,
				"stopping approvals is always allowed, with any accepted factor");
			Audited(SystemAuditTypes.PasskeyApprovalChanged, Times.Exactly(2));
		}

		[Test]
		public async Task A_store_fault_refuses_the_ceremony()
		{
			var failing = new Mock<IUserPasskeyRepository>();
			failing.Setup(r => r.GetActiveForUserAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
			var service = Service(failing.Object);
			var caller = Caller();
			await Verified(caller);

			(await service.BeginRegistrationAsync(caller, true, 10)).Outcome.Should().Be(PasskeyOutcome.ServiceUnavailable);
			(await service.BeginAssertionAsync(caller, AuthenticationChallengePurpose.SensitiveOperation)).Outcome.Should().Be(PasskeyOutcome.ServiceUnavailable);
		}
	}
}
