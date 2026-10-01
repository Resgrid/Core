using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Sso;
using System.Linq;
using System.Security.Claims;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Web.Services
{
	[TestFixture]
	public class ConnectControllerSsoTests
	{
		private Mock<IDepartmentsService> _departmentsService;
		private Mock<ISystemAuditsService> _systemAuditsService;
		private Mock<IDepartmentSsoService> _ssoService;
		private Mock<IEncryptionService> _encryptionService;
		private Mock<ICacheProvider> _cacheProvider;
		private Mock<ISsoBrokerService> _broker;
		private ConnectController _controller;

		[SetUp]
		public void SetUp()
		{
			var department = new Department { DepartmentId = 42, Code = "DEPT" };
			_departmentsService = new Mock<IDepartmentsService>();
			_departmentsService.Setup(x => x.GetDepartmentByNameAsync("DEPT")).ReturnsAsync(department);
			_systemAuditsService = new Mock<ISystemAuditsService>();
			_systemAuditsService
				.Setup(x => x.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SystemAudit audit, CancellationToken _) => audit);
			_ssoService = new Mock<IDepartmentSsoService>();
			_encryptionService = new Mock<IEncryptionService>();
			_cacheProvider = new Mock<ICacheProvider>();
			_broker = new Mock<ISsoBrokerService>();

			var httpContext = new DefaultHttpContext();
			httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;

			_controller = new ConnectController(
				Mock.Of<IUsersService>(),
				Mock.Of<IUserProfileService>(),
				_departmentsService.Object,
				null,
				null,
				_systemAuditsService.Object,
				_ssoService.Object,
				_encryptionService.Object,
				_cacheProvider.Object,
				Mock.Of<IUserSessionService>(),
				Mock.Of<IExternalIdentityLinkService>(),
				Mock.Of<IMfaPolicyService>(),
				Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaLoginTransactionService>(), _broker.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = httpContext }
			};
		}

		[Test]
		public async Task SamlMobileCallback_StoresAssertionAndRedirectsWithRelayInsteadOfAssertion()
		{
			// Arrange
			_encryptionService.Setup(x => x.Encrypt("raw-saml-response")).Returns("encrypted-saml-response");
			_encryptionService.Setup(x => x.Encrypt("42:DEPT")).Returns("encrypted-department-token");
			_cacheProvider
				.Setup(x => x.SetStringAsync(
					It.Is<string>(key => key.StartsWith("Sso:SamlRelay:", StringComparison.Ordinal)),
					"encrypted-saml-response",
					It.Is<TimeSpan>(expiration => expiration == TimeSpan.FromMinutes(5))))
				.ReturnsAsync(true);

			// Act
			var result = await _controller.SamlMobileCallback(
				departmentToken: null, departmentCode: "DEPT", SAMLResponse: "raw-saml-response", RelayState: null, CancellationToken.None);

			// Assert
			var redirect = result.Should().BeOfType<RedirectResult>().Subject;
			var decodedLocation = Uri.UnescapeDataString(redirect.Url);
			decodedLocation.Should().Contain("saml_response=saml-relay:");
			decodedLocation.Should().NotContain("raw-saml-response");
			decodedLocation.Should().Contain("department_token=encrypted-department-token");

			// An untagged RelayState keeps the original target (Responder) and echoes nothing.
			redirect.Url.Should().StartWith("resgrid://auth/callback?");
			redirect.Url.Should().NotContain("relay_state");
			_controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
		}

		[TestCase("responder", "resgrid://auth/callback?")]
		[TestCase("unit", "resgridunit://auth/callback?")]
		[TestCase("dispatch", "resgriddispatch://auth/callback?")]
		[TestCase("ic", "resgridic://auth/callback?")]
		public async Task SamlMobileCallback_ReturnsToTheAppNamedInRelayState_AndEchoesIt(string client, string expectedTarget)
		{
			// Arrange
			SetUpRelayStorage();
			var relayState = $"{client}.3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13";

			// Act
			var result = await _controller.SamlMobileCallback(
				departmentToken: null, departmentCode: "DEPT", SAMLResponse: "raw-saml-response", RelayState: relayState, CancellationToken.None);

			// Assert
			var redirect = result.Should().BeOfType<RedirectResult>().Subject;
			redirect.Url.Should().StartWith(expectedTarget);
			redirect.Url.Should().EndWith($"&relay_state={relayState}");
			Uri.UnescapeDataString(redirect.Url).Should().Contain("department_token=encrypted-department-token");
		}

		[TestCase("")]
		[TestCase("3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13")]
		[TestCase("evil.3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13")]
		[TestCase("xunit.3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13")]
		[TestCase("a.ic.3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13")]
		[TestCase("unit.short")]
		[TestCase("unit.3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13&department_token=x")]
		[TestCase("unit.3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13\n")]
		[TestCase("UNIT.3f2b8c1e-5d7a-4e9b-8a61-0c4d2e7f9b13")]
		[TestCase("javascript:alert(1)")]
		[TestCase("https://evil.example/steal")]
		public async Task SamlMobileCallback_AnythingButAnAppRelayState_ReturnsToResponderAndEchoesNothing(string relayState)
		{
			// Arrange
			SetUpRelayStorage();

			// Act
			var result = await _controller.SamlMobileCallback(
				departmentToken: null, departmentCode: "DEPT", SAMLResponse: "raw-saml-response", RelayState: relayState, CancellationToken.None);

			// Assert
			var redirect = result.Should().BeOfType<RedirectResult>().Subject;
			redirect.Url.Should().StartWith("resgrid://auth/callback?");
			redirect.Url.Should().NotContain("relay_state");
		}

		[Test]
		public async Task SamlMobileCallback_AnOverlongRelayState_IsNotEchoed()
		{
			// Arrange: SAML bindings cap RelayState at 80 bytes.
			SetUpRelayStorage();
			var relayState = "unit." + new string('a', 64) + new string('b', 12);

			// Act
			var result = await _controller.SamlMobileCallback(
				departmentToken: null, departmentCode: "DEPT", SAMLResponse: "raw-saml-response", RelayState: relayState, CancellationToken.None);

			// Assert
			var redirect = result.Should().BeOfType<RedirectResult>().Subject;
			redirect.Url.Should().StartWith("resgrid://auth/callback?");
			redirect.Url.Should().NotContain("relay_state");
		}

		[TestCase("responder", "resgrid://auth/callback")]
		[TestCase("unit", "resgridunit://auth/callback")]
		[TestCase("Dispatch", "resgriddispatch://auth/callback")]
		[TestCase("ic", "resgridic://auth/callback")]
		[TestCase("command", "resgridic://auth/callback")]
		[TestCase(null, "resgrid://auth/callback")]
		[TestCase("web", "resgrid://auth/callback")]
		[TestCase("bogus", "resgrid://auth/callback")]
		public async Task SsoDiscovery_NamesTheCallingAppsOwnOidcRedirectUri(string client, string expected)
		{
			// Arrange
			_ssoService
				.Setup(x => x.GetSsoConfigsForDepartmentAsync(42, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new[] { new DepartmentSsoConfig { IsEnabled = true, SsoProviderType = (int)SsoProviderType.Oidc, Authority = "https://idp.example.com", ClientId = "client-1" } });
			if (client != null)
				_controller.HttpContext.Request.Headers["X-Resgrid-Client"] = client;

			// Act
			var result = await _controller.GetSsoConfig(departmentToken: null, departmentCode: "DEPT", CancellationToken.None);

			// Assert
			var body = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<GetDepartmentSsoConfigResult>().Subject;
			body.Data.OidcRedirectUri.Should().Be(expected);
		}

		private const string AppRelayState = "unit.0123456789abcdef";

		private DepartmentSsoConfig StartableSaml()
		{
			var config = new DepartmentSsoConfig { DepartmentSsoConfigId = "saml", IsEnabled = true, SsoProviderType = (int)SsoProviderType.Saml2, EntityId = "urn:dept" };
			_ssoService.Setup(x => x.GetSsoConfigForDepartmentAsync(42, SsoProviderType.Saml2, It.IsAny<CancellationToken>())).ReturnsAsync(config);
			_ssoService.Setup(x => x.GetSsoConfigsForDepartmentAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { config });
			return config;
		}

		[Test]
		public async Task SamlMobileLogin_SendsTheBrowserToTheIdpWithTheAppsOwnRelayState()
		{
			var config = StartableSaml();
			_broker.Setup(b => b.LegacySamlSignInUrl(config, "DEPT", AppRelayState, false)).Returns("https://idp.example.test/sso?SAMLRequest=abc&RelayState=" + AppRelayState);

			var result = await _controller.SamlMobileLogin(departmentToken: null, departmentCode: "DEPT", relayState: AppRelayState, CancellationToken.None);

			result.Should().BeOfType<RedirectResult>().Which.Url.Should().Be("https://idp.example.test/sso?SAMLRequest=abc&RelayState=" + AppRelayState);
			_controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("rgsso.0123456789abcdef", TestName = "SamlMobileLogin refuses the broker's own RelayState")]
		[TestCase("xunit.0123456789abcdef", TestName = "SamlMobileLogin refuses an unknown app")]
		[TestCase("unit.short", TestName = "SamlMobileLogin refuses a short nonce")]
		[TestCase("https://evil.example/", TestName = "SamlMobileLogin refuses a URL")]
		public async Task SamlMobileLogin_RefusesAnythingButAnAppsTaggedRelayState(string relayState)
		{
			var config = StartableSaml();
			_broker.Setup(b => b.LegacySamlSignInUrl(config, "DEPT", It.IsAny<string>(), It.IsAny<bool>())).Returns("https://idp.example.test/sso");

			var result = await _controller.SamlMobileLogin(departmentToken: null, departmentCode: "DEPT", relayState: relayState, CancellationToken.None);

			result.Should().BeOfType<BadRequestObjectResult>();
			_broker.Verify(b => b.LegacySamlSignInUrl(It.IsAny<DepartmentSsoConfig>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
		}

		[Test]
		public async Task SamlMobileLogin_RefusesADepartmentThatCannotStartOne()
		{
			(await _controller.SamlMobileLogin(null, "NOPE", AppRelayState, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>("an unknown department");

			(await _controller.SamlMobileLogin(null, "DEPT", AppRelayState, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>("no SAML configuration");

			var config = StartableSaml();
			(await _controller.SamlMobileLogin(null, "DEPT", AppRelayState, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>("the broker cannot start it");

			_broker.Setup(b => b.LegacySamlSignInUrl(config, "DEPT", AppRelayState, false)).Returns("https://idp.example.test/sso");
			config.IsEnabled = false;
			(await _controller.SamlMobileLogin(null, "DEPT", AppRelayState, CancellationToken.None)).Should().BeOfType<BadRequestObjectResult>("a disabled configuration");
		}

		[Test]
		public async Task SsoDiscovery_NamesTheSamlStartPage_WhereTheConfigurationCanStartOne()
		{
			var saved = Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl;
			try
			{
				Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = "https://api.resgrid.test/";
				var config = StartableSaml();
				_broker.Setup(b => b.DepartmentTokenFor(It.Is<Department>(d => d.DepartmentId == 42))).Returns("enc/token+=");
				_broker.Setup(b => b.SupportsBrokered(config)).Returns(true);

				var body = (GetDepartmentSsoConfigResult)((OkObjectResult)(await _controller.GetSsoConfig(null, "DEPT", CancellationToken.None)).Result).Value;
				body.Data.SamlLoginUrl.Should().Be("https://api.resgrid.test/api/v4/connect/saml-mobile-login?departmentToken=enc%2Ftoken%2B%3D");

				_broker.Setup(b => b.SupportsBrokered(config)).Returns(false);
				body = (GetDepartmentSsoConfigResult)((OkObjectResult)(await _controller.GetSsoConfig(null, "DEPT", CancellationToken.None)).Result).Value;
				body.Data.SamlLoginUrl.Should().BeNull("a configuration that cannot start a sign-in names no page");

				var oidc = new DepartmentSsoConfig { IsEnabled = true, SsoProviderType = (int)SsoProviderType.Oidc, Authority = "https://idp.example.com", ClientId = "c" };
				_ssoService.Setup(x => x.GetSsoConfigsForDepartmentAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { oidc });
				_broker.Setup(b => b.SupportsBrokered(oidc)).Returns(true);
				body = (GetDepartmentSsoConfigResult)((OkObjectResult)(await _controller.GetSsoConfig(null, "DEPT", CancellationToken.None)).Result).Value;
				body.Data.SamlLoginUrl.Should().BeNull();
			}
			finally
			{
				Resgrid.Config.SystemBehaviorConfig.ResgridApiBaseUrl = saved;
			}
		}

		[Test]
		public async Task SsoDiscovery_SamlHasNoOidcRedirectUri()
		{
			_ssoService
				.Setup(x => x.GetSsoConfigsForDepartmentAsync(42, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new[] { new DepartmentSsoConfig { IsEnabled = true, SsoProviderType = (int)SsoProviderType.Saml2, EntityId = "urn:dept" } });
			_controller.HttpContext.Request.Headers["X-Resgrid-Client"] = "unit";

			var result = await _controller.GetSsoConfig(departmentToken: null, departmentCode: "DEPT", CancellationToken.None);

			var body = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<GetDepartmentSsoConfigResult>().Subject;
			body.Data.OidcRedirectUri.Should().BeNull();
		}

		[Test]
		public void SsoAdminDetail_ListsEveryAppsOidcRedirectUris_ForOidcOnly()
		{
			var map = typeof(SsoAdminController).GetMethod("MapToDetail", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

			var saved = Resgrid.Config.SsoConfig.AppWebOrigins;
			SsoConfigDetailData oidc, saml;
			try
			{
				Resgrid.Config.SsoConfig.AppWebOrigins = "unit=https://unit.example.org;dispatch=https://dispatch.example.org";
				oidc = (SsoConfigDetailData)map.Invoke(null, new object[] { new DepartmentSsoConfig { SsoProviderType = (int)SsoProviderType.Oidc } });
				saml = (SsoConfigDetailData)map.Invoke(null, new object[] { new DepartmentSsoConfig { SsoProviderType = (int)SsoProviderType.Saml2 } });
			}
			finally
			{
				Resgrid.Config.SsoConfig.AppWebOrigins = saved;
			}

			oidc.OidcAppRedirectUris.Select(a => $"{a.Client}{(a.Web ? " web" : "")}={a.RedirectUri}").Should().Equal(
				"responder=resgrid://auth/callback", "unit=resgridunit://auth/callback", "unit web=https://unit.example.org/auth/callback",
				"dispatch=resgriddispatch://auth/callback", "dispatch web=https://dispatch.example.org/login/sso", "ic=resgridic://auth/callback");
			oidc.OidcAppRedirectUris.Should().OnlyContain(a => !string.IsNullOrEmpty(a.DisplayName));
			saml.OidcAppRedirectUris.Should().BeNull();
		}

		private void SetUpRelayStorage()
		{
			_encryptionService.Setup(x => x.Encrypt("raw-saml-response")).Returns("encrypted-saml-response");
			_encryptionService.Setup(x => x.Encrypt("42:DEPT")).Returns("encrypted-department-token");
			_cacheProvider
				.Setup(x => x.SetStringAsync(It.IsAny<string>(), "encrypted-saml-response", It.IsAny<TimeSpan>()))
				.ReturnsAsync(true);
		}

		[Test]
		public async Task ExternalToken_ConsumesRelayOnceAndValidatesStoredAssertion()
		{
			// Arrange
			var relayId = new string('A', 64);
			var relayToken = $"saml-relay:{relayId}";
			_cacheProvider
				.Setup(x => x.IncrementAsync($"Sso:SamlRelayUse:{relayId}", It.IsAny<TimeSpan>()))
				.ReturnsAsync(1);
			_cacheProvider
				.Setup(x => x.GetStringAsync($"Sso:SamlRelay:{relayId}"))
				.ReturnsAsync("encrypted-saml-response");
			_cacheProvider
				.Setup(x => x.RemoveAsync($"Sso:SamlRelay:{relayId}"))
				.ReturnsAsync(true);
			_encryptionService.Setup(x => x.Decrypt("encrypted-saml-response")).Returns("raw-saml-response");
			_ssoService
				.Setup(x => x.ValidateExternalTokenAsync(
					42, SsoProviderType.Saml2, "raw-saml-response", "DEPT", It.IsAny<CancellationToken>()))
				.ReturnsAsync((System.Security.Claims.ClaimsPrincipal)null);

			// Act
			var result = await _controller.ExternalToken(
				"saml2", relayToken, "DEPT", null, null, null, CancellationToken.None);

			// Assert
			result.Should().BeOfType<UnauthorizedObjectResult>();
			_ssoService.Verify(x => x.ValidateExternalTokenAsync(
				42, SsoProviderType.Saml2, "raw-saml-response", "DEPT", It.IsAny<CancellationToken>()), Times.Once);
			_cacheProvider.Verify(x => x.RemoveAsync($"Sso:SamlRelay:{relayId}"), Times.Once);
		}

		[Test]
		public async Task ExternalToken_DoesNotContinueASamlCodeRetryKeptForAnotherDepartment()
		{
			var relayId = new string('B', 64);
			_cacheProvider.Setup(x => x.IncrementAsync($"Sso:SamlRelayUse:{relayId}", It.IsAny<TimeSpan>())).ReturnsAsync(2);
			_cacheProvider.Setup(x => x.GetStringAsync($"Sso:SamlRelayMfa:{relayId}")).ReturnsAsync("kept");
			_encryptionService.Setup(x => x.Decrypt("kept")).Returns("user-1|7|sso-7|0");

			var result = await _controller.ExternalToken("saml2", $"saml-relay:{relayId}", "DEPT", null, null, "123456", CancellationToken.None);

			result.Should().BeOfType<UnauthorizedObjectResult>().Which.Value.Should().BeEquivalentTo(new
			{
				error = "invalid_grant", error_description = "The SAML relay token is invalid, expired, or has already been used."
			});
			_ssoService.Verify(x => x.GetSsoConfigForDepartmentAsync(It.IsAny<int>(), It.IsAny<SsoProviderType>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- Shared installations: a fresh provider sign-in (plan section 12.5.2) ----------------------------------------

		private void ValidatesTo(DateTime? providerSignIn)
		{
			var claims = new System.Collections.Generic.List<Claim> { new(ClaimTypes.NameIdentifier, "external-user") };
			if (providerSignIn != null)
				claims.Add(new Claim(ProviderSignInTime.ClaimType, ProviderSignInTime.ClaimValue(providerSignIn.Value), ClaimValueTypes.Integer64));
			_ssoService.Setup(x => x.ValidateExternalTokenAsync(42, SsoProviderType.Oidc, "id-token", "DEPT", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc")));
		}

		private Task<IActionResult> Exchange() => _controller.ExternalToken("oidc", "id-token", "DEPT", null, null, null, CancellationToken.None);

		private void ReachedLinking(Times times) => _ssoService.Verify(x => x.ProvisionOrLinkUserAsync(42, It.IsAny<ClaimsPrincipal>(),
			It.IsAny<DepartmentSsoConfig>(), "DEPT", It.IsAny<CancellationToken>()), times);

		[TestCase(-2 * 60 * 60, TestName = "ExternalToken refuses a shared installation's sign-in the provider answered from an old session")]
		[TestCase(null, TestName = "ExternalToken refuses a shared installation's sign-in that does not say when the provider authenticated")]
		[TestCase(30 * 60, TestName = "ExternalToken refuses a shared installation's sign-in dated in the future")]
		public async Task ExternalToken_RefusesASharedInstallationsProviderSignInThatIsNotFresh(int? ageSeconds)
		{
			_controller.ControllerContext.HttpContext.Request.Headers[SharedSessionRules.InstallationHeader] = "true";
			ValidatesTo(ageSeconds == null ? null : DateTime.UtcNow.AddSeconds(-ageSeconds.Value));

			var result = await Exchange();

			result.Should().BeOfType<UnauthorizedObjectResult>().Which.Value.ToString().Should().Contain("login_required");
			ReachedLinking(Times.Never());
		}

		[Test]
		public async Task ExternalToken_TreatsAnAppTheDepartmentRunsSharedAsASharedInstallation()
		{
			_controller.ControllerContext.HttpContext.Request.Headers["X-Resgrid-Client"] = "unit";
			_ssoService.Setup(x => x.GetSecurityPolicyForDepartmentAsync(42, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new DepartmentSecurityPolicy { DepartmentId = 42, SharedModeRequiredApps = (int)SharedModeApps.Unit });
			ValidatesTo(DateTime.UtcNow.AddHours(-2));

			(await Exchange()).Should().BeOfType<UnauthorizedObjectResult>().Which.Value.ToString().Should().Contain("login_required");
			ReachedLinking(Times.Never());
		}

		[Test]
		public async Task ExternalToken_LetsAFreshSharedSignInAndAnyPersonalOneThroughToLinking()
		{
			ValidatesTo(DateTime.UtcNow.AddHours(-2));
			(await Exchange()).Should().BeOfType<UnauthorizedObjectResult>().Which.Value.ToString().Should().NotContain("login_required",
				"a personal installation may use the provider's session, as before");
			ReachedLinking(Times.Once());

			_controller.ControllerContext.HttpContext.Request.Headers[SharedSessionRules.InstallationHeader] = "true";
			ValidatesTo(DateTime.UtcNow.AddSeconds(-20));
			(await Exchange()).Should().BeOfType<UnauthorizedObjectResult>().Which.Value.ToString().Should().NotContain("login_required");
			ReachedLinking(Times.Exactly(2));
		}
	}
}
