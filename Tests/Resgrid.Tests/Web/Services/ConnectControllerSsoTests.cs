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
				_cacheProvider.Object)
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
				departmentToken: null, departmentCode: "DEPT", SAMLResponse: "raw-saml-response", CancellationToken.None);

			// Assert
			var redirect = result.Should().BeOfType<RedirectResult>().Subject;
			var decodedLocation = Uri.UnescapeDataString(redirect.Url);
			decodedLocation.Should().Contain("saml_response=saml-relay:");
			decodedLocation.Should().NotContain("raw-saml-response");
			decodedLocation.Should().Contain("department_token=encrypted-department-token");
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
	}
}
