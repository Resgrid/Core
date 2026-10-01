using System;
using System.Collections.Generic;
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
using Resgrid.Web.Models.AccountViewModels;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The Phase 3 exit matrix (passkey plan section 14, Phase 3; workbook section 12, slice 24): Web sign-in writes real session
	/// evidence, and the one ADP issuer decides from it. Recent sign-in MFA serves protected data from its own time, never moved
	/// forward; a password alone, a single sign-on alone and a recovery code never do; the provider's MFA does only where the
	/// department accepts it for protected data; and a fresh step-up still works where reuse does not.
	/// </summary>
	public partial class WebLoginMfaTransactionTests
	{
		private sealed class MatrixAdp : IDisposable
		{
			public ECDsa Key;
			public X509Certificate2 Certificate;
			public ProtectedDataGrantService Grants;
			public AdpStepUpService Issuer;

			public void Dispose()
			{
				Certificate?.Dispose();
				Key?.Dispose();
			}
		}

		/// <summary>A real evidence service behind Web sign-in, and a real ADP issuer reading the same evidence.</summary>
		private MatrixAdp RealEvidenceAndIssuer()
		{
			_gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(true);
			_gates.SetupGet(g => g.EmitGrantV2).Returns(true);
			_gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			var sessionRows = new InMemoryUserSessionsRepository();
			var evidenceRows = new InMemoryMfaEvidenceRepository();
			_evidenceService = new MfaEvidenceService(evidenceRows, new InMemoryUserMfaStateRepository(), _passkeyRows, sessionRows,
				new InMemoryMfaActivityRepository(), TimeProvider.System);

			var adp = new MatrixAdp { Key = ECDsa.Create(ECCurve.NamedCurves.nistP256) };
			adp.Certificate = new CertificateRequest("CN=adp-issuer", adp.Key, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
			adp.Grants = new ProtectedDataGrantService(() => adp.Certificate, () => adp.Certificate);
			var protection = new Mock<IDepartmentDataProtectionService>();
			protection.Setup(p => p.GetPolicyByDepartmentIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new DepartmentDataProtectionPolicy { DepartmentId = DepartmentId, PolicyEpoch = 3, StepUpWindowMinutes = 15 });
			protection.Setup(p => p.GetStepUpDecisionForClientAsync(DepartmentId, It.IsAny<UserSessionClientApplication>(), It.IsAny<bool>()))
				.ReturnsAsync(new AdpStepUpDecision { StepUpRequired = true, PolicyEpoch = 3, StepUpWindowMinutes = 15 });
			var ssoConfigs = new Mock<IDepartmentSsoConfigRepository>();
			ssoConfigs.Setup(r => r.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(() => new[] { _ssoConfig });
			adp.Issuer = new AdpStepUpService(adp.Grants, protection.Object, _policyService, _evidenceService, _passkeys.Object, _approvals.Object, _broker.Object,
				_sso.Object, new MfaCredentialStateService(_passkeyRows, sessionRows, ssoConfigs.Object, TimeProvider.System), _activity.Object,
				Mock.Of<IAdpAuditRepository>(), _gates.Object, TimeProvider.System);
			return adp;
		}

		/// <summary>The caller the ADP issuer sees for the Web session the last sign-in created.</summary>
		private AdpStepUpCaller WebSessionCaller(int sessionNumber = 1) => new()
		{
			UserId = UserId,
			UserName = "user1",
			DepartmentId = DepartmentId,
			Session = new ProtectedGrantSessionContext
			{
				SessionId = "new-session-" + sessionNumber, ClientApplication = (int)UserSessionClientApplication.Web, AuthenticationGeneration = 4,
				SessionExpiresOnUtc = DateTime.UtcNow.AddHours(8)
			},
			ClientApplication = UserSessionClientApplication.Web,
			AccountAuthenticationGeneration = 4,
			AuditSystem = SystemAuditSystems.Website
		};

		[Test]
		public async Task Recent_sign_in_mfa_serves_protected_data_from_its_own_time_and_a_password_alone_never_does()
		{
			using var adp = RealEvidenceAndIssuer();
			_ssoConfig = new DepartmentSsoConfig { DepartmentSsoConfigId = "cfg", DepartmentId = DepartmentId, IsEnabled = true };

			// Password and an authenticator code on the login transaction.
			var secret = await SignInWithPassword();
			var before = DateTime.UtcNow;
			await Browser(secret).Controller.LoginMfa(new LoginMfaViewModel { Code = "123456" }, CancellationToken.None);
			var after = DateTime.UtcNow;

			var reused = await adp.Issuer.IssueFromRecentEvidenceAsync(WebSessionCaller());
			reused.Outcome.Should().Be(AdpGrantOutcome.Issued);
			adp.Grants.ValidateGrant(reused.Token, DepartmentId, 3, ProtectedDataGrantScopes.Read, out var grant).Should().Be(ProtectedDataGrantValidationOutcome.Valid);
			grant.MfaAtUtc.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeOnOrBefore(after.AddSeconds(1), "the sign-in's own verification time");
			reused.ExpiresOnUtc.Should().BeCloseTo(grant.MfaAtUtc.AddMinutes(15), TimeSpan.FromSeconds(1), "never moved forward by asking later");

			// The same sign-in, where the department does not accept reusing it: a new verification is needed.
			_policy.AcceptRecentLoginMfaForAdp = false;
			(await adp.Issuer.IssueFromRecentEvidenceAsync(WebSessionCaller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired);
			var fresh = await adp.Issuer.IssueForTotpAsync(WebSessionCaller(), DateTime.UtcNow);
			fresh.Outcome.Should().Be(AdpGrantOutcome.Issued, "the fresh path still works");
			_policy.AcceptRecentLoginMfaForAdp = true;

			// A password alone (an account without MFA in a department that does not require it) never serves protected data.
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<Resgrid.Model.Identity.IdentityUser>())).ReturnsAsync(false);
			_signIn.Setup(s => s.PasswordSignInAsync("user1", "pw", true, true)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);
			_issued.Clear();
			await Browser().Controller.LogOn(new LoginViewModel { Username = "user1", Password = "pw" }, CancellationToken.None, null);
			_issued.Should().ContainSingle();
			(await adp.Issuer.IssueFromRecentEvidenceAsync(WebSessionCaller(2))).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"a password is not a second factor");
		}

		[Test]
		public async Task A_recovery_code_sign_in_and_a_single_sign_on_alone_never_serve_protected_data()
		{
			using var adp = RealEvidenceAndIssuer();
			SsoReady();

			// Recovery-code sign-in: recovery evidence only.
			var secret = await SignInWithPassword();
			await Browser(secret).Controller.LoginWithRecoveryCode(new VerifyCodeViewModel { Code = "RC-1111" }, CancellationToken.None);
			(await adp.Issuer.IssueFromRecentEvidenceAsync(WebSessionCaller(1))).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"recovery never produces an ADP grant");

			// A single sign-on for an account without MFA: a first factor only.
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<Resgrid.Model.Identity.IdentityUser>())).ReturnsAsync(false);
			Redeems(LoginAtProvider());
			await Browser(ssoTrip: await BeginSsoSignIn()).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);
			(await adp.Issuer.IssueFromRecentEvidenceAsync(WebSessionCaller(2))).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"a provider sign-in without a mapped MFA value is no bypass");
		}

		[Test]
		public async Task The_providers_mfa_at_sign_in_serves_protected_data_only_where_the_department_accepts_it_there()
		{
			using var adp = RealEvidenceAndIssuer();
			SsoReady();
			_policy.AllowFederatedMfaForLoginMfa = true;
			_users.Setup(m => m.GetTwoFactorEnabledAsync(It.IsAny<Resgrid.Model.Identity.IdentityUser>())).ReturnsAsync(false);
			var providerSignedInAt = DateTime.UtcNow.AddMinutes(-2);
			Redeems(LoginAtProvider("amr:mfa", providerSignedInAt));
			await Browser(ssoTrip: await BeginSsoSignIn()).Controller.SsoReturnContinue("code-1", _begun.ClientState, null, CancellationToken.None);

			_policy.AllowFederatedMfaForAdp = false;
			(await adp.Issuer.IssueFromRecentEvidenceAsync(WebSessionCaller())).Outcome.Should().Be(AdpGrantOutcome.StepUpRequired,
				"accepted for sign-in, not for protected data");

			_policy.AllowFederatedMfaForAdp = true;
			var reused = await adp.Issuer.IssueFromRecentEvidenceAsync(WebSessionCaller());
			reused.Outcome.Should().Be(AdpGrantOutcome.Issued);
			adp.Grants.ValidateGrant(reused.Token, DepartmentId, 3, ProtectedDataGrantScopes.Read, out var grant).Should().Be(ProtectedDataGrantValidationOutcome.Valid);
			grant.MfaMethod.Should().Be(MfaMethodNames.Federated);
			grant.MfaAtUtc.Should().BeCloseTo(providerSignedInAt, TimeSpan.FromSeconds(1), "the provider's own authentication time");
		}
	}
}
