using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Models.Security;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Mfa;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using Sso = Resgrid.Web.Services.Models.v4.Sso;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 10 (section 7.8): the mapping's version and test record, provider step-up for API
	/// operations, and the guards that keep provider step-up from authorizing changes to itself. Sign-in with provider MFA
	/// is proven over HTTP in <see cref="LoginMfaTransactionApiTests"/>.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class ProviderStepUpTests
	{
		private const string UserId = "user-1";
		private const string OtherAdmin = "admin-2";
		private const int DepartmentId = 42;
		private const string SessionId = "session-9";
		private const string ConfigId = "cfg";

		private IHttpContextAccessor _previousApiAccessor;

		[SetUp]
		public void SetUp() => _previousApiAccessor = ApiClaims._httpContextAccessor;

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = _previousApiAccessor;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		private static string OidcMapping() => new FederatedMfaMapping { RequestAcrValues = new() { "mfa-level" }, AcceptAmr = new() { "mfa" } }.Serialize();

		private static DepartmentSsoConfig Config(long version = 3, long? tested = 3, string mapping = null) => new()
		{
			DepartmentSsoConfigId = ConfigId, DepartmentId = DepartmentId, SsoProviderType = (int)SsoProviderType.Oidc, IsEnabled = true,
			Authority = "https://idp.example.test", ClientId = "resgrid-client", FederatedMfaMappingJson = mapping ?? OidcMapping(),
			FederatedMfaMappingVersion = version, FederatedMfaTestedVersion = tested
		};

		private static IPasskeyFeatureGates Deployed()
		{
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(true);
			return gates.Object;
		}

		private static string ProblemType(IConvertToActionResult result) =>
			result.Convert() is ObjectResult { Value: ProblemDetails problem } ? problem.Type : null;

		private static DefaultHttpContext ApiContext(string userId = UserId)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()), new Claim(ClaimTypes.Name, "user1")
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
			{
				SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Responder, AuthenticationGeneration = 4
			};
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;
			return http;
		}

		// ---- The mapping's version and test (DepartmentSsoService) ---------------------------------------------------

		private sealed class SsoHarness
		{
			public readonly Mock<IDepartmentSsoConfigRepository> Configs = new();
			public readonly Mock<IDepartmentMembersRepository> Members = new();
			public readonly Mock<IUserSessionMfaEvidenceRepository> Evidence = new();
			public readonly DepartmentSsoService Service;
			public long Version;

			public SsoHarness(DepartmentSsoConfig stored)
			{
				Version = stored?.FederatedMfaMappingVersion ?? 0;
				Configs.Setup(r => r.GetByDepartmentIdAndTypeAsync(DepartmentId, SsoProviderType.Oidc)).ReturnsAsync(stored);
				Configs.Setup(r => r.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(() => stored == null ? new List<DepartmentSsoConfig>() : new List<DepartmentSsoConfig> { stored });
				Configs.Setup(r => r.InsertAsync(It.IsAny<DepartmentSsoConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((DepartmentSsoConfig c, CancellationToken _, bool _) => c);
				Configs.Setup(r => r.UpdateAsync(It.IsAny<DepartmentSsoConfig>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((DepartmentSsoConfig c, CancellationToken _, bool _) => c);
				Configs.Setup(r => r.AdvanceFederatedMfaMappingVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => ++Version);
				var encryption = new Mock<IEncryptionService>();
				encryption.Setup(e => e.EncryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
					.Returns((string value, int _, string _) => "encrypted:" + value);
				Service = new DepartmentSsoService(Configs.Object, Mock.Of<IDepartmentSecurityPolicyRepository>(), Members.Object, Mock.Of<IDepartmentsService>(),
					Mock.Of<IUserProfileService>(), encryption.Object, Mock.Of<ICacheProvider>(), Mock.Of<IExternalIdentityLinkService>(), Mock.Of<ILimitsService>(),
					Mock.Of<Resgrid.Model.Repositories.Queries.IUnitOfWork>(), Mock.Of<IDepartmentDataProtectionPolicyRepository>(),
					new Lazy<IDepartmentDataProtectionService>(() => Mock.Of<IDepartmentDataProtectionService>()), Evidence.Object, Mock.Of<IAuditLogsRepository>());
			}

			public void VerifyAdvanced(Times times) =>
				Configs.Verify(r => r.AdvanceFederatedMfaMappingVersionAsync(ConfigId, It.IsAny<CancellationToken>()), times);
		}

		/// <summary>An update as the editors send it: the stored configuration with some fields changed.</summary>
		private static DepartmentSsoConfig Edited(DepartmentSsoConfig stored, Action<DepartmentSsoConfig> change)
		{
			var edited = (DepartmentSsoConfig)stored.GetType().GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
				.Invoke(stored, null)!;
			change(edited);
			return edited;
		}

		[Test]
		public async Task Changing_the_mapping_advances_its_version_and_retires_what_the_old_version_verified()
		{
			var stored = Config();
			var harness = new SsoHarness(stored);

			var saved = await harness.Service.SaveSsoConfigAsync(Edited(stored, c => c.FederatedMfaMappingJson =
				new FederatedMfaMapping { AcceptAmr = new() { "hwk" } }.Serialize()), "DEPT");

			saved.FederatedMfaMappingVersion.Should().Be(4);
			harness.VerifyAdvanced(Times.Once());
			harness.Evidence.Verify(e => e.RevokeByFactorReferenceAsync("federated:cfg:3", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once,
				"sessions verified under the old mapping stop counting");
		}

		[TestCase("Authority", true)]
		[TestCase("ClientId", true)]
		[TestCase("IdpSsoUrl", true)]
		[TestCase("IdpCertificate", true)]
		[TestCase("AllowLocalLogin", false)]
		[TestCase("AttributeMappingJson", false)]
		[TestCase("Nothing", false)]
		public async Task A_new_issuer_or_client_needs_a_new_test_and_other_edits_do_not(string field, bool advances)
		{
			var stored = Config();
			stored.EncryptedIdpCertificate = "encrypted:old-certificate";
			var harness = new SsoHarness(stored);

			await harness.Service.SaveSsoConfigAsync(Edited(stored, c =>
			{
				switch (field)
				{
					case "Authority": c.Authority = "https://other-tenant.example.test"; break;
					case "ClientId": c.ClientId = "another-client"; break;
					case "IdpSsoUrl": c.IdpSsoUrl = "https://idp.example.test/sso"; break;
					case "IdpCertificate": c.EncryptedIdpCertificate = "new-certificate"; break;
					case "AllowLocalLogin": c.AllowLocalLogin = !c.AllowLocalLogin; break;
					case "AttributeMappingJson": c.AttributeMappingJson = "{\"email\":\"mail\"}"; break;
				}
			}), "DEPT");

			harness.VerifyAdvanced(advances ? Times.Once() : Times.Never());
		}

		[Test]
		public async Task Without_a_mapping_nothing_is_versioned_and_a_first_mapping_starts_at_version_one()
		{
			var unmapped = Config();
			unmapped.FederatedMfaMappingJson = null;
			unmapped.FederatedMfaMappingVersion = 0;
			unmapped.FederatedMfaTestedVersion = null;
			var harness = new SsoHarness(unmapped);
			await harness.Service.SaveSsoConfigAsync(Edited(unmapped, c => c.Authority = "https://other-tenant.example.test"), "DEPT");
			harness.VerifyAdvanced(Times.Never());

			var fresh = new SsoHarness(null);
			var inserted = await fresh.Service.SaveSsoConfigAsync(Config(0, null), "DEPT");
			inserted.FederatedMfaMappingVersion.Should().Be(1);
			fresh.Evidence.Verify(e => e.RevokeByFactorReferenceAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never,
				"a new configuration has nothing to retire");
		}

		[Test]
		public async Task Provider_step_up_is_offered_only_with_a_tested_mapping_to_members_the_provider_has_signed_in()
		{
			var harness = new SsoHarness(Config());
			var members = new List<DepartmentMember>
			{
				new() { UserId = UserId, DepartmentId = DepartmentId, ExternalSsoId = "external-user" },
				new() { UserId = "local-only", DepartmentId = DepartmentId },
				new() { UserId = "removed", DepartmentId = DepartmentId, ExternalSsoId = "gone", IsDeleted = true }
			};
			harness.Members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(DepartmentId)).ReturnsAsync(members);

			(await harness.Service.IsFederatedMfaAvailableAsync(DepartmentId, UserId)).Should().BeTrue();
			(await harness.Service.IsFederatedMfaAvailableAsync(DepartmentId, "local-only")).Should().BeFalse("the provider has never signed this member in");
			(await harness.Service.IsFederatedMfaAvailableAsync(DepartmentId, "removed")).Should().BeFalse();
			(await harness.Service.GetTestedFederatedMfaConfigAsync(DepartmentId)).Should().NotBeNull();

			var untested = new SsoHarness(Config(tested: 2));
			untested.Members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(DepartmentId)).ReturnsAsync(members);
			(await untested.Service.IsFederatedMfaAvailableAsync(DepartmentId, UserId)).Should().BeFalse("version 3 has not passed its test");
			(await untested.Service.GetTestedFederatedMfaConfigAsync(DepartmentId)).Should().BeNull();
		}

		// ---- API provider step-up (MfaController) --------------------------------------------------------------------

		public sealed class MfaHarness
		{
			public readonly Mock<IMfaEvidenceService> Evidence = new();
			public readonly Mock<ISsoBrokerService> Broker = new();
			public readonly Mock<IDepartmentSsoService> Sso = new();
			public readonly Mock<UserManager<IdentityUser>> Users;
			public readonly DepartmentSecurityPolicy Policy = new() { DepartmentId = DepartmentId, AllowFederatedMfaForLoginMfa = true };
			public DepartmentSsoConfig Tested = Config();
			public SsoLoginTransaction Redeemed;
			public readonly MfaController Controller;

			public MfaHarness()
			{
				var user = new IdentityUser { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };
				Users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
				Users.Setup(m => m.FindByIdAsync(UserId)).ReturnsAsync(user);
				Users.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(false);
				Sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Policy);
				Sso.Setup(s => s.GetTestedFederatedMfaConfigAsync(DepartmentId, It.IsAny<CancellationToken>()))
					.ReturnsAsync(() => FederatedMfaMapping.IsTested(Tested) ? Tested : null);
				Sso.Setup(s => s.IsFederatedMfaAvailableAsync(DepartmentId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => FederatedMfaMapping.IsTested(Tested));
				Redeemed = new SsoLoginTransaction
				{
					SsoLoginTransactionId = "sso-1", Purpose = (int)SsoTransactionPurpose.StepUp, DepartmentId = DepartmentId, DepartmentSsoConfigId = ConfigId,
					Operation = MfaStepUpOperations.SecurityChange, SessionId = SessionId, ExpectedUserId = UserId, UserId = UserId, AuthenticationGeneration = 4,
					FederatedMappingVersion = 3, FederatedMfaValue = "amr:mfa", AuthenticatedOnUtc = DateTime.UtcNow.AddSeconds(-30),
					State = (int)SsoLoginTransactionState.Redeemed
				};
				Broker.Setup(b => b.RedeemAsync("sso-1", "code", "verifier", UserSessionClientApplication.Responder, It.IsAny<CancellationToken>(),
						It.Is<SsoTransactionPurpose[]>(p => p.SequenceEqual(new[] { SsoTransactionPurpose.StepUp }))))
					.ReturnsAsync(() => SsoRedemptionResult.Of(SsoBrokerOutcome.Succeeded, Redeemed));
				var cache = new Mock<ICacheProvider>();
				cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
				Controller = new MfaController(Users.Object, new MfaPolicyService(Sso.Object, new InMemoryUserMfaStateRepository(), Deployed()), Evidence.Object,
					cache.Object, Mock.Of<ISystemAuditsService>(), Mock.Of<IPasskeyService>(), Sso.Object, Broker.Object, Mock.Of<IMfaApprovalService>(),
					Mock.Of<IMfaActivityService>())
				{
					ControllerContext = new ControllerContext { HttpContext = ApiContext() }
				};
			}

			public static VerifyStepUpInput Input(string operation = MfaStepUpOperations.SecurityChange) => new()
			{
				Operation = operation, Method = MfaMethodNames.Federated, SsoTransactionId = "sso-1", SsoCode = "code", CodeVerifier = "verifier"
			};

			public void VerifyNothingRecorded() =>
				Evidence.Verify(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<MfaEvidenceKind>(),
					It.IsAny<MfaEvidenceMethod>(), It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int?>(),
					It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_provider_step_up_becomes_federated_evidence_at_the_providers_authentication_time()
		{
			var harness = new MfaHarness();

			var result = await harness.Controller.VerifyStepUp(MfaHarness.Input(), CancellationToken.None);

			ProblemType(result).Should().BeNull();
			DateTime.Parse(result.Value.Data.VerifiedAt).ToUniversalTime().Should().BeCloseTo(harness.Redeemed.AuthenticatedOnUtc!.Value, TimeSpan.FromSeconds(1));
			harness.Evidence.Verify(e => e.RecordAsync(UserId, "sid:" + SessionId, UserSessionClientApplication.Responder, MfaEvidenceKind.SecondFactor,
				MfaEvidenceMethod.Federated, MfaEvidencePurpose.StepUp, harness.Redeemed.AuthenticatedOnUtc.Value, 4, DepartmentId, "federated:cfg:3",
				It.IsAny<CancellationToken>()), Times.Once, "the member needs no Resgrid factor for provider step-up");
		}

		private static IEnumerable<TestCaseData> Mismatches()
		{
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Redeemed.Operation = MfaStepUpOperations.AdpManagement), "federated_mfa_not_satisfied")
				.SetName("A step-up begun for another operation");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Redeemed.SessionId = "another-session"), "federated_mfa_not_satisfied")
				.SetName("A step-up begun by another session");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Redeemed.AuthenticationGeneration = 3), "federated_mfa_not_satisfied")
				.SetName("A step-up from before a password change");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Redeemed.UserId = "someone-else"), "federated_mfa_not_satisfied")
				.SetName("A step-up that signed in another account");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Redeemed.DepartmentId = 7), "federated_mfa_not_satisfied")
				.SetName("A step-up for another department");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Tested = Config(4, 4)), "federated_mfa_not_satisfied")
				.SetName("A mapping changed since the provider answered");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Redeemed.FederatedMfaValue = null), "federated_mfa_not_satisfied")
				.SetName("A step-up without a mapped value");
			yield return new TestCaseData((Action<MfaHarness>)(h =>
				{
					// Plan section 12.5.3: begun before this shared session's last lock, so it does not count after the unlock.
					h.Redeemed.CreatedOnUtc = DateTime.UtcNow.AddMinutes(-3);
					h.Controller.HttpContext.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
					{
						SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Responder, AuthenticationGeneration = 4, SharedMode = true,
						SessionLockVersion = 1, SessionLockedOnUtc = DateTime.UtcNow.AddMinutes(-1)
					};
				}), "federated_mfa_not_satisfied")
				.SetName("A step-up begun before a shared session locked");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Broker.Setup(b => b.RedeemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<UserSessionClientApplication>(), It.IsAny<CancellationToken>(), It.IsAny<SsoTransactionPurpose[]>()))
					.ReturnsAsync(SsoRedemptionResult.Of(SsoBrokerOutcome.AlreadyUsed))), "sso_transaction_invalid")
				.SetName("A spent code");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Policy.AllowFederatedMfaForLoginMfa = false), "mfa_method_not_allowed")
				.SetName("A department that does not accept provider MFA");
			yield return new TestCaseData((Action<MfaHarness>)(h => h.Tested = Config(3, null)), "mfa_method_not_allowed")
				.SetName("An untested mapping");
		}

		[TestCaseSource(nameof(Mismatches))]
		public async Task A_provider_step_up_counts_only_for_the_session_operation_and_mapping_it_was_begun_for(Action<MfaHarness> change, string problem)
		{
			var harness = new MfaHarness();
			change(harness);

			ProblemType(await harness.Controller.VerifyStepUp(MfaHarness.Input(), CancellationToken.None)).Should().Be(problem);
			harness.VerifyNothingRecorded();
		}

		[Test]
		public async Task Provider_step_up_never_manages_account_factors()
		{
			var harness = new MfaHarness();
			harness.Redeemed.Operation = MfaStepUpOperations.AccountSecurity;

			ProblemType(await harness.Controller.VerifyStepUp(MfaHarness.Input(MfaStepUpOperations.AccountSecurity), CancellationToken.None))
				.Should().Be("mfa_method_not_allowed");
			harness.Broker.Verify(b => b.RedeemAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(),
				It.IsAny<CancellationToken>(), It.IsAny<SsoTransactionPurpose[]>()), Times.Never, "the code is not even spent");

			var account = (await harness.Controller.StepUpOptions(MfaStepUpOperations.AccountSecurity, CancellationToken.None)).Value.Data;
			account.Methods.Should().NotContain(MfaMethodNames.Federated);
			harness.Sso.Verify(s => s.IsFederatedMfaAvailableAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
				"account factors never look provider step-up up");
			var security = (await harness.Controller.StepUpOptions(MfaStepUpOperations.SecurityChange, CancellationToken.None)).Value.Data;
			security.Methods.Should().Equal(MfaMethodNames.Federated);
			security.EnrollmentRequired.Should().BeFalse("an SSO member with a tested mapping can step up without a Resgrid factor");
			var adp = (await harness.Controller.StepUpOptions(MfaStepUpOperations.AdpManagement, CancellationToken.None)).Value.Data;
			adp.Methods.Should().BeEmpty("the ADP switch is separate and off");
		}

		// ---- SSO administration (SsoAdminController) -----------------------------------------------------------------

		private sealed class AdminHarness
		{
			public readonly Mock<IDepartmentSsoService> Sso = new();
			public readonly Mock<ISsoBrokerService> Broker = new();
			public readonly Mock<ISystemAuditsService> Audits = new();
			public DepartmentSsoConfig Active = Config(3, null);
			public DepartmentSecurityPolicy Stored = new() { DepartmentId = DepartmentId };
			public MfaEvidence Latest = new() { VerifiedOnUtc = DateTime.UtcNow, Method = (int)MfaEvidenceMethod.Totp };
			public readonly Resgrid.Web.Services.Controllers.v4.SsoAdminController Controller;

			public AdminHarness(string userId = UserId)
			{
				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "DEPT", ManagingUserId = UserId, AdminUsers = new List<string> { OtherAdmin } });
				Sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => new[] { Active });
				Sso.Setup(s => s.GetTestedFederatedMfaConfigAsync(DepartmentId, It.IsAny<CancellationToken>()))
					.ReturnsAsync(() => FederatedMfaMapping.IsTested(Active) ? Active : null);
				Sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Stored);
				Sso.Setup(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((DepartmentSecurityPolicy p, string _, CancellationToken _) => p);
				Sso.Setup(s => s.SaveSsoConfigAsync(It.IsAny<DepartmentSsoConfig>(), "DEPT", It.IsAny<CancellationToken>()))
					.ReturnsAsync((DepartmentSsoConfig c, string _, CancellationToken _) =>
					{
						c.FederatedMfaMappingVersion++;
						c.FederatedMfaTestedVersion = null;
						return c;
					});
				var evidence = new Mock<IMfaEvidenceService>();
				evidence.Setup(e => e.GetLatestSecondFactorAsync(userId, "sid:" + SessionId, 4, It.IsAny<CancellationToken>())).ReturnsAsync(() => Latest);
				Controller = new Resgrid.Web.Services.Controllers.v4.SsoAdminController(Sso.Object, departments.Object, Mock.Of<IPermissionsService>(),
					Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), evidence.Object,
					new MfaPolicyService(Sso.Object, new InMemoryUserMfaStateRepository(), Deployed()), Broker.Object, Audits.Object, Deployed())
				{
					ControllerContext = new ControllerContext { HttpContext = ApiContext(userId) }
				};
			}

			/// <summary>Recent provider step-up evidence the department otherwise accepts for security changes.</summary>
			public void LatestIsProviderStepUp()
			{
				Active = Config();
				Stored.AllowFederatedMfaForLoginMfa = true;
				Latest = new MfaEvidence { VerifiedOnUtc = DateTime.UtcNow, Method = (int)MfaEvidenceMethod.Federated };
			}
		}

		[Test]
		public async Task Saving_a_mapping_validates_it_and_takes_the_managing_member_with_a_resgrid_factor()
		{
			var mapping = JObject.Parse("{\"requestAcrValues\":[\"mfa-level\"],\"acceptAmr\":[\"mfa\"]}");

			var otherAdmin = new AdminHarness(OtherAdmin);
			ProblemType(await otherAdmin.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput { Mapping = mapping }, CancellationToken.None))
				.Should().Be("managing_member_required");

			var viaProvider = new AdminHarness();
			viaProvider.LatestIsProviderStepUp();
			ProblemType(await viaProvider.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput { Mapping = mapping }, CancellationToken.None))
				.Should().Be("step_up_required", "provider step-up cannot authorize a change to itself");

			var harness = new AdminHarness();
			ProblemType(await harness.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput
			{
				Mapping = JObject.Parse("{\"acceptAmr\":[\"pwd\"]}")
			}, CancellationToken.None)).Should().Be("invalid_request");
			harness.Sso.Verify(s => s.SaveSsoConfigAsync(It.IsAny<DepartmentSsoConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var saved = (await harness.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput { Mapping = mapping }, CancellationToken.None)).Value.Data;
			saved.MappingVersion.Should().Be(4);
			saved.Effective.Should().BeFalse("a saved mapping needs its test");
			saved.Mapping!["acceptAmr"]!.Values<string>().Should().Equal("mfa");
			harness.Active.FederatedMfaMappingJson.Should().Be(FederatedMfaMapping.Parse(mapping.ToString()).Serialize());

			await harness.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput { Mapping = JValue.CreateNull() }, CancellationToken.None);
			harness.Active.FederatedMfaMappingJson.Should().BeNull("a null mapping removes provider step-up");
		}

		[Test]
		public async Task Changing_or_removing_the_mapping_is_audited_and_a_refused_change_is_not()
		{
			var refused = new AdminHarness();
			await refused.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput { Mapping = JObject.Parse("{\"acceptAmr\":[\"pwd\"]}") },
				CancellationToken.None);
			refused.Audits.Verify(a => a.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()), Times.Never);

			var harness = new AdminHarness();
			harness.Active = Config();
			await harness.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput { Mapping = JObject.Parse("{\"acceptAmr\":[\"otp\"]}") },
				CancellationToken.None);
			harness.Audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.FederatedMfaMappingChanged &&
				x.System == (int)SystemAuditSystems.Api && x.UserId == UserId && x.Successful && x.Data.Contains("saved as version 4")),
				It.IsAny<CancellationToken>()), Times.Once);

			await harness.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput { Mapping = JValue.CreateNull() }, CancellationToken.None);
			harness.Audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.FederatedMfaMappingChanged &&
				x.Data.Contains("removed")), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Saving_the_mapping_already_stored_is_not_audited_as_a_change()
		{
			var harness = new AdminHarness();
			harness.Active = Config();

			await harness.Controller.SaveFederatedMfaMapping(new Sso.SaveFederatedMfaMappingInput { Mapping = JObject.Parse(OidcMapping()) },
				CancellationToken.None);

			harness.Audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.FederatedMfaMappingChanged),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_mapping_test_starts_bound_to_the_managing_members_session()
		{
			var harness = new AdminHarness();
			harness.Broker.Setup(b => b.BeginAsync(It.IsAny<SsoBeginRequest>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new Resgrid.Model.Security.SsoBeginResult { Outcome = SsoBrokerOutcome.Succeeded, AuthorizeUrl = "https://idp/authorize", TransactionId = "sso-1" });

			var begun = await harness.Controller.BeginFederatedMfaTest(new Sso.FederatedMfaTestBeginInput
			{
				ReturnTarget = "resgrid://sso-return", CodeChallenge = "challenge", CodeChallengeMethod = "S256"
			}, CancellationToken.None);

			begun.Value.Data.SsoTransactionId.Should().Be("sso-1");
			harness.Broker.Verify(b => b.BeginAsync(It.Is<SsoBeginRequest>(r => r.Purpose == SsoTransactionPurpose.MappingTest && r.SessionId == SessionId &&
				r.UserId == UserId && r.AuthenticationGeneration == 4 && r.ClientApplication == UserSessionClientApplication.Responder &&
				r.DepartmentId == DepartmentId), It.IsAny<CancellationToken>()), Times.Once);

			var viaProvider = new AdminHarness();
			viaProvider.LatestIsProviderStepUp();
			ProblemType(await viaProvider.Controller.BeginFederatedMfaTest(new Sso.FederatedMfaTestBeginInput(), CancellationToken.None)).Should().Be("step_up_required");
		}

		private static SsoLoginTransaction TestRound() => new()
		{
			SsoLoginTransactionId = "sso-1", Purpose = (int)SsoTransactionPurpose.MappingTest, DepartmentId = DepartmentId, DepartmentSsoConfigId = ConfigId,
			SessionId = SessionId, ExpectedUserId = UserId, UserId = UserId, FederatedMappingVersion = 3, FederatedMfaValue = "amr:mfa"
		};

		private static void Redeems(AdminHarness harness, SsoLoginTransaction transaction) =>
			harness.Broker.Setup(b => b.RedeemAsync("sso-1", "code", "verifier", UserSessionClientApplication.Responder, It.IsAny<CancellationToken>(),
					It.Is<SsoTransactionPurpose[]>(p => p.SequenceEqual(new[] { SsoTransactionPurpose.MappingTest }))))
				.ReturnsAsync(SsoRedemptionResult.Of(SsoBrokerOutcome.Succeeded, transaction));

		private static readonly Sso.FederatedMfaTestCompleteInput TestInput = new() { SsoTransactionId = "sso-1", SsoCode = "code", CodeVerifier = "verifier" };

		[Test]
		public async Task A_passed_test_records_exactly_the_version_it_tested_and_is_audited()
		{
			var harness = new AdminHarness();
			Redeems(harness, TestRound());
			harness.Sso.Setup(s => s.RecordFederatedMfaTestAsync(ConfigId, 3, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var result = (await harness.Controller.CompleteFederatedMfaTest(TestInput, CancellationToken.None)).Value.Data;

			result.Tested.Should().BeTrue();
			result.MappingVersion.Should().Be(3);
			result.MatchedValue.Should().Be("amr:mfa");
			harness.Audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(audit => audit.Type == (int)SystemAuditTypes.FederatedMfaMappingTested &&
				audit.Successful && audit.UserId == UserId && audit.Data.Contains("version 3")), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task A_test_of_a_changed_mapping_or_another_session_records_nothing()
		{
			var changed = new AdminHarness();
			Redeems(changed, TestRound());
			changed.Sso.Setup(s => s.RecordFederatedMfaTestAsync(ConfigId, 3, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			ProblemType(await changed.Controller.CompleteFederatedMfaTest(TestInput, CancellationToken.None)).Should().Be("federated_mapping_changed");
			changed.Audits.Verify(a => a.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()), Times.Never);

			var foreign = new AdminHarness();
			var round = TestRound();
			round.SessionId = "another-session";
			Redeems(foreign, round);
			ProblemType(await foreign.Controller.CompleteFederatedMfaTest(TestInput, CancellationToken.None)).Should().Be("sso_transaction_invalid");
			foreign.Sso.Verify(s => s.RecordFederatedMfaTestAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var unmatched = new AdminHarness();
			var failed = TestRound();
			failed.FederatedMfaValue = null;
			Redeems(unmatched, failed);
			ProblemType(await unmatched.Controller.CompleteFederatedMfaTest(TestInput, CancellationToken.None)).Should().Be("sso_transaction_invalid");
			unmatched.Sso.Verify(s => s.RecordFederatedMfaTestAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var otherAdmin = new AdminHarness(OtherAdmin);
			Redeems(otherAdmin, TestRound());
			ProblemType(await otherAdmin.Controller.CompleteFederatedMfaTest(TestInput, CancellationToken.None)).Should().Be("managing_member_required");
		}

		[Test]
		public async Task Turning_provider_mfa_on_through_the_api_needs_a_tested_mapping_and_a_resgrid_factor()
		{
			var enable = new Sso.SaveSecurityPolicyInput { MinPasswordLength = 8, AllowFederatedMfaForLoginMfa = true };

			var untested = new AdminHarness();
			ProblemType(await untested.Controller.SaveSecurityPolicy(enable, CancellationToken.None)).Should().Be("federated_mapping_untested");
			untested.Sso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			// Provider step-up already counts for security changes here (the sign-in switch is on), so only the guard on
			// turning a switch on can refuse it.
			var viaProvider = new AdminHarness();
			viaProvider.LatestIsProviderStepUp();
			ProblemType(await viaProvider.Controller.SaveSecurityPolicy(new Sso.SaveSecurityPolicyInput { MinPasswordLength = 8, AllowFederatedMfaForAdp = true },
				CancellationToken.None)).Should().Be("step_up_required", "provider step-up cannot authorize turning itself on for ADP");
			viaProvider.Sso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var harness = new AdminHarness();
			harness.Active = Config();
			ProblemType(await harness.Controller.SaveSecurityPolicy(enable, CancellationToken.None)).Should().BeNull();
			harness.Sso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p => p.AllowFederatedMfaForLoginMfa), UserId,
				It.IsAny<CancellationToken>()), Times.Once);

			var off = new AdminHarness();
			off.LatestIsProviderStepUp();
			ProblemType(await off.Controller.SaveSecurityPolicy(new Sso.SaveSecurityPolicyInput { MinPasswordLength = 8, AllowFederatedMfaForLoginMfa = false },
				CancellationToken.None)).Should().BeNull("turning it off is not guarded beyond the usual step-up");
		}

		// ---- Web security policy page (SecurityController) -----------------------------------------------------------

		private static (Resgrid.Web.Areas.User.Controllers.SecurityController Controller, Mock<IDepartmentSsoService> Sso) WebPolicy(MfaEvidence latest,
			DepartmentSsoConfig tested)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(SessionClaimTypes.SessionId, SessionId), new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = UserId });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<DepartmentSsoConfig>());
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, DepartmentSecurityPolicyId = 3 });
			sso.Setup(s => s.GetTestedFederatedMfaConfigAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(tested);
			var strings = new Mock<Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security>>();
			strings.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new Microsoft.Extensions.Localization.LocalizedString(key, key));
			var user = new IdentityUser { Id = UserId, AuthenticationGeneration = 4 };
			var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.GetLatestSecondFactorAsync(UserId, "sid:" + SessionId, 4, It.IsAny<CancellationToken>())).ReturnsAsync(latest);
			var policy = new Mock<IMfaPolicyService>();
			policy.Setup(p => p.IsEvidenceAcceptedAsync(It.IsAny<int?>(), It.IsAny<MfaMethodScope>(), It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			var controller = new Resgrid.Web.Areas.User.Controllers.SecurityController(departments.Object, Mock.Of<IAuditService>(), Mock.Of<IPermissionsService>(),
				Mock.Of<IEventAggregator>(), Mock.Of<IDepartmentSettingsService>(), Mock.Of<ISystemAuditsService>(), users.Object, strings.Object, sso.Object,
				Mock.Of<IEncryptionService>(), Mock.Of<IRecordsCutoverService>(), Deployed(), evidence.Object, policy.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
			};
			return (controller, sso);
		}

		private static SecurityPolicyEditView EnableOnWeb() => new()
		{
			MinPasswordLength = 12, AllowPasskeysForLoginMfa = true, AllowPasskeysForAdp = true, AllowResponderApproval = true,
			AcceptRecentLoginMfaForAdp = true, AcceptRecentUnlockMfaForAdp = true, AllowFederatedMfaForLoginMfa = true
		};

		[Test]
		public async Task Turning_provider_mfa_on_in_the_web_needs_a_tested_mapping_and_a_resgrid_factor()
		{
			var totp = new MfaEvidence { VerifiedOnUtc = DateTime.UtcNow, Method = (int)MfaEvidenceMethod.Totp };
			var provider = new MfaEvidence { VerifiedOnUtc = DateTime.UtcNow, Method = (int)MfaEvidenceMethod.Federated };

			var (untested, untestedSso) = WebPolicy(totp, null);
			var refused = await untested.SecurityPolicy(EnableOnWeb(), CancellationToken.None);
			refused.Should().BeOfType<ViewResult>();
			untested.ModelState[string.Empty]!.Errors.Single().ErrorMessage.Should().Be("SecurityPolicyFederatedMappingUntested");
			untestedSso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var (viaProvider, viaProviderSso) = WebPolicy(provider, Config());
			(await viaProvider.SecurityPolicy(EnableOnWeb(), CancellationToken.None)).Should().BeOfType<ViewResult>();
			viaProvider.ModelState[string.Empty]!.Errors.Single().ErrorMessage.Should().Be("SecurityPolicyFederatedNeedsResgridMfa");
			viaProviderSso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var (allowed, allowedSso) = WebPolicy(totp, Config());
			(await allowed.SecurityPolicy(EnableOnWeb(), CancellationToken.None)).Should().BeOfType<RedirectToActionResult>();
			allowedSso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p => p.AllowFederatedMfaForLoginMfa), UserId,
				It.IsAny<CancellationToken>()), Times.Once);
		}
	}
}
