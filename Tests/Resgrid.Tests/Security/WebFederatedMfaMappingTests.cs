using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
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
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Security;
using Resgrid.Web.Attributes;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The Web provider step-up mapping page (passkey workbook section 12, slice 20; plan section 7.8): what counts as the identity
	/// provider's MFA is the managing member's decision, needs a Resgrid factor to change, is validated before it is stored,
	/// advances the tested version, and is audited.
	/// </summary>
	[TestFixture]
	public class WebFederatedMfaMappingTests
	{
		private const string Manager = "manager-1";
		private const string OtherAdmin = "admin-2";
		private const int DepartmentId = 42;
		private const string SessionId = "session-9";
		private const string OidcMapping = "{\"requestAcrValues\":[\"mfa-level\"],\"acceptAmr\":[\"mfa\",\"otp\"]}";

		private sealed class Harness
		{
			public readonly Mock<IDepartmentSsoService> Sso = new();
			public readonly Mock<ISystemAuditsService> Audits = new();
			public DepartmentSsoConfig Active;
			public MfaEvidence Latest = new() { VerifiedOnUtc = DateTime.UtcNow, Method = (int)MfaEvidenceMethod.Totp };
			public bool ProviderGate = true;
			public readonly SecurityController Controller;

			public Harness(string userId = Manager, DepartmentSsoConfig active = null, bool noConfig = false)
			{
				Active = noConfig ? null : active ?? Config(OidcMapping, version: 3, tested: 3);
				var http = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(new[]
					{
						new Claim(ClaimTypes.PrimarySid, userId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
						new Claim(ClaimTypes.Name, "manager"), new Claim(SessionClaimTypes.SessionId, SessionId),
						new Claim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update)
					}, "test"))
				};
				http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
				Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };

				var departments = new Mock<IDepartmentsService>();
				departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "DEPT", ManagingUserId = Manager, AdminUsers = new List<string> { OtherAdmin } });
				Sso.Setup(s => s.GetSsoConfigsForDepartmentAsync(DepartmentId, It.IsAny<CancellationToken>()))
					.ReturnsAsync(() => Active == null
						? new List<DepartmentSsoConfig> { new() { DepartmentSsoConfigId = "off", IsEnabled = false } }
						: new List<DepartmentSsoConfig> { new() { DepartmentSsoConfigId = "off", IsEnabled = false }, Active });
				Sso.Setup(s => s.SaveSsoConfigAsync(It.IsAny<DepartmentSsoConfig>(), "DEPT", It.IsAny<CancellationToken>()))
					.ReturnsAsync((DepartmentSsoConfig c, string _, CancellationToken _) =>
					{
						c.FederatedMfaMappingVersion++;
						c.FederatedMfaTestedVersion = null;
						return c;
					});

				var strings = new Mock<Microsoft.Extensions.Localization.IStringLocalizer<Resgrid.Localization.Areas.User.Security.Security>>();
				strings.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new Microsoft.Extensions.Localization.LocalizedString(key, key));
				strings.Setup(l => l["FederatedMfaInvalid"]).Returns(new Microsoft.Extensions.Localization.LocalizedString("FederatedMfaInvalid", "invalid: {0}"));
				var user = new IdentityUser { Id = userId, AuthenticationGeneration = 4 };
				var users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
				users.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
				var evidence = new Mock<IMfaEvidenceService>();
				evidence.Setup(e => e.GetLatestSecondFactorAsync(userId, "sid:" + SessionId, 4, It.IsAny<CancellationToken>())).ReturnsAsync(() => Latest);
				var policy = new Mock<IMfaPolicyService>();
				policy.Setup(p => p.IsEvidenceAcceptedAsync(It.IsAny<int?>(), It.IsAny<MfaMethodScope>(), It.IsAny<MfaEvidence>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(true);
				var gates = new Mock<IPasskeyFeatureGates>();
				gates.SetupGet(g => g.ProviderStepUpEnabled).Returns(() => ProviderGate);

				Controller = new SecurityController(departments.Object, Mock.Of<IAuditService>(), Mock.Of<IPermissionsService>(),
					Mock.Of<IEventAggregator>(), Mock.Of<IDepartmentSettingsService>(), Audits.Object, users.Object, strings.Object, Sso.Object,
					Mock.Of<IEncryptionService>(), Mock.Of<IRecordsCutoverService>(), gates.Object, evidence.Object, policy.Object, Mock.Of<IDepartmentApiKeysService>())
				{
					ControllerContext = new ControllerContext { HttpContext = http },
					TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>())
				};
			}

			public void VerifyNothingSaved()
			{
				Sso.Verify(s => s.SaveSsoConfigAsync(It.IsAny<DepartmentSsoConfig>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
				Audits.Verify(a => a.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()), Times.Never);
			}

			public string Error() => Controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage;
		}

		private static DepartmentSsoConfig Config(string mapping, long version, long? tested, SsoProviderType type = SsoProviderType.Oidc) => new()
		{
			DepartmentSsoConfigId = "cfg", DepartmentId = DepartmentId, SsoProviderType = (int)type, IsEnabled = true,
			FederatedMfaMappingJson = mapping == null ? null : FederatedMfaMapping.Parse(mapping).Serialize(),
			FederatedMfaMappingVersion = version, FederatedMfaTestedVersion = tested, FederatedMfaTestedOnUtc = tested == null ? null : new DateTime(2026, 9, 28, 10, 0, 0)
		};

		private static FederatedMfaEditView Form(string acceptAmr = "mfa\r\notp\r\n", string requestAcr = "mfa-level") => new()
		{
			RequestAcrValues = requestAcr, AcceptAmr = acceptAmr
		};

		[Test]
		public async Task The_page_shows_the_active_mapping_as_lines_with_its_version_and_test()
		{
			var harness = new Harness();

			var view = (await harness.Controller.FederatedMfa(CancellationToken.None)).Should().BeOfType<ViewResult>().Subject;
			var model = view.Model.Should().BeOfType<FederatedMfaEditView>().Subject;

			model.HasActiveSsoConfig.Should().BeTrue("the disabled configuration is ignored; the enabled one is shown");
			model.IsOidc.Should().BeTrue();
			model.AcceptAmr.Should().Be("mfa\notp");
			model.RequestAcrValues.Should().Be("mfa-level");
			model.HasMapping.Should().BeTrue();
			model.MappingVersion.Should().Be(3);
			model.Effective.Should().BeTrue();
			model.TestedOnUtc.Should().Be(new DateTime(2026, 9, 28, 10, 0, 0));
			model.CanChange.Should().BeTrue();
			model.ProviderStepUpAvailable.Should().BeTrue();

			var untested = new Harness(active: Config(OidcMapping, version: 4, tested: 3));
			var untestedModel = (FederatedMfaEditView)((ViewResult)await untested.Controller.FederatedMfa(CancellationToken.None)).Model;
			untestedModel.Effective.Should().BeFalse("version 4 has not passed its own test");
			untestedModel.TestedOnUtc.Should().BeNull("an older version's test says nothing about this one");

			var other = new Harness(OtherAdmin);
			((FederatedMfaEditView)((ViewResult)await other.Controller.FederatedMfa(CancellationToken.None)).Model).CanChange.Should().BeFalse();
		}

		[Test]
		public async Task Without_an_enabled_sso_configuration_nothing_can_be_saved()
		{
			var harness = new Harness(noConfig: true);

			var model = (FederatedMfaEditView)((ViewResult)await harness.Controller.FederatedMfa(CancellationToken.None)).Model;
			model.HasActiveSsoConfig.Should().BeFalse();
			model.HasMapping.Should().BeFalse();

			(await harness.Controller.FederatedMfa(Form(), "save", CancellationToken.None)).Should().BeOfType<ViewResult>();
			harness.VerifyNothingSaved();
		}

		[Test]
		public async Task Only_the_managing_member_changes_it_whatever_the_form_claims()
		{
			var harness = new Harness(OtherAdmin);
			var form = Form();
			form.CanChange = true;

			(await harness.Controller.FederatedMfa(form, "save", CancellationToken.None)).Should().BeOfType<ViewResult>();

			harness.Error().Should().Be("SecurityPolicyMfaMethodsManagingMemberOnly");
			form.CanChange.Should().BeFalse("who may change it comes from the department, never the form");
			harness.VerifyNothingSaved();
		}

		[Test]
		public async Task Provider_step_up_cannot_approve_a_change_to_what_counts_as_provider_step_up()
		{
			var harness = new Harness();
			harness.Latest = new MfaEvidence { VerifiedOnUtc = DateTime.UtcNow, Method = (int)MfaEvidenceMethod.Federated };

			(await harness.Controller.FederatedMfa(Form(), "save", CancellationToken.None)).Should().BeOfType<ViewResult>();
			harness.Error().Should().Be("FederatedMfaNeedsResgridMfa");

			(await harness.Controller.FederatedMfa(Form(), "remove", CancellationToken.None)).Should().BeOfType<ViewResult>();
			harness.VerifyNothingSaved();
		}

		[Test]
		public async Task A_valid_mapping_is_stored_normalized_needs_a_new_test_and_is_audited()
		{
			var harness = new Harness();

			var form = Form(acceptAmr: " mfa \r\n\r\nhwk\r\n", requestAcr: "urn:okta:loa:2fa:any");
			form.RequestClaims = "  \r\n";
			var result = await harness.Controller.FederatedMfa(form, "save", CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("FederatedMfa");
			harness.Controller.TempData["FederatedMfaSuccess"].Should().Be("FederatedMfaSaved");
			harness.Sso.Verify(s => s.SaveSsoConfigAsync(It.Is<DepartmentSsoConfig>(c => c.DepartmentSsoConfigId == "cfg" && c.UpdatedByUserId == Manager),
				"DEPT", It.IsAny<CancellationToken>()), Times.Once);
			var stored = FederatedMfaMapping.Parse(harness.Active.FederatedMfaMappingJson);
			stored.AcceptAmr.Should().Equal("mfa", "hwk");
			stored.RequestAcrValues.Should().Equal("urn:okta:loa:2fa:any");
			stored.AcceptAcr.Should().BeNull("an empty list is not stored");
			stored.RequestClaims.Should().BeNull("a blank claims request is no claims request");
			harness.Active.FederatedMfaMappingVersion.Should().Be(4);
			FederatedMfaMapping.IsTested(harness.Active).Should().BeFalse("the new version counts only after its own test");
			harness.Audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.FederatedMfaMappingChanged &&
				x.System == (int)SystemAuditSystems.Website && x.UserId == Manager && x.Successful && x.Data.Contains("version 4")),
				It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task An_invalid_mapping_is_refused_with_the_reason_and_nothing_is_stored()
		{
			var passwordAsMfa = new Harness();
			(await passwordAsMfa.Controller.FederatedMfa(Form(acceptAmr: "pwd"), "save", CancellationToken.None)).Should().BeOfType<ViewResult>();
			passwordAsMfa.Error().Should().Be("invalid: acceptAmr cannot accept \"pwd\": a password is not MFA.");
			passwordAsMfa.VerifyNothingSaved();

			var nothingAccepted = new Harness();
			(await nothingAccepted.Controller.FederatedMfa(Form(acceptAmr: "\r\n  \r\n"), "save", CancellationToken.None)).Should().BeOfType<ViewResult>();
			nothingAccepted.Error().Should().Be("invalid: Name at least one amr, acr or acrs value that counts as MFA.");
			nothingAccepted.VerifyNothingSaved();

			var badClaims = new Harness();
			var form = Form();
			form.RequestClaims = "{\"access_token\":{}}";
			(await badClaims.Controller.FederatedMfa(form, "save", CancellationToken.None)).Should().BeOfType<ViewResult>();
			badClaims.VerifyNothingSaved();

			var samlValuesOnOidc = new Harness();
			form = Form();
			form.AcceptAuthnContextClassRefs = "https://refeds.org/profile/mfa";
			(await samlValuesOnOidc.Controller.FederatedMfa(form, "save", CancellationToken.None)).Should().BeOfType<ViewResult>();
			samlValuesOnOidc.VerifyNothingSaved();
		}

		[Test]
		public async Task A_saml_mapping_uses_authn_context_values()
		{
			var harness = new Harness(active: Config(null, version: 0, tested: null, type: SsoProviderType.Saml2));

			var model = (FederatedMfaEditView)((ViewResult)await harness.Controller.FederatedMfa(CancellationToken.None)).Model;
			model.IsOidc.Should().BeFalse();
			model.HasMapping.Should().BeFalse();

			var result = await harness.Controller.FederatedMfa(new FederatedMfaEditView
			{
				RequestAuthnContextClassRefs = "https://refeds.org/profile/mfa", AcceptAuthnContextClassRefs = "https://refeds.org/profile/mfa"
			}, "save", CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			FederatedMfaMapping.Parse(harness.Active.FederatedMfaMappingJson).AcceptAuthnContextClassRefs.Should().Equal("https://refeds.org/profile/mfa");
		}

		[Test]
		public async Task Removing_the_mapping_stores_none_and_is_audited()
		{
			var harness = new Harness();

			var result = await harness.Controller.FederatedMfa(Form(), "remove", CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			harness.Controller.TempData["FederatedMfaSuccess"].Should().Be("FederatedMfaRemoved");
			harness.Active.FederatedMfaMappingJson.Should().BeNull("remove ignores whatever the form still holds");
			harness.Audits.Verify(a => a.SaveSystemAuditAsync(It.Is<SystemAudit>(x => x.Type == (int)SystemAuditTypes.FederatedMfaMappingChanged &&
				x.Data.Contains("removed")), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Saving_the_same_mapping_changes_nothing_and_keeps_its_test()
		{
			var harness = new Harness();

			var result = await harness.Controller.FederatedMfa(Form(), "save", CancellationToken.None);

			result.Should().BeOfType<RedirectToActionResult>();
			harness.VerifyNothingSaved();
			FederatedMfaMapping.IsTested(harness.Active).Should().BeTrue();
		}

		[Test]
		public void The_page_is_a_five_minute_security_change_with_antiforgery_on_the_post()
		{
			foreach (var action in typeof(SecurityController).GetMethods().Where(m => m.Name == nameof(SecurityController.FederatedMfa)))
			{
				var guard = action.GetCustomAttribute<RequiresRecentTwoFactorAttribute>();
				guard.Should().NotBeNull();
				guard.RequireForOperation.Should().BeTrue();
				guard.VerificationWindowMinutes.Should().Be(5);
				guard.MethodScope.Should().Be(MfaMethodScope.SecurityChange);
			}
			typeof(SecurityController).GetMethods().Single(m => m.Name == nameof(SecurityController.FederatedMfa) && m.GetParameters().Length == 3)
				.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
		}

		[Test]
		public void Guards_name_the_rows_they_serve()
		{
			// Row 14: recovery codes are account factor management, never approval or provider step-up.
			foreach (var name in new[] { "ViewRecoveryCodes", "RegenerateRecoveryCodes" })
				typeof(TwoFactorController).GetMethod(name)!.GetCustomAttribute<RequiresRecentTwoFactorAttribute>()!.MethodScope
					.Should().Be(MfaMethodScope.Account, name);

			// Row 13: permissions are a security change, and both of their commands are guarded.
			foreach (var name in new[] { "SetPermission", "SetPermissionData" })
				typeof(SecurityController).GetMethod(name)!.GetCustomAttribute<RequiresRecentTwoFactorAttribute>()!.MethodScope
					.Should().Be(MfaMethodScope.SecurityChange, name);
		}

		[Test]
		public void The_form_round_trips_a_mapping_one_value_per_line()
		{
			var model = new FederatedMfaEditView();
			model.CopyFrom(FederatedMfaMapping.Parse("{\"acceptAcrs\":[\"c1\",\"c2\"],\"requestClaims\":\"{\\\"id_token\\\":{}}\"}"));

			model.AcceptAcrs.Should().Be("c1\nc2");
			model.RequestClaims.Should().Be("{\"id_token\":{}}");
			model.AcceptAmr.Should().BeNull();

			var mapping = model.ToMapping();
			mapping.AcceptAcrs.Should().Equal("c1", "c2");
			mapping.RequestClaims.Should().Be("{\"id_token\":{}}");
			mapping.AcceptAmr.Should().BeNull();

			new FederatedMfaEditView { AcceptAmr = "mfa\r\nmfa" }.ToMapping().AcceptAmr.Should().Equal(new[] { "mfa", "mfa" },
				"duplicates are kept so validation can refuse them rather than silently merging");
		}
	}
}
