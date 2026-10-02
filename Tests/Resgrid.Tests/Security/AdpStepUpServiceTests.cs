using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey workbook section 12, slice 18 (Phase 2): one Protected Data Grant issuer for every method. A passkey,
	/// Responder approval or provider step-up grant is version 2, bound to the session and to the credential behind it, whose
	/// revocation voids the grant at its next use; every grant expires from its verification, never past the session; and
	/// missing signing material is an unavailable operation, never a token-less success.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class AdpStepUpServiceTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "unit-session";
		private const string ConfigId = "sso-config-1";

		private sealed class Clock : TimeProvider
		{
			public DateTime Now = DateTime.UtcNow;
			public override DateTimeOffset GetUtcNow() => new(Now);
		}

		private Clock _clock;
		private ECDsa _key;
		private X509Certificate2 _certificate;
		private ProtectedDataGrantService _grants;
		private DepartmentSecurityPolicy _securityPolicy;
		private DepartmentDataProtectionPolicy _adpPolicy;
		private AdpStepUpDecision _decision;
		private Mock<IPasskeyFeatureGates> _gates;
		private InMemoryUserPasskeyRepository _passkeyRows;
		private InMemoryUserSessionsRepository _sessionRows;
		private InMemoryMfaEvidenceRepository _evidenceRows;
		private Mock<IPasskeyService> _passkeys;
		private Mock<IMfaApprovalService> _approvals;
		private Mock<ISsoBrokerService> _broker;
		private Mock<IDepartmentSsoService> _departmentSso;
		private Mock<IDepartmentSsoConfigRepository> _ssoConfigs;
		private DepartmentSsoConfig _config;
		private Mock<IMfaActivityService> _activity;
		private List<AdpAuditEvent> _audited;
		private MfaCredentialStateService _credentials;

		[SetUp]
		public void SetUp()
		{
			_clock = new Clock();
			_key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			_certificate = new CertificateRequest("CN=adp-issuer", _key, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
			_grants = new ProtectedDataGrantService(() => _certificate, () => _certificate);
			_securityPolicy = new DepartmentSecurityPolicy { DepartmentId = DepartmentId, AllowFederatedMfaForAdp = true };
			_adpPolicy = new DepartmentDataProtectionPolicy { DepartmentId = DepartmentId, PolicyEpoch = 3, StepUpWindowMinutes = 15 };
			_decision = new AdpStepUpDecision { StepUpRequired = true, PolicyEpoch = 3, StepUpWindowMinutes = 15 };
			_gates = new Mock<IPasskeyFeatureGates>();
			_gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(true);
			_gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(true);
			_gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(true);
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			_gates.SetupGet(g => g.EmitGrantV2).Returns(true);
			_passkeyRows = new InMemoryUserPasskeyRepository();
			_sessionRows = new InMemoryUserSessionsRepository();
			_evidenceRows = new InMemoryMfaEvidenceRepository();
			_passkeys = new Mock<IPasskeyService>();
			_approvals = new Mock<IMfaApprovalService>();
			_broker = new Mock<ISsoBrokerService>();
			_departmentSso = new Mock<IDepartmentSsoService>();
			_departmentSso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _securityPolicy);
			_config = new DepartmentSsoConfig
			{
				DepartmentSsoConfigId = ConfigId, DepartmentId = DepartmentId, IsEnabled = true, FederatedMfaMappingJson = "{}",
				FederatedMfaMappingVersion = 3, FederatedMfaTestedVersion = 3
			};
			_departmentSso.Setup(s => s.GetTestedFederatedMfaConfigAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(() => FederatedMfaMapping.IsTested(_config) ? _config : null);
			_ssoConfigs = new Mock<IDepartmentSsoConfigRepository>();
			_ssoConfigs.Setup(r => r.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(() => new[] { _config });
			_activity = new Mock<IMfaActivityService>();
			_audited = new List<AdpAuditEvent>();
			_credentials = new MfaCredentialStateService(_passkeyRows, _sessionRows, _ssoConfigs.Object, _clock);
		}

		[TearDown]
		public void TearDown()
		{
			_certificate?.Dispose();
			_key?.Dispose();
		}

		private AdpStepUpService Service(ProtectedDataGrantService grants = null)
		{
			var protection = new Mock<IDepartmentDataProtectionService>();
			protection.Setup(p => p.GetPolicyByDepartmentIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => _adpPolicy);
			protection.Setup(p => p.GetStepUpDecisionForClientAsync(DepartmentId, It.IsAny<UserSessionClientApplication>(), It.IsAny<bool>()))
				.ReturnsAsync(() => _decision);
			var audit = new Mock<IAdpAuditRepository>();
			audit.Setup(a => a.AppendAsync(It.IsAny<AdpAuditEvent>(), It.IsAny<CancellationToken>()))
				.Callback((AdpAuditEvent e, CancellationToken _) => _audited.Add(e)).Returns(Task.CompletedTask);
			var policy = new MfaPolicyService(_departmentSso.Object, new InMemoryUserMfaStateRepository(), _gates.Object);
			var evidence = new MfaEvidenceService(_evidenceRows, new InMemoryUserMfaStateRepository(), _passkeyRows, _sessionRows,
				new InMemoryMfaActivityRepository(), _clock);
			return new AdpStepUpService(grants ?? _grants, protection.Object, policy, evidence, _passkeys.Object, _approvals.Object, _broker.Object,
				_departmentSso.Object, _credentials, _activity.Object, audit.Object, _gates.Object, _clock);
		}

		private ProtectedGrantSessionContext Session(DateTime? expiresOn = null, long? lockVersion = null) => new()
		{
			SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4,
			SessionLockVersion = lockVersion, SharedMode = lockVersion != null, SessionExpiresOnUtc = expiresOn ?? _clock.Now.AddHours(8)
		};

		private AdpStepUpCaller Caller(ProtectedGrantSessionContext session = null, bool untracked = false) => new()
		{
			UserId = UserId, UserName = "user1", DepartmentId = DepartmentId, Session = untracked ? null : session ?? Session(),
			LegacySessionId = untracked ? "legacy-sid" : null, ClientApplication = UserSessionClientApplication.Unit, AccountAuthenticationGeneration = 4,
			AuditSystem = SystemAuditSystems.Api
		};

		private ProtectedDataGrant Read(AdpGrantIssue issued)
		{
			issued.Succeeded.Should().BeTrue(issued.ErrorCode);
			_grants.ValidateGrant(issued.Token, DepartmentId, 3, ProtectedDataGrantScopes.Read, out var grant).Should().Be(ProtectedDataGrantValidationOutcome.Valid);
			return grant;
		}

		private static DateTime Seconds(DateTime utc) => utc.AddTicks(-(utc.Ticks % TimeSpan.TicksPerSecond));

		private UserPasskey Passkey(string id = "pk-unit", UserSessionClientApplication client = UserSessionClientApplication.Unit, bool approval = false)
		{
			var passkey = new UserPasskey
			{
				UserPasskeyId = id, UserId = UserId, ClientApplication = (int)client, RpId = "unit.example", CredentialId = new byte[] { 1 },
				CredentialIdHash = new byte[] { 2 }, PublicKey = new byte[] { 3 }, UserHandle = new byte[] { 4 }, DisplayName = id, ApprovalEnabled = approval,
				CreatedOnUtc = _clock.Now.AddDays(-1), StateVersion = 2
			};
			_passkeyRows.Rows.Add(passkey);
			return passkey;
		}

		private void AssertionSucceeds(UserPasskey passkey, DateTime verifiedOn) =>
			_passkeys.Setup(p => p.CompleteAssertionAsync(It.Is<PasskeyCaller>(c => c.SessionId == SessionId && c.ClientApplication == UserSessionClientApplication.Unit &&
					c.DepartmentId == DepartmentId), AuthenticationChallengePurpose.AdpStepUp, "req-1", "{}", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyAssertionResult { Outcome = PasskeyOutcome.Succeeded, Passkey = passkey, VerifiedOnUtc = verifiedOn });

		// ---- Passkey ---------------------------------------------------------------------------------------------------

		[Test]
		public async Task A_passkey_grant_is_bound_to_the_session_and_its_passkey_and_is_void_once_the_passkey_is_removed()
		{
			var passkey = Passkey();
			var verifiedOn = _clock.Now.AddMinutes(-5);
			AssertionSucceeds(passkey, verifiedOn);

			var issued = await Service().CompletePasskeyAsync(Caller(), "req-1", "{}");

			var grant = Read(issued);
			grant.Version.Should().Be(2);
			grant.SessionId.Should().Be(SessionId);
			grant.ClientApp.Should().Be((int)UserSessionClientApplication.Unit);
			grant.MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.Passkey);
			grant.MfaCredentialId.Should().Be("passkey:pk-unit");
			grant.MfaStateVersion.Should().Be(2);
			grant.MfaAtUtc.Should().Be(Seconds(verifiedOn), "mfa_at is the verification, not issuance");
			issued.ExpiresOnUtc.Should().BeCloseTo(verifiedOn.AddMinutes(15), TimeSpan.FromSeconds(1), "the window runs from the verification");
			issued.WindowMinutes.Should().Be(15);

			var evidence = _evidenceRows.Rows.Should().ContainSingle().Subject;
			evidence.Method.Should().Be((int)MfaEvidenceMethod.Passkey);
			evidence.Purpose.Should().Be((int)MfaEvidencePurpose.AdpStepUp);
			evidence.DepartmentId.Should().Be(DepartmentId);
			evidence.FactorReference.Should().Be("passkey:pk-unit");
			_audited.Should().Contain(e => e.Operation == "grant-issued" && e.Outcome == "mfa-verified" && e.ResourceId == "passkey" && e.PolicyEpoch == 3);

			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, _credentials)).Should().Be(ProtectedGrantBindingOutcome.Bound);
			ProtectedGrantBinding.Check(grant, UserId, Session(), 15).Should().Be(ProtectedGrantBindingOutcome.MethodUnverifiable,
				"a reader that cannot check the passkey refuses its grant");
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, null)).Should().Be(ProtectedGrantBindingOutcome.MethodUnverifiable);

			await _passkeyRows.TryRevokeAsync("pk-unit", UserId, PasskeyRevocationReason.RemovedByUser, UserId, _clock.Now);
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, _credentials)).Should().Be(ProtectedGrantBindingOutcome.MethodUnverifiable,
				"removing the passkey voids its grant at the next protected read");
		}

		[Test]
		public async Task A_passkey_for_another_app_or_a_changed_passkey_does_not_count()
		{
			var passkey = Passkey(client: UserSessionClientApplication.Web);
			AssertionSucceeds(passkey, _clock.Now);
			(await Service().CompletePasskeyAsync(Caller(), "req-1", "{}")).Outcome.Should().Be(AdpGrantOutcome.CredentialRevoked,
				"a passkey is only ever for the app it is bound to");

			_passkeyRows.Rows.Clear();
			var unit = Passkey();
			AssertionSucceeds(unit, _clock.Now);
			var grant = Read(await Service().CompletePasskeyAsync(Caller(), "req-1", "{}"));
			unit.StateVersion++;
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, _credentials)).Should().Be(ProtectedGrantBindingOutcome.MethodUnverifiable,
				"a changed passkey (a new state version) voids grants from before the change");

			var failing = new Mock<IMfaCredentialStateService>();
			failing.Setup(f => f.IsCurrentAsync(It.IsAny<ProtectedDataGrant>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, failing.Object)).Should().Be(ProtectedGrantBindingOutcome.MethodUnverifiable,
				"a revocation that cannot be checked is not proof that none happened");
		}

		[Test]
		public async Task A_failed_passkey_is_denied_activity_and_issues_nothing()
		{
			_passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.AdpStepUp, It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>())).ReturnsAsync(PasskeyAssertionResult.Of(PasskeyOutcome.VerificationFailed));

			var issued = await Service().CompletePasskeyAsync(Caller(), "req-1", "{}");

			issued.Outcome.Should().Be(AdpGrantOutcome.VerificationFailed);
			issued.ErrorCode.Should().Be(PasskeyOutcomes.ErrorCode(PasskeyOutcome.VerificationFailed));
			issued.Token.Should().BeNull();
			_evidenceRows.Rows.Should().BeEmpty();
			_activity.Verify(a => a.RecordAsync(It.Is<MfaActivityEntry>(e => !e.Successful && e.Method == MfaEvidenceMethod.Passkey &&
				e.Purpose == MfaEvidencePurpose.AdpStepUp && e.DepartmentId == DepartmentId && e.SessionId == SessionId), It.IsAny<CancellationToken>()), Times.Once);
			_audited.Should().Contain(e => e.Operation == "mfa-verify" && e.Outcome == "denied");
		}

		// ---- Expiry (plan section 9.2) --------------------------------------------------------------------------------

		[Test]
		public async Task A_grant_expires_from_its_verification_and_never_after_the_session_ends()
		{
			var fiveLeft = await Service().IssueForTotpAsync(Caller(), _clock.Now.AddMinutes(-10));
			fiveLeft.ExpiresOnUtc.Should().BeCloseTo(_clock.Now.AddMinutes(5), TimeSpan.FromSeconds(1), "10:00 + 15 minutes, asked at 10:10");

			var sessionEnd = _clock.Now.AddMinutes(2);
			(await Service().IssueForTotpAsync(Caller(Session(sessionEnd)), _clock.Now)).ExpiresOnUtc
				.Should().BeCloseTo(sessionEnd, TimeSpan.FromSeconds(1), "no grant outlives its session or shift");

			(await Service().IssueForTotpAsync(Caller(), _clock.Now.AddMinutes(-16))).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired);
			(await Service().IssueForTotpAsync(Caller(), _clock.Now.AddMinutes(5))).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"a verification in the future is not believed");
			(await Service().IssueForTotpAsync(Caller(Session(_clock.Now.AddSeconds(-1))), _clock.Now)).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"an ended session gets nothing");

			// A version 1 grant has no verification check at signing, so the issuer's own checks are all that stand here.
			(await Service().IssueForTotpAsync(Caller(untracked: true), _clock.Now.AddMinutes(5))).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"a future verification is refused for a version 1 grant too");
			(await Service().IssueForTotpAsync(Caller(untracked: true), _clock.Now.AddMinutes(-16))).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"and so is one older than the window");
		}

		[Test]
		public async Task Without_signing_material_nothing_succeeds_but_a_verified_code_is_still_evidence()
		{
			var service = Service(new ProtectedDataGrantService(() => null, () => null));

			var totp = await service.IssueForTotpAsync(Caller(), _clock.Now);
			totp.Outcome.Should().Be(AdpGrantOutcome.NotConfigured);
			totp.ErrorCode.Should().Be("grants_not_configured");
			AdpGrantOutcomes.StatusFor(totp.Outcome).Should().Be(503);
			_evidenceRows.Rows.Should().ContainSingle("the code did verify").Which.Purpose.Should().Be((int)MfaEvidencePurpose.AdpStepUp);

			(await service.CompletePasskeyAsync(Caller(), "req-1", "{}")).Outcome.Should().Be(AdpGrantOutcome.NotConfigured);
			_passkeys.Verify(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), It.IsAny<AuthenticationChallengePurpose>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "no ceremony is spent on an operation that cannot finish");

			_decision.StepUpRequired = false;
			(await service.IssueExemptAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.NotConfigured);
		}

		[Test]
		public void The_signer_never_issues_past_the_bound_session_and_the_session_context_carries_its_end()
		{
			var end = _clock.Now.AddMinutes(4);
			ProtectedGrantSessionContext.From(new UserSession { UserSessionId = "s", ExpiresOn = end }, null).SessionExpiresOnUtc.Should().Be(end);

			ProtectedDataGrantIssueRequest Request(DateTime? notAfter) => ProtectedGrantIssueRequests.ForSession(new ProtectedDataGrantIssueRequest
			{
				UserId = UserId, DepartmentId = DepartmentId, ClientApp = (int)UserSessionClientApplication.Unit, PolicyEpoch = 3, WindowMinutes = 15,
				Scopes = new[] { ProtectedDataGrantScopes.Read }, MfaAtUtc = _clock.Now, NotAfterUtc = notAfter
			}, Session(), ProtectedDataGrantMfaMethods.Totp, emitVersionTwo: true);

			_grants.IssueGrant(Request(end)).ExpiresOnUtc.Should().BeCloseTo(end, TimeSpan.FromSeconds(1));
			_grants.IssueGrant(Request(_clock.Now.AddHours(1))).ExpiresOnUtc.Should().BeCloseTo(_clock.Now.AddMinutes(15), TimeSpan.FromSeconds(1));
			_grants.Invoking(g => g.IssueGrant(Request(_clock.Now.AddSeconds(-1)))).Should().Throw<ArgumentException>("an ended session gets no grant");
		}

		[Test]
		public async Task Only_the_callers_own_current_credential_resolves()
		{
			Passkey();
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.Passkey, "passkey:pk-unit", 4))
				.StateVersion.Should().Be(2);
			(await _credentials.ResolveAsync("someone-else", DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.Passkey, "passkey:pk-unit", 4))
				.Should().BeNull("another account's passkey");
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.Passkey, "pk-unit", 4)).Should().BeNull();
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.Totp, "passkey:pk-unit", 4))
				.Should().BeNull("an authenticator code names no credential");
			await _passkeyRows.TryRevokeAsync("pk-unit", UserId, PasskeyRevocationReason.RemovedByUser, UserId, _clock.Now);
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.Passkey, "passkey:pk-unit", 4))
				.Should().BeNull("a removed passkey resolves to nothing, whatever its version");

			var tested = "federated:" + ConfigId + ":3";
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.Federated, tested, 4))
				.StateVersion.Should().Be(3);
			foreach (var (reference, department, why) in new[]
			{
				("federated:" + ConfigId + ":2", DepartmentId, "an older mapping version"),
				("federated:other-config:3", DepartmentId, "another configuration"),
				(tested, 7, "another department")
			})
				(await _credentials.ResolveAsync(UserId, department, UserSessionClientApplication.Unit, MfaEvidenceMethod.Federated, reference, 4))
					.Should().BeNull(why);
			_config.FederatedMfaTestedVersion = 2;
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.Federated, tested, 4))
				.Should().BeNull("an untested mapping counts for nothing");

			ResponderInstallation();
			const string approval = "approval:pk-responder:responder-1";
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.PasskeyApproval, approval, 4))
				.StateVersion.Should().Be(2);
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.PasskeyApproval, approval, 3))
				.Should().BeNull("an approval from before a password change");
			await _passkeyRows.TrySetApprovalEnabledAsync("pk-responder", UserId, false);
			(await _credentials.ResolveAsync(UserId, DepartmentId, UserSessionClientApplication.Unit, MfaEvidenceMethod.PasskeyApproval, approval, 4))
				.Should().BeNull("a passkey that no longer approves");
		}

		// ---- Where each method is allowed --------------------------------------------------------------------------

		[Test]
		public async Task Methods_other_than_totp_need_a_tracked_session_version_two_and_the_departments_adp_switches()
		{
			AssertionSucceeds(Passkey(), _clock.Now);

			(await Service().CompletePasskeyAsync(Caller(untracked: true), "req-1", "{}")).Outcome.Should().Be(AdpGrantOutcome.SessionRequired);
			var legacy = Read(await Service().IssueForTotpAsync(Caller(untracked: true), _clock.Now));
			legacy.Version.Should().Be(1, "an untracked credential keeps its version 1 authenticator-code grant");
			legacy.SessionId.Should().Be("legacy-sid");

			_gates.SetupGet(g => g.EmitGrantV2).Returns(false);
			(await Service().CompletePasskeyAsync(Caller(), "req-1", "{}")).Outcome.Should().Be(AdpGrantOutcome.MethodNotAllowed,
				"a passkey grant names its credential, which only version 2 carries");
			Read(await Service().IssueForTotpAsync(Caller(), _clock.Now)).Version.Should().Be(1, "until EmitGrantV2, a code still gets version 1");
			(await Service().BeginPasskeyAsync(Caller())).Outcome.Should().Be(PasskeyOutcome.Unavailable);

			_gates.SetupGet(g => g.EmitGrantV2).Returns(true);
			_securityPolicy.AllowPasskeysForAdp = false;
			(await Service().CompletePasskeyAsync(Caller(), "req-1", "{}")).Outcome.Should().Be(AdpGrantOutcome.MethodNotAllowed);
			(await Service().RequestApprovalAsync(Caller())).Outcome.Should().Be(MfaApprovalOutcome.Unavailable,
				"approval follows the passkey switch for protected data");
			Read(await Service().IssueForTotpAsync(Caller(), _clock.Now)).MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.Totp,
				"an authenticator code is always accepted");

			_securityPolicy.AllowPasskeysForAdp = true;
			_securityPolicy.AllowFederatedMfaForAdp = false;
			(await Service().CompleteFederatedAsync(Caller(), "sso-1", "code", "verifier")).Outcome.Should().Be(AdpGrantOutcome.MethodNotAllowed);
			_broker.Verify(b => b.RedeemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(),
				It.IsAny<CancellationToken>(), It.IsAny<SsoTransactionPurpose[]>()), Times.Never);
			_passkeys.Verify(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), It.IsAny<AuthenticationChallengePurpose>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never, "nothing is verified for a refused method");
		}

		[Test]
		public async Task The_method_choice_lists_only_what_the_caller_has_and_the_department_accepts()
		{
			_passkeys.Setup(p => p.HasActiveForClientAsync(UserId, UserSessionClientApplication.Unit, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_approvals.Setup(a => a.IsAvailableAsync(UserId, UserSessionClientApplication.Unit, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_departmentSso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var all = await Service().GetMethodChoiceAsync(Caller(), totpEnrolled: true);
			all.EnrolledMethods.Should().Equal(MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval, MfaMethodNames.Federated);
			all.AllowedMethods.Should().BeEquivalentTo(new[] { MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval, MfaMethodNames.Federated });

			(await Service().GetMethodChoiceAsync(Caller(untracked: true), totpEnrolled: true)).EnrolledMethods.Should().Equal(new[] { MfaMethodNames.Totp },
				"without a tracked session only a code can mint a grant");

			_securityPolicy.AllowPasskeysForAdp = false;
			_securityPolicy.AllowFederatedMfaForAdp = false;
			(await Service().GetMethodChoiceAsync(Caller(), totpEnrolled: true)).AllowedMethods.Should().Equal(new[] { MfaMethodNames.Totp });
		}

		// ---- Responder approval ---------------------------------------------------------------------------------------

		private void ResponderApproves(DateTime decidedOn, MfaApprovalPurpose purpose = MfaApprovalPurpose.Adp, int? department = DepartmentId)
		{
			var request = new MfaApprovalRequest
			{
				MfaApprovalRequestId = "approval-1", UserId = UserId, RequesterKind = (int)MfaApprovalRequesterKind.Session, RequesterId = SessionId,
				Purpose = (int)purpose, DepartmentId = department, AuthenticationGeneration = 4, State = (int)MfaApprovalRequestState.Approved,
				DecidedOnUtc = decidedOn, ApproverPasskeyId = "pk-responder", ApproverSessionId = "responder-1"
			};
			_approvals.Setup(a => a.GetForRequesterAsync("approval-1", MfaApprovalRequesterKind.Session, SessionId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, request));
			_approvals.Setup(a => a.ConsumeAsync("approval-1", MfaApprovalRequesterKind.Session, SessionId, UserId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Succeeded, request));
		}

		private void ResponderInstallation()
		{
			Passkey("pk-responder", UserSessionClientApplication.Responder, approval: true);
			_sessionRows.Add(new UserSession
			{
				UserSessionId = "responder-1", UserId = UserId, ClientApplication = (int)UserSessionClientApplication.Responder, State = (int)UserSessionState.Active,
				ExpiresOn = _clock.Now.AddDays(1), AuthenticationGeneration = 4
			});
		}

		[Test]
		public async Task An_approval_grant_names_the_approving_passkey_and_responder_and_dies_with_either()
		{
			ResponderInstallation();
			var decidedOn = _clock.Now.AddSeconds(-20);
			ResponderApproves(decidedOn);

			var grant = Read(await Service().CompleteApprovalAsync(Caller(), "approval-1"));

			grant.MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.PasskeyApproval);
			grant.MfaCredentialId.Should().Be("approval:pk-responder:responder-1");
			grant.MfaAtUtc.Should().Be(Seconds(decidedOn), "the approval's decision is the verification");
			_evidenceRows.Rows.Single().FactorReference.Should().Be("approval:pk-responder:responder-1");
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, _credentials)).Should().Be(ProtectedGrantBindingOutcome.Bound);

			await _sessionRows.DisableApprovalsAsync(UserId, "responder-1", _clock.Now, CancellationToken.None);
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, _credentials)).Should().Be(ProtectedGrantBindingOutcome.MethodUnverifiable,
				"a stopped installation's approvals stop counting");
		}

		[Test]
		public async Task An_approval_counts_only_for_this_sessions_protected_data_in_this_department_and_once_decided()
		{
			ResponderInstallation();
			ResponderApproves(_clock.Now, MfaApprovalPurpose.StepUp);
			(await Service().CompleteApprovalAsync(Caller(), "approval-1")).Outcome.Should().Be(AdpGrantOutcome.ApprovalUnavailable,
				"an approval for another operation");

			ResponderApproves(_clock.Now, department: 7);
			(await Service().CompleteApprovalAsync(Caller(), "approval-1")).Outcome.Should().Be(AdpGrantOutcome.ApprovalUnavailable, "another department's data");
			_approvals.Verify(a => a.ConsumeAsync(It.IsAny<string>(), It.IsAny<MfaApprovalRequesterKind>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never, "nothing is spent on a mismatch");

			ResponderApproves(_clock.Now);
			_approvals.Setup(a => a.ConsumeAsync("approval-1", MfaApprovalRequesterKind.Session, SessionId, UserId, 4, It.IsAny<CancellationToken>()))
				.ReturnsAsync(MfaApprovalResult.Of(MfaApprovalOutcome.Pending));
			var pending = await Service().CompleteApprovalAsync(Caller(), "approval-1");
			pending.Outcome.Should().Be(AdpGrantOutcome.ApprovalPending);
			AdpGrantOutcomes.StatusFor(pending.Outcome).Should().Be(409);

			ResponderApproves(_clock.Now);
			(await Service().CompleteApprovalAsync(Caller(Session(lockVersion: 2)), "approval-1")).Outcome.Should().Be(AdpGrantOutcome.ApprovalUnavailable,
				"a shared session's approval is bound to the lock version it was asked at");
		}

		[Test]
		public async Task An_approval_request_for_protected_data_carries_the_session_department_and_lock_version()
		{
			MfaApprovalRequester asked = null;
			_approvals.Setup(a => a.RequestAsync(It.IsAny<MfaApprovalRequester>(), It.IsAny<CancellationToken>()))
				.Callback((MfaApprovalRequester r, CancellationToken _) => asked = r)
				.ReturnsAsync(new MfaApprovalStart { Outcome = MfaApprovalOutcome.Succeeded, ApprovalRequestId = "approval-1", MatchNumber = "42" });

			(await Service().RequestApprovalAsync(Caller(Session(lockVersion: 5)))).Succeeded.Should().BeTrue();

			asked.Purpose.Should().Be(MfaApprovalPurpose.Adp);
			asked.Scope.Should().Be(MfaMethodScope.Adp);
			asked.Kind.Should().Be(MfaApprovalRequesterKind.Session);
			asked.RequesterId.Should().Be(SessionId);
			asked.DepartmentId.Should().Be(DepartmentId);
			asked.LockVersion.Should().Be(5);
			asked.SharedMode.Should().BeTrue();

			(await Service().RequestApprovalAsync(Caller(untracked: true))).Outcome.Should().Be(MfaApprovalOutcome.SessionRequired);
		}

		// ---- Provider step-up -----------------------------------------------------------------------------------------

		private SsoLoginTransaction Redeemed(string sessionId = SessionId, long mappingVersion = 3) => new()
		{
			SsoLoginTransactionId = "sso-1", Purpose = (int)SsoTransactionPurpose.AdpStepUp, DepartmentId = DepartmentId, DepartmentSsoConfigId = ConfigId,
			SessionId = sessionId, ExpectedUserId = UserId, UserId = UserId, AuthenticationGeneration = 4, FederatedMfaValue = "mfa",
			FederatedMappingVersion = mappingVersion, CreatedOnUtc = _clock.Now.AddMinutes(-2), AuthenticatedOnUtc = _clock.Now.AddMinutes(-1)
		};

		private void BrokerRedeems(SsoLoginTransaction transaction) =>
			_broker.Setup(b => b.RedeemAsync("sso-1", "code", "verifier", UserSessionClientApplication.Unit, It.IsAny<CancellationToken>(),
					It.Is<SsoTransactionPurpose[]>(p => p.Length == 1 && p[0] == SsoTransactionPurpose.AdpStepUp)))
				.ReturnsAsync(SsoRedemptionResult.Of(SsoBrokerOutcome.Succeeded, transaction));

		[Test]
		public async Task A_provider_step_up_grant_names_the_tested_mapping_and_dies_when_it_changes()
		{
			BrokerRedeems(Redeemed());

			var grant = Read(await Service().CompleteFederatedAsync(Caller(), "sso-1", "code", "verifier"));

			grant.MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.Federated);
			grant.MfaCredentialId.Should().Be("federated:" + ConfigId + ":3");
			grant.MfaStateVersion.Should().Be(3);
			grant.MfaAtUtc.Should().Be(Seconds(_clock.Now.AddMinutes(-1)), "the provider's authentication time is the verification");
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, _credentials)).Should().Be(ProtectedGrantBindingOutcome.Bound);

			_config.FederatedMfaMappingVersion = 4;
			_config.FederatedMfaTestedVersion = 4;
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(), 15, _credentials)).Should().Be(ProtectedGrantBindingOutcome.MethodUnverifiable,
				"a changed mapping voids grants its old version produced");
		}

		[Test]
		public async Task A_provider_step_up_counts_only_for_the_session_and_account_that_began_it()
		{
			var cases = new (Action<SsoLoginTransaction> Change, string Why)[]
			{
				(t => t.SessionId = "other-session", "another session's round trip"),
				(t => t.UserId = "someone-else", "another provider account"),
				(t => t.FederatedMfaValue = null, "no MFA asserted"),
				(t => t.FederatedMappingVersion = 2, "an older mapping"),
				(t => t.AuthenticationGeneration = 3, "from before a password change"),
				(t => t.DepartmentId = 7, "another department")
			};

			foreach (var (change, why) in cases)
			{
				var transaction = Redeemed();
				change(transaction);
				BrokerRedeems(transaction);
				var refused = await Service().CompleteFederatedAsync(Caller(), "sso-1", "code", "verifier");
				refused.Outcome.Should().Be(AdpGrantOutcome.VerificationFailed, why);
				refused.ErrorCode.Should().Be("federated_mfa_not_satisfied", why);
			}

			BrokerRedeems(Redeemed());
			var lockedSince = new ProtectedGrantSessionContext
			{
				SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4, SessionLockVersion = 1,
				SharedMode = true, SessionLockedOnUtc = _clock.Now.AddSeconds(-30), SessionExpiresOnUtc = _clock.Now.AddHours(8)
			};
			(await Service().CompleteFederatedAsync(Caller(lockedSince), "sso-1", "code", "verifier")).Outcome
				.Should().Be(AdpGrantOutcome.VerificationFailed, "begun before the shared session last locked");

			_evidenceRows.Rows.Should().BeEmpty();
			_activity.Verify(a => a.RecordAsync(It.Is<MfaActivityEntry>(e => !e.Successful && e.Method == MfaEvidenceMethod.Federated), It.IsAny<CancellationToken>()),
				Times.Exactly(cases.Length + 1));
		}

		// ---- Exemption ------------------------------------------------------------------------------------------------

		[Test]
		public async Task An_exempt_grant_is_only_for_an_exempt_client_names_no_method_and_ends_with_the_session()
		{
			(await Service().IssueExemptAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired);

			_decision.StepUpRequired = false;
			var sessionEnd = _clock.Now.AddMinutes(3);
			var issued = await Service().IssueExemptAsync(Caller(Session(sessionEnd)));
			var grant = Read(issued);
			grant.StepUpExempt.Should().BeTrue();
			grant.MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.None);
			grant.MfaCredentialId.Should().BeNull();
			issued.ExpiresOnUtc.Should().BeCloseTo(sessionEnd, TimeSpan.FromSeconds(1));
			_evidenceRows.Rows.Should().BeEmpty("an exemption is not MFA and creates no evidence");
			_audited.Should().Contain(e => e.Outcome == "step-up-exempt" && e.ResourceId == ProtectedDataGrantMfaMethods.None);
			(await ProtectedGrantBinding.CheckAsync(grant, UserId, Session(sessionEnd), 15, _credentials)).Should().Be(ProtectedGrantBindingOutcome.Bound);
		}
	
		// ---- Reusing this session's recent sign-in or unlock MFA (slice 23; plan section 9.1) ------------------------------

		private async Task Verified(MfaEvidenceMethod method, MfaEvidencePurpose purpose, DateTime at, string reference = null, int? department = null,
			UserSessionClientApplication client = UserSessionClientApplication.Unit, long generation = 4)
		{
			var evidence = new MfaEvidenceService(_evidenceRows, new InMemoryUserMfaStateRepository(), _passkeyRows, _sessionRows,
				new InMemoryMfaActivityRepository(), _clock);
			await evidence.RecordAsync(UserId, MfaEvidence.TrackedSessionKey(SessionId), client, MfaEvidenceKind.SecondFactor, method, purpose, at, generation,
				department, reference);
		}

		[Test]
		public async Task Sign_in_mfa_is_reused_for_protected_data_from_its_own_time_where_the_department_allows_it()
		{
			var verifiedAt = _clock.Now.AddMinutes(-8);
			await Verified(MfaEvidenceMethod.Totp, MfaEvidencePurpose.Login, verifiedAt);
			var recorded = _evidenceRows.Rows.Count;

			var issued = await Service().IssueFromRecentEvidenceAsync(Caller());

			issued.Outcome.Should().Be(AdpGrantOutcome.Issued);
			var grant = Read(issued);
			grant.MfaAtUtc.Should().BeCloseTo(verifiedAt, TimeSpan.FromSeconds(1), "the original verification time, never now");
			issued.ExpiresOnUtc.Should().BeCloseTo(verifiedAt.AddMinutes(15), TimeSpan.FromSeconds(1), "the window runs from the verification");
			_evidenceRows.Rows.Count.Should().Be(recorded, "reuse records no new verification");
			_audited.Single().Outcome.Should().Be("mfa-reused");

			_securityPolicy.AcceptRecentLoginMfaForAdp = false;
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired, "the department turned reuse off");
		}

		[Test]
		public async Task Only_a_recent_real_verification_of_this_client_whose_method_protected_data_accepts_is_reused()
		{
			await Verified(MfaEvidenceMethod.Totp, MfaEvidencePurpose.Login, _clock.Now.AddMinutes(-16));
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired, "older than the window");

			_evidenceRows.Rows.Clear();
			await Verified(MfaEvidenceMethod.Totp, MfaEvidencePurpose.Login, _clock.Now.AddMinutes(-1), client: UserSessionClientApplication.Web);
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired, "another client's verification");

			_evidenceRows.Rows.Clear();
			await Verified(MfaEvidenceMethod.Totp, MfaEvidencePurpose.StepUp, _clock.Now.AddMinutes(-1));
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"a step-up for another action is not a sign-in");

			_evidenceRows.Rows.Clear();
			await Verified(MfaEvidenceMethod.Totp, MfaEvidencePurpose.AdpStepUp, _clock.Now.AddMinutes(-1), department: 7);
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"protected-data evidence stays in its own department");
			_evidenceRows.Rows.Clear();
			await Verified(MfaEvidenceMethod.Totp, MfaEvidencePurpose.AdpStepUp, _clock.Now.AddMinutes(-1), department: DepartmentId);
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.Issued);

			_evidenceRows.Rows.Clear();
			await Verified(MfaEvidenceMethod.Federated, MfaEvidencePurpose.Login, _clock.Now.AddMinutes(-1), FederatedMfaMapping.FactorReferenceFor(ConfigId, 3));
			_securityPolicy.AllowFederatedMfaForAdp = false;
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"the department does not accept that method for protected data");
			_securityPolicy.AllowFederatedMfaForAdp = true;
			var federated = await Service().IssueFromRecentEvidenceAsync(Caller());
			federated.Outcome.Should().Be(AdpGrantOutcome.Issued);
			Read(federated).MfaMethod.Should().Be(MfaMethodNames.Federated);

			(await Service().IssueFromRecentEvidenceAsync(Caller(untracked: true))).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired, "a session is needed");
		}

		[Test]
		public async Task A_revoked_passkey_behind_the_sign_in_is_not_reused()
		{
			var passkey = Passkey();
			await Verified(MfaEvidenceMethod.Passkey, MfaEvidencePurpose.Login, _clock.Now.AddMinutes(-2), UserPasskey.FactorReferenceFor("pk-unit"));
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.Issued);

			passkey.RevokedOnUtc = _clock.Now;
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Succeeded.Should().BeFalse("its credential no longer counts");
		}

		[Test]
		public async Task Shared_unlock_mfa_is_reused_only_where_the_department_accepts_it()
		{
			await Verified(MfaEvidenceMethod.Totp, MfaEvidencePurpose.SharedUnlock, _clock.Now.AddMinutes(-1));
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.Issued);

			_securityPolicy.AcceptRecentUnlockMfaForAdp = false;
			(await Service().IssueFromRecentEvidenceAsync(Caller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired);
		}
}
}
