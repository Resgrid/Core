using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Chat;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 5b: grant issuers emit version 2 only behind EmitGrantV2 and only for a validated
	/// session, and chat export keeps its explicit step-up rule on session evidence.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class MfaStepUpIssuanceTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;

		private static ProtectedGrantSessionContext Session(bool federated = false) => new()
		{
			SessionId = "session-9", ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4,
			FederatedFirstFactor = federated
		};

		private static ProtectedDataGrantIssueRequest Request(bool exempt = false) => new()
		{
			UserId = UserId, DepartmentId = DepartmentId, SessionId = "claim-session", ClientApp = (int)UserSessionClientApplication.Api,
			PolicyEpoch = 3, WindowMinutes = 15, Scopes = new[] { ProtectedDataGrantScopes.Read }, MfaAtUtc = DateTime.UtcNow,
			StepUpExempt = exempt
		};

		[Test]
		public void Without_the_gate_or_a_validated_session_issuers_stay_on_version_one()
		{
			ProtectedGrantIssueRequests.ForSession(Request(), Session(), ProtectedDataGrantMfaMethods.Totp, emitVersionTwo: false).Version.Should().Be(1);
			var untracked = ProtectedGrantIssueRequests.ForSession(Request(), null, ProtectedDataGrantMfaMethods.Totp, emitVersionTwo: true);
			untracked.Version.Should().Be(1);
			untracked.SessionId.Should().Be("claim-session", "version 1 keeps what it always carried");
		}

		[Test]
		public void Version_two_binds_the_validated_session_not_the_token_claims()
		{
			var request = ProtectedGrantIssueRequests.ForSession(Request(), Session(federated: true), ProtectedDataGrantMfaMethods.Totp, emitVersionTwo: true);

			request.Version.Should().Be(2);
			request.SessionId.Should().Be("session-9");
			request.ClientApp.Should().Be((int)UserSessionClientApplication.Unit);
			request.AuthenticationGeneration.Should().Be(4);
			request.MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.Totp);
			request.FederatedFirstFactor.Should().BeTrue();
		}

		[Test]
		public void An_exempt_version_two_request_names_no_method_and_no_verification_time()
		{
			var request = ProtectedGrantIssueRequests.ForSession(Request(exempt: true), Session(), ProtectedDataGrantMfaMethods.None, emitVersionTwo: true);

			request.MfaMethod.Should().Be(ProtectedDataGrantMfaMethods.None);
			request.MfaAtUtc.Should().Be(default);
		}

		[Test]
		public void A_grant_issued_for_a_session_reads_back_bound_to_exactly_that_session()
		{
			using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
			using var certificate = new CertificateRequest("CN=issuance", ecdsa, HashAlgorithmName.SHA256)
				.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
			var grants = new ProtectedDataGrantService(() => certificate, () => certificate);

			foreach (var exempt in new[] { false, true })
			{
				var token = grants.IssueGrant(ProtectedGrantIssueRequests.ForSession(Request(exempt), Session(),
					exempt ? ProtectedDataGrantMfaMethods.None : ProtectedDataGrantMfaMethods.Totp, emitVersionTwo: true)).Token;

				grants.ValidateGrant(token, DepartmentId, 3, ProtectedDataGrantScopes.Read, out var grant).Should().Be(ProtectedDataGrantValidationOutcome.Valid);
				ProtectedGrantBinding.Check(grant, UserId, Session(), 15).Should().Be(ProtectedGrantBindingOutcome.Bound);
				ProtectedGrantBinding.Check(grant, UserId, new ProtectedGrantSessionContext
				{
					SessionId = "session-10", ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4
				}, 15).Should().Be(ProtectedGrantBindingOutcome.SessionMismatch);
			}
		}

		[TestCase(UserSessionAuthenticationMethod.OidcSso, true)]
		[TestCase(UserSessionAuthenticationMethod.SamlSso, true)]
		[TestCase(UserSessionAuthenticationMethod.LocalPassword, false)]
		public void A_session_records_whether_its_first_factor_was_sso(UserSessionAuthenticationMethod method, bool federated)
		{
			ProtectedGrantSessionContext.From(new UserSession { UserSessionId = "s", AuthenticationMethod = (int)method }, null)
				.FederatedFirstFactor.Should().Be(federated);
		}

		// ---- Chat export: an explicit step-up on this session ---------------------------------------------------------

		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp() => _previousAccessor = ApiClaims._httpContextAccessor;

		[TearDown]
		public void TearDown() => ApiClaims._httpContextAccessor = _previousAccessor;

		private static (ChatModerationController Controller, Mock<IChatModerationService> Moderation, Mock<IMfaEvidenceService> Evidence,
			Mock<ICacheProvider> Cache) Chat(MfaEvidence latestStepUp, bool totpEnrolled = true, bool providerMfaAccepted = false, bool ssoLinked = true)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			http.Items[ProtectedGrantSessionContext.HttpItemKey] = Session();
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;

			var user = new IdentityUser { Id = UserId, AuthenticationGeneration = 4 };
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
			users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(totpEnrolled);
			users.Setup(m => m.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), "123456")).ReturnsAsync(true);

			// An SSO member of a department that accepts its identity provider's MFA for sign-in, with a tested mapping.
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, AllowFederatedMfaForLoginMfa = providerMfaAccepted });
			sso.Setup(s => s.GetTestedFederatedMfaConfigAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSsoConfig { DepartmentSsoConfigId = "cfg", DepartmentId = DepartmentId, IsEnabled = true });
			sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(ssoLinked);
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);

			var authorization = new Mock<IAuthorizationService>();
			authorization.Setup(a => a.IsUserValidWithinLimitsAsync(UserId, DepartmentId)).ReturnsAsync(true);
			authorization.Setup(a => a.CanUserModifyDepartmentAsync(UserId, DepartmentId)).ReturnsAsync(true);
			var flags = new Mock<IFeatureToggleService>();
			flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>(), It.IsAny<System.Collections.Generic.IDictionary<string, string>>())).ReturnsAsync(true);
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.GetLatestStepUpAsync(UserId, "sid:session-9", 4, It.IsAny<CancellationToken>())).ReturnsAsync(latestStepUp);
			var moderation = new Mock<IChatModerationService>();

			var controller = new ChatModerationController(moderation.Object, Mock.Of<IChatChannelService>(), Mock.Of<IChatPermissionService>(),
				Mock.Of<IChatMessageService>(), flags.Object, authorization.Object, Mock.Of<IEventAggregator>(), cache.Object, users.Object, evidence.Object,
				new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), gates.Object), sso.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
			return (controller, moderation, evidence, cache);
		}

		private static void VerifyExportRequested(Mock<IChatModerationService> moderation, Times times) =>
			moderation.Verify(m => m.RequestExportAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime?>(),
				It.IsAny<DateTime?>(), It.IsAny<ChatExportFormat>(), It.IsAny<CancellationToken>(), It.IsAny<ChatModerationContext>()), times);

		[Test]
		public async Task An_export_without_an_explicit_step_up_on_this_session_is_refused()
		{
			var (controller, moderation, _, _) = Chat(latestStepUp: null);

			var result = await controller.RequestExport(new RequestExportInput { ChatChannelId = "c" }, CancellationToken.None);

			result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
			VerifyExportRequested(moderation, Times.Never());
		}

		[Test]
		public async Task A_recent_step_up_on_this_session_releases_the_export()
		{
			var (controller, moderation, _, _) = Chat(new MfaEvidence
			{
				VerifiedOnUtc = DateTime.UtcNow.AddMinutes(-1), Kind = (int)MfaEvidenceKind.SecondFactor, Method = (int)MfaEvidenceMethod.Totp
			});

			await controller.RequestExport(new RequestExportInput { ChatChannelId = "c" }, CancellationToken.None);

			VerifyExportRequested(moderation, Times.Once());
		}

		[Test]
		public async Task An_sso_member_without_a_resgrid_factor_exports_after_provider_step_up_where_the_department_accepts_it()
		{
			var providerStepUp = new MfaEvidence
			{
				VerifiedOnUtc = DateTime.UtcNow.AddMinutes(-1), Kind = (int)MfaEvidenceKind.SecondFactor, Method = (int)MfaEvidenceMethod.Federated
			};

			var (accepted, acceptedModeration, _, _) = Chat(providerStepUp, totpEnrolled: false, providerMfaAccepted: true);
			await accepted.RequestExport(new RequestExportInput { ChatChannelId = "c" }, CancellationToken.None);
			VerifyExportRequested(acceptedModeration, Times.Once());

			var (notYet, notYetModeration, _, _) = Chat(latestStepUp: null, totpEnrolled: false, providerMfaAccepted: true);
			var stepUp = await notYet.RequestExport(new RequestExportInput { ChatChannelId = "c" }, CancellationToken.None);
			stepUp.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized,
				"they can verify with provider step-up, so they are asked to, not told to enroll");
			VerifyExportRequested(notYetModeration, Times.Never());

			var (refused, refusedModeration, _, _) = Chat(providerStepUp, totpEnrolled: false, providerMfaAccepted: false);
			var enroll = await refused.RequestExport(new RequestExportInput { ChatChannelId = "c" }, CancellationToken.None);
			enroll.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden,
				"a department that does not accept its provider's MFA needs a Resgrid factor");
			VerifyExportRequested(refusedModeration, Times.Never());

			var (unlinked, unlinkedModeration, _, _) = Chat(latestStepUp: null, totpEnrolled: false, providerMfaAccepted: true, ssoLinked: false);
			(await unlinked.RequestExport(new RequestExportInput { ChatChannelId = "c" }, CancellationToken.None)).Result.Should().BeOfType<ObjectResult>()
				.Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden, "a member not signed in through the provider cannot use provider step-up");
			VerifyExportRequested(unlinkedModeration, Times.Never());
		}

		[Test]
		public async Task The_code_endpoint_still_needs_an_authenticator_app()
		{
			var (controller, _, evidence, _) = Chat(latestStepUp: null, totpEnrolled: false, providerMfaAccepted: true);

			var result = await controller.VerifyExportMfa(new VerifyExportMfaInput { TotpCode = "123456" }, CancellationToken.None);

			result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
			evidence.Verify(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<MfaEvidenceKind>(),
				It.IsAny<MfaEvidenceMethod>(), It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int?>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Verifying_for_export_records_step_up_evidence_instead_of_a_per_user_proof()
		{
			var (controller, _, evidence, cache) = Chat(latestStepUp: null);

			await controller.VerifyExportMfa(new VerifyExportMfaInput { TotpCode = "123456" }, CancellationToken.None);

			evidence.Verify(e => e.RecordAsync(UserId, "sid:session-9", UserSessionClientApplication.Unit, MfaEvidenceKind.SecondFactor,
				MfaEvidenceMethod.Totp, MfaEvidencePurpose.StepUp, It.IsAny<DateTime>(), 4, DepartmentId, null, It.IsAny<CancellationToken>()), Times.Once);
			cache.Verify(c => c.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
		}
	}
}
