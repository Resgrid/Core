using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Security;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 6 (section 10.1): the department's second-factor method switches, what their changes
	/// advance, which methods each scope accepts, and who may change them.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class SecurityPolicySwitchTests
	{
		private const int DepartmentId = 42;
		private const string Manager = "manager-1";
		private const string OtherAdmin = "admin-2";

		// ---- What a change advances --------------------------------------------------------------------------------

		private static readonly (string Name, Action<DepartmentSecurityPolicy> Change, bool MfaVersion, bool AdpEpoch)[] Changes =
		{
			("RequireMfa", p => p.RequireMfa = true, true, false),
			("AllowPasskeysForLoginMfa", p => p.AllowPasskeysForLoginMfa = false, true, false),
			("AllowFederatedMfaForLoginMfa", p => p.AllowFederatedMfaForLoginMfa = true, true, false),
			("AllowResponderApproval", p => p.AllowResponderApproval = false, true, true),
			("AllowPasskeysForAdp", p => p.AllowPasskeysForAdp = false, false, true),
			("AllowFederatedMfaForAdp", p => p.AllowFederatedMfaForAdp = true, false, true),
			("AcceptRecentLoginMfaForAdp", p => p.AcceptRecentLoginMfaForAdp = false, false, true),
			("AcceptRecentUnlockMfaForAdp", p => p.AcceptRecentUnlockMfaForAdp = false, false, true),
			("SessionTimeoutMinutes", p => p.SessionTimeoutMinutes = 30, false, false),
			("RequireSso", p => p.RequireSso = true, false, false)
		};

		[TestCaseSource(nameof(Changes))]
		public void Each_switch_advances_exactly_what_the_plan_says((string Name, Action<DepartmentSecurityPolicy> Change, bool MfaVersion, bool AdpEpoch) change)
		{
			var before = new DepartmentSecurityPolicy();
			var after = new DepartmentSecurityPolicy();
			change.Change(after);

			DepartmentSecurityPolicyDecisions.MfaPolicyChanged(before, after).Should().Be(change.MfaVersion, change.Name);
			DepartmentSecurityPolicyDecisions.AdpMethodPolicyChanged(before, after).Should().Be(change.AdpEpoch, change.Name);
		}

		[Test]
		public void A_department_without_a_policy_row_has_the_documented_defaults()
		{
			var defaults = new DepartmentSecurityPolicy();

			defaults.RequireMfa.Should().BeFalse();
			defaults.AllowPasskeysForLoginMfa.Should().BeTrue();
			defaults.AllowPasskeysForAdp.Should().BeTrue();
			defaults.AllowFederatedMfaForLoginMfa.Should().BeFalse();
			defaults.AllowFederatedMfaForAdp.Should().BeFalse();
			defaults.AllowResponderApproval.Should().BeTrue();
			defaults.AcceptRecentLoginMfaForAdp.Should().BeTrue();
			defaults.AcceptRecentUnlockMfaForAdp.Should().BeTrue();
			defaults.SharedIdleLockMinutes.Should().Be(5, "plan section 10.5 defaults");
			defaults.SharedShiftHours.Should().Be(12);
			defaults.SharedModeRequiredApps.Should().Be(0, "no app is shared unless the department says so");
		}

		// ---- Which methods each scope accepts ----------------------------------------------------------------------

		private static IPasskeyFeatureGates Gates(bool on = true, bool? shared = null)
		{
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(on);
			gates.SetupGet(g => g.AdpAcceptanceEnabled).Returns(on);
			gates.SetupGet(g => g.ResponderApprovalEnabled).Returns(on);
			gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(on);
			gates.SetupGet(g => g.SharedDeviceModeEnabled).Returns(shared ?? on);
			return gates.Object;
		}

		[Test]
		public void With_every_gate_off_only_totp_is_accepted_anywhere()
		{
			var permissive = new DepartmentSecurityPolicy { AllowFederatedMfaForLoginMfa = true, AllowFederatedMfaForAdp = true };

			foreach (var scope in Enum.GetValues<MfaMethodScope>())
				MfaPolicyService.AllowedMethods(permissive, scope, Gates(on: false)).Should().Equal(MfaMethodNames.Totp);
		}

		[Test]
		public void Each_scope_follows_its_own_switches()
		{
			var policy = new DepartmentSecurityPolicy { AllowFederatedMfaForLoginMfa = true, AllowFederatedMfaForAdp = true };

			MfaPolicyService.AllowedMethods(policy, MfaMethodScope.Login, Gates())
				.Should().Equal(MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval, MfaMethodNames.Federated);
			MfaPolicyService.AllowedMethods(policy, MfaMethodScope.SecurityChange, Gates())
				.Should().Equal(new[] { MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.Federated }, "never Responder approval for security changes");
			MfaPolicyService.AllowedMethods(policy, MfaMethodScope.Account, Gates())
				.Should().Equal(new[] { MfaMethodNames.Totp, MfaMethodNames.Passkey }, "account factors use no approval or provider step-up");
			MfaPolicyService.AllowedMethods(policy, MfaMethodScope.Adp, Gates())
				.Should().Equal(MfaMethodNames.Totp, MfaMethodNames.Passkey, MfaMethodNames.PasskeyApproval, MfaMethodNames.Federated);
		}

		[Test]
		public void Turning_passkeys_off_for_a_row_also_turns_off_approval_there_but_never_totp()
		{
			var loginOff = new DepartmentSecurityPolicy { AllowPasskeysForLoginMfa = false };

			MfaPolicyService.AllowedMethods(loginOff, MfaMethodScope.Login, Gates()).Should().Equal(MfaMethodNames.Totp);
			MfaPolicyService.AllowedMethods(loginOff, MfaMethodScope.Adp, Gates())
				.Should().Contain(MfaMethodNames.Passkey, "the ADP switch is separate");
			MfaPolicyService.AllowedMethods(loginOff, MfaMethodScope.Account, Gates())
				.Should().Contain(MfaMethodNames.Passkey, "account factor management ignores department switches");
		}

		[Test]
		public void Provider_step_up_needs_its_switch_as_well_as_the_gate()
		{
			MfaPolicyService.AllowedMethods(new DepartmentSecurityPolicy(), MfaMethodScope.Login, Gates())
				.Should().NotContain(MfaMethodNames.Federated, "it is off by default");
		}

		private static MfaPolicyService Policy(DepartmentSecurityPolicy stored, out Mock<IDepartmentSsoService> sso)
		{
			sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
			return new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), Gates());
		}

		[Test]
		public async Task Evidence_of_a_method_the_department_switched_off_no_longer_counts()
		{
			var service = Policy(new DepartmentSecurityPolicy { AllowPasskeysForLoginMfa = false }, out var sso);

			(await service.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Passkey)).Should().BeFalse();
			(await service.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Adp, MfaEvidenceMethod.Passkey)).Should().BeTrue();
			(await service.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Totp)).Should().BeTrue();
			(await service.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.RecoveryCode)).Should().BeFalse();

			sso.Invocations.Clear();
			await service.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Totp);
			sso.Invocations.Should().BeEmpty("TOTP is always accepted, without a policy read");
		}

		[Test]
		public async Task Api_step_up_with_a_passkey_the_department_switched_off_does_not_authorize_a_security_change()
		{
			var service = Policy(new DepartmentSecurityPolicy { AllowPasskeysForLoginMfa = false }, out _);
			var http = new DefaultHttpContext();
			http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext { SessionId = "s", AuthenticationGeneration = 1 };
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.GetLatestSecondFactorAsync("user-1", "sid:s", 1, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaEvidence { VerifiedOnUtc = DateTime.UtcNow, Method = (int)MfaEvidenceMethod.Passkey });

			(await Resgrid.Web.Services.Helpers.ApiStepUpEvidence.HasRecentSecondFactorAsync(evidence.Object, service, "user-1", http, DepartmentId,
				MfaMethodScope.SecurityChange, TimeSpan.FromMinutes(5))).Should().BeFalse("the user verifies again with an accepted method");
			(await Resgrid.Web.Services.Helpers.ApiStepUpEvidence.HasRecentSecondFactorAsync(evidence.Object, service, "user-1", http, DepartmentId,
				MfaMethodScope.Adp, TimeSpan.FromMinutes(5))).Should().BeTrue("the ADP switch is still on");
		}

		[Test]
		public async Task A_failed_policy_read_is_never_mistaken_for_the_permissive_defaults()
		{
			var service = Policy(null, out var sso);
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			var check = () => service.IsMethodAcceptedAsync(DepartmentId, MfaMethodScope.Login, MfaEvidenceMethod.Passkey);

			await check.Should().ThrowAsync<TimeoutException>();
		}

		// ---- Saving: atomic with the version and epoch it advances -------------------------------------------------

		private sealed class SaveHarness
		{
			public readonly List<string> Steps = new();
			public readonly Mock<IUnitOfWork> Unit = new();
			public readonly Mock<IDepartmentSecurityPolicyRepository> Policies = new();
			public readonly Mock<IDepartmentDataProtectionPolicyRepository> Adp = new();
			public readonly Mock<IDepartmentDataProtectionService> AdpService = new();
			public readonly Mock<IAuditLogsRepository> Audits = new();
			public DepartmentSsoService Service;

			public SaveHarness(DepartmentSecurityPolicy stored)
			{
				Policies.Setup(p => p.GetByDepartmentIdForUpdateAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
				Policies.Setup(p => p.SaveOrUpdateAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.Callback(() => Steps.Add("save")).ReturnsAsync((DepartmentSecurityPolicy p, CancellationToken _, bool _) => p);
				Policies.Setup(p => p.IncrementMfaPolicyVersionAsync(DepartmentId, It.IsAny<CancellationToken>()))
					.Callback(() => Steps.Add("mfa-version")).ReturnsAsync(8);
				Adp.Setup(a => a.IncrementPolicyEpochAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.Callback(() => Steps.Add("adp-epoch")).ReturnsAsync(5);
				Unit.Setup(u => u.CommitChanges()).Callback(() => Steps.Add("commit"));
				Unit.Setup(u => u.DiscardChanges()).Callback(() => Steps.Add("discard"));
				AdpService.Setup(a => a.InvalidateProtectionCacheAsync(DepartmentId)).Callback(() => Steps.Add("invalidate")).Returns(Task.CompletedTask);
				Audits.Setup(a => a.SaveOrUpdateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.Callback(() => Steps.Add("audit")).ReturnsAsync((AuditLog a, CancellationToken _, bool _) => a);
				Service = new DepartmentSsoService(Mock.Of<IDepartmentSsoConfigRepository>(), Policies.Object, Mock.Of<IDepartmentMembersRepository>(),
					Mock.Of<IDepartmentsService>(), Mock.Of<IUserProfileService>(), Mock.Of<IEncryptionService>(), Mock.Of<ICacheProvider>(),
					Mock.Of<IExternalIdentityLinkService>(), Mock.Of<ILimitsService>(), Unit.Object, Adp.Object,
					new Lazy<IDepartmentDataProtectionService>(() => AdpService.Object), Mock.Of<IUserSessionMfaEvidenceRepository>(), Audits.Object);
			}
		}

		[Test]
		public async Task Turning_adp_passkeys_off_revokes_grants_in_the_same_transaction_and_clears_the_cache_after_commit()
		{
			var harness = new SaveHarness(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, MfaPolicyVersion = 7 });

			var saved = await harness.Service.SaveSecurityPolicyAsync(
				new DepartmentSecurityPolicy { DepartmentId = DepartmentId, AllowPasskeysForAdp = false }, Manager);

			harness.Steps.Should().Equal("save", "adp-epoch", "audit", "commit", "invalidate");
			harness.Adp.Verify(a => a.IncrementPolicyEpochAsync(DepartmentId, Manager, It.IsAny<CancellationToken>()), Times.Once);
			saved.MfaPolicyVersion.Should().Be(7, "a grant-only switch leaves the sign-in rules' version alone");
		}

		[Test]
		public async Task Responder_approval_advances_both_the_mfa_version_and_the_adp_epoch()
		{
			var harness = new SaveHarness(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, MfaPolicyVersion = 7 });

			var saved = await harness.Service.SaveSecurityPolicyAsync(
				new DepartmentSecurityPolicy { DepartmentId = DepartmentId, AllowResponderApproval = false, MfaPolicyVersion = 1 }, Manager);

			harness.Steps.Should().Equal("save", "mfa-version", "adp-epoch", "audit", "commit", "invalidate");
			saved.MfaPolicyVersion.Should().Be(8, "the version is the server's, never what the caller sent");
		}

		[Test]
		public async Task An_unrelated_change_advances_nothing()
		{
			var harness = new SaveHarness(new DepartmentSecurityPolicy { DepartmentId = DepartmentId });

			await harness.Service.SaveSecurityPolicyAsync(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, SessionTimeoutMinutes = 60 }, Manager);

			harness.Steps.Should().Equal("save", "audit", "commit");
		}

		[Test]
		public async Task Every_change_is_audited_in_the_same_transaction_with_who_and_what()
		{
			var harness = new SaveHarness(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, DepartmentSecurityPolicyId = 12, MfaPolicyVersion = 7 });
			AuditLog audit = null;
			harness.Audits.Setup(a => a.SaveOrUpdateAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback((AuditLog a, CancellationToken _, bool _) => { audit = a; harness.Steps.Add("audit"); })
				.ReturnsAsync((AuditLog a, CancellationToken _, bool _) => a);

			await harness.Service.SaveSecurityPolicyAsync(new DepartmentSecurityPolicy
			{
				DepartmentId = DepartmentId, DepartmentSecurityPolicyId = 12, AllowResponderApproval = false
			}, Manager);

			harness.Steps.Should().Equal("save", "mfa-version", "adp-epoch", "audit", "commit", "invalidate");
			audit.LogType.Should().Be((int)AuditLogTypes.DepartmentSecurityPolicyChanged);
			audit.DepartmentId.Should().Be(DepartmentId);
			audit.UserId.Should().Be(Manager);
			audit.ObjectId.Should().Be("12");
			using var data = System.Text.Json.JsonDocument.Parse(audit.Data);
			data.RootElement.GetProperty("before").GetProperty("AllowResponderApproval").GetBoolean().Should().BeTrue();
			data.RootElement.GetProperty("after").GetProperty("AllowResponderApproval").GetBoolean().Should().BeFalse();
			data.RootElement.GetProperty("after").GetProperty("SessionTimeoutMinutes").GetInt32().Should().Be(0);
			data.RootElement.GetProperty("mfaPolicyVersion").GetInt64().Should().Be(8);
			data.RootElement.GetProperty("adpEpochAdvanced").GetBoolean().Should().BeTrue();
		}

		[Test]
		public async Task Saving_the_same_policy_writes_no_audit()
		{
			var stored = new DepartmentSecurityPolicy { DepartmentId = DepartmentId, RequireMfa = true, AllowedIpRanges = "10.0.0.0/8", SharedShiftHours = 8 };
			var harness = new SaveHarness(stored);

			await harness.Service.SaveSecurityPolicyAsync(new DepartmentSecurityPolicy
			{
				DepartmentId = DepartmentId, RequireMfa = true, AllowedIpRanges = "10.0.0.0/8", SharedShiftHours = 8
			}, Manager);

			harness.Steps.Should().Equal("save", "commit");
		}

		[Test]
		public async Task A_first_policy_is_compared_with_the_defaults()
		{
			var unchanged = new SaveHarness(stored: null);
			await unchanged.Service.SaveSecurityPolicyAsync(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, MinPasswordLength = 12 }, Manager);
			unchanged.Steps.Should().Equal("save", "audit", "commit");

			var requiring = new SaveHarness(stored: null);
			await requiring.Service.SaveSecurityPolicyAsync(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, RequireMfa = true }, Manager);
			requiring.Steps.Should().Equal("save", "mfa-version", "audit", "commit");
		}

		[Test]
		public async Task A_failure_rolls_everything_back_and_clears_nothing()
		{
			var harness = new SaveHarness(new DepartmentSecurityPolicy { DepartmentId = DepartmentId });
			harness.Adp.Setup(a => a.IncrementPolicyEpochAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());

			var save = () => harness.Service.SaveSecurityPolicyAsync(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, AllowPasskeysForAdp = false }, Manager);

			await save.Should().ThrowAsync<TimeoutException>();
			harness.Steps.Should().Equal("save", "discard");
		}

		// ---- Who may change them -----------------------------------------------------------------------------------

		private static (Resgrid.Web.Services.Controllers.v4.SsoAdminController Controller, Mock<IDepartmentSsoService> Sso) Api(
			string userId, DepartmentSecurityPolicy stored, bool sharedGate = true)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext { SessionId = "s", AuthenticationGeneration = 1 };
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = Manager, AdminUsers = new List<string> { OtherAdmin } });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
			sso.Setup(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentSecurityPolicy p, string _, CancellationToken _) => p);
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.GetLatestSecondFactorAsync(userId, "sid:s", 1, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new MfaEvidence { VerifiedOnUtc = DateTime.UtcNow, Method = (int)MfaEvidenceMethod.Totp });
			var controller = new Resgrid.Web.Services.Controllers.v4.SsoAdminController(sso.Object, departments.Object, Mock.Of<IPermissionsService>(),
				Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), evidence.Object,
				new MfaPolicyService(sso.Object, new InMemoryUserMfaStateRepository(), Gates()), Mock.Of<ISsoBrokerService>(), Mock.Of<ISystemAuditsService>(),
				Gates(shared: sharedGate))
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
			return (controller, sso);
		}

		private IHttpContextAccessor _previousApiAccessor;

		[SetUp]
		public void SetUp() => _previousApiAccessor = ApiClaims._httpContextAccessor;

		[TearDown]
		public void TearDown()
		{
			ApiClaims._httpContextAccessor = _previousApiAccessor;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		}

		[Test]
		public async Task Only_the_managing_member_changes_the_method_switches_through_the_api()
		{
			var (controller, sso) = Api(OtherAdmin, new DepartmentSecurityPolicy { DepartmentId = DepartmentId });

			var refused = await controller.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { AllowPasskeysForAdp = false },
				CancellationToken.None);

			((ProblemDetails)((ObjectResult)((IConvertToActionResult)refused).Convert()).Value).Type.Should().Be("managing_member_required");
			sso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_client_that_does_not_send_the_switches_leaves_them_as_they_are()
		{
			var (controller, sso) = Api(OtherAdmin, new DepartmentSecurityPolicy { DepartmentId = DepartmentId, AllowPasskeysForAdp = false });

			await controller.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { SessionTimeoutMinutes = 30 }, CancellationToken.None);

			sso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p =>
				!p.AllowPasskeysForAdp && p.AllowPasskeysForLoginMfa && p.SessionTimeoutMinutes == 30), OtherAdmin, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task The_managing_member_changes_them_and_is_recorded_as_the_actor()
		{
			var (controller, sso) = Api(Manager, new DepartmentSecurityPolicy { DepartmentId = DepartmentId });

			await controller.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { AllowPasskeysForLoginMfa = false },
				CancellationToken.None);

			sso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p => !p.AllowPasskeysForLoginMfa), Manager, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task On_the_web_page_another_administrators_posted_switches_are_ignored()
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, OtherAdmin), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = Manager });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<DepartmentSsoConfig>());
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSecurityPolicy { DepartmentId = DepartmentId, DepartmentSecurityPolicyId = 3 });
			var strings = new Mock<Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security>>();
			strings.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new Microsoft.Extensions.Localization.LocalizedString(key, key));
			var controller = new SecurityController(departments.Object, Mock.Of<IAuditService>(), Mock.Of<IPermissionsService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<ISystemAuditsService>(), null, strings.Object, sso.Object, Mock.Of<IEncryptionService>(),
				Mock.Of<IRecordsCutoverService>(), Gates(), Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>())
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
			};

			// A disabled checkbox still posts its hidden "false"; a forged CanChangeMethodSwitches changes nothing either.
			await controller.SecurityPolicy(new SecurityPolicyEditView
			{
				MinPasswordLength = 12, AllowPasskeysForLoginMfa = false, AllowPasskeysForAdp = false, AllowResponderApproval = false,
				AcceptRecentLoginMfaForAdp = false, AcceptRecentUnlockMfaForAdp = false, CanChangeMethodSwitches = true
			}, CancellationToken.None);

			sso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p =>
				p.AllowPasskeysForLoginMfa && p.AllowPasskeysForAdp && p.AllowResponderApproval && p.AcceptRecentLoginMfaForAdp &&
				p.AcceptRecentUnlockMfaForAdp && p.MinPasswordLength == 12), OtherAdmin, It.IsAny<CancellationToken>()), Times.Once);
		}

		// ---- Shared-device policy (passkey plan section 10.5) --------------------------------------------------------

		private static string ProblemType(IConvertToActionResult result) =>
			result.Convert() is ObjectResult { Value: ProblemDetails problem } ? problem.Type : null;

		[Test]
		public async Task Only_the_managing_member_changes_the_shared_device_policy_through_the_api()
		{
			var (other, otherSso) = Api(OtherAdmin, new DepartmentSecurityPolicy { DepartmentId = DepartmentId });
			ProblemType(await other.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { SharedIdleLockMinutes = 3 },
				CancellationToken.None)).Should().Be("managing_member_required");
			otherSso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var (manager, sso) = Api(Manager, new DepartmentSecurityPolicy { DepartmentId = DepartmentId });
			await manager.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput
			{
				SharedIdleLockMinutes = 3, SharedShiftHours = 10, SharedModeRequiredApps = (int)(SharedModeApps.Unit | SharedModeApps.Dispatch)
			}, CancellationToken.None);
			sso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p =>
				p.SharedIdleLockMinutes == 3 && p.SharedShiftHours == 10 && p.SharedModeRequiredApps == 5), Manager, It.IsAny<CancellationToken>()), Times.Once);

			var (untouched, untouchedSso) = Api(OtherAdmin, new DepartmentSecurityPolicy { DepartmentId = DepartmentId, SharedIdleLockMinutes = 2 });
			await untouched.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { SessionTimeoutMinutes = 30 }, CancellationToken.None);
			untouchedSso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p => p.SharedIdleLockMinutes == 2), OtherAdmin,
				It.IsAny<CancellationToken>()), Times.Once, "a client that does not send the shared values leaves them as they are");
		}

		[Test]
		public async Task The_api_refuses_shared_values_out_of_range_and_new_requirements_the_deployment_cannot_serve()
		{
			foreach (var input in new[]
			{
				new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { SharedIdleLockMinutes = 16 },
				new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { SharedIdleLockMinutes = 0 },
				new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { SharedShiftHours = 25 },
				new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { SharedModeRequiredApps = 8 }
			})
			{
				var (controller, sso) = Api(Manager, new DepartmentSecurityPolicy { DepartmentId = DepartmentId });
				ProblemType(await controller.SaveSecurityPolicy(input, CancellationToken.None)).Should().Be("invalid_request");
				sso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
			}

			var stored = new DepartmentSecurityPolicy { DepartmentId = DepartmentId, SharedModeRequiredApps = (int)SharedModeApps.Unit };
			var (gatedOff, gatedSso) = Api(Manager, stored, sharedGate: false);
			ProblemType(await gatedOff.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput
				{ SharedModeRequiredApps = (int)(SharedModeApps.Unit | SharedModeApps.Command) }, CancellationToken.None)).Should().Be("shared_mode_unavailable");
			gatedSso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var (clearing, clearingSso) = Api(Manager, new DepartmentSecurityPolicy { DepartmentId = DepartmentId, SharedModeRequiredApps = (int)SharedModeApps.Unit },
				sharedGate: false);
			await clearing.SaveSecurityPolicy(new Resgrid.Web.Services.Models.v4.Sso.SaveSecurityPolicyInput { SharedModeRequiredApps = 0, SharedIdleLockMinutes = 2 },
				CancellationToken.None);
			clearingSso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p => p.SharedModeRequiredApps == 0 && p.SharedIdleLockMinutes == 2),
				Manager, It.IsAny<CancellationToken>()), Times.Once, "removing a requirement or tightening a timer is allowed with the gate off");
		}

		private static (SecurityController Controller, Mock<IDepartmentSsoService> Sso) WebPage(string userId, DepartmentSecurityPolicy stored, bool sharedGate)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
				}, "test"))
			};
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, ManagingUserId = Manager });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<DepartmentSsoConfig>());
			sso.Setup(s => s.GetSecurityPolicyForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(stored);
			var strings = new Mock<Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security>>();
			strings.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new Microsoft.Extensions.Localization.LocalizedString(key, key));
			var controller = new SecurityController(departments.Object, Mock.Of<IAuditService>(), Mock.Of<IPermissionsService>(), Mock.Of<IEventAggregator>(),
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<ISystemAuditsService>(), null, strings.Object, sso.Object, Mock.Of<IEncryptionService>(),
				Mock.Of<IRecordsCutoverService>(), Gates(shared: sharedGate), Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>())
			{
				ControllerContext = new ControllerContext { HttpContext = http },
				TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
			};
			return (controller, sso);
		}

		[Test]
		public async Task On_the_web_page_the_managing_member_sets_the_shared_policy_and_other_administrators_cannot()
		{
			SecurityPolicyEditView Posted() => new()
			{
				MinPasswordLength = 12, SharedIdleLockMinutes = 3, SharedShiftHours = 8, RequireSharedModeForUnit = true, RequireSharedModeForDispatch = true,
				CanChangeMethodSwitches = true
			};

			var (manager, sso) = WebPage(Manager, new DepartmentSecurityPolicy { DepartmentId = DepartmentId, DepartmentSecurityPolicyId = 3 }, sharedGate: true);
			await manager.SecurityPolicy(Posted(), CancellationToken.None);
			sso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p =>
				p.SharedIdleLockMinutes == 3 && p.SharedShiftHours == 8 && p.SharedModeRequiredApps == (int)(SharedModeApps.Unit | SharedModeApps.Dispatch)),
				Manager, It.IsAny<CancellationToken>()), Times.Once);

			var (other, otherSso) = WebPage(OtherAdmin, new DepartmentSecurityPolicy { DepartmentId = DepartmentId, DepartmentSecurityPolicyId = 3 }, sharedGate: true);
			await other.SecurityPolicy(Posted(), CancellationToken.None);
			otherSso.Verify(s => s.SaveSecurityPolicyAsync(It.Is<DepartmentSecurityPolicy>(p =>
				p.SharedIdleLockMinutes == 5 && p.SharedShiftHours == 12 && p.SharedModeRequiredApps == 0), OtherAdmin, It.IsAny<CancellationToken>()), Times.Once);

			var (gated, gatedSso) = WebPage(Manager, new DepartmentSecurityPolicy { DepartmentId = DepartmentId, DepartmentSecurityPolicyId = 3 }, sharedGate: false);
			await gated.SecurityPolicy(Posted(), CancellationToken.None);
			gated.ModelState.IsValid.Should().BeFalse("a new requirement needs the deployment's shared-device mode");
			gatedSso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

			var outOfRange = Posted();
			outOfRange.SharedIdleLockMinutes = 20;
			var (ranged, rangedSso) = WebPage(Manager, new DepartmentSecurityPolicy { DepartmentId = DepartmentId, DepartmentSecurityPolicyId = 3 }, sharedGate: true);
			await ranged.SecurityPolicy(outOfRange, CancellationToken.None);
			ranged.ModelState.ContainsKey("SharedIdleLockMinutes").Should().BeTrue();
			rangedSso.Verify(s => s.SaveSecurityPolicyAsync(It.IsAny<DepartmentSecurityPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}
	}
}
