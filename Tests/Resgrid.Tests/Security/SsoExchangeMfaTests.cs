using System;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.v4;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// The API SSO exchange's second factor (passkey plan section 7.6 row 4): RequireMfa without enrollment is its own
	/// actionable error, and wrong codes count toward the account lockout shared with every other TOTP surface.
	/// </summary>
	[TestFixture]
	public class SsoExchangeMfaTests
	{
		private const int DepartmentId = 42;

		private IdentityUser _user;
		private Mock<UserManager<IdentityUser>> _users;
		private Mock<IMfaPolicyService> _mfaPolicy;
		private Mock<ISsoBrokerService> _broker;
		private ConnectController _controller;

		[SetUp]
		public void SetUp()
		{
			_user = new IdentityUser { Id = "user-1", UserName = "user1", AuthenticationGeneration = 4 };
			_users = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			_users.Setup(m => m.IsLockedOutAsync(_user)).ReturnsAsync(false);

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByNameAsync("DEPT")).ReturnsAsync(new Department { DepartmentId = DepartmentId, Code = "DEPT" });
			var sso = new Mock<IDepartmentSsoService>();
			sso.Setup(s => s.ValidateExternalTokenAsync(DepartmentId, SsoProviderType.Oidc, "id-token", "DEPT", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity("oidc")));
			sso.Setup(s => s.ProvisionOrLinkUserAsync(DepartmentId, It.IsAny<ClaimsPrincipal>(), It.IsAny<DepartmentSsoConfig>(), "DEPT", It.IsAny<CancellationToken>()))
				.ReturnsAsync(_user);
			var audits = new Mock<ISystemAuditsService>();
			audits.Setup(a => a.SaveSystemAuditAsync(It.IsAny<SystemAudit>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((SystemAudit audit, CancellationToken _) => audit);
			_mfaPolicy = new Mock<IMfaPolicyService>();

			sso.Setup(s => s.ValidateExternalTokenAsync(DepartmentId, SsoProviderType.Oidc, IdToken, "DEPT", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity("oidc")));
			_broker = new Mock<ISsoBrokerService>();
			var signIn = new Mock<SignInManager<IdentityUser>>(_users.Object, Mock.Of<IHttpContextAccessor>(),
				Mock.Of<IUserClaimsPrincipalFactory<IdentityUser>>(), null, null, null, null);
			signIn.Setup(s => s.CreateUserPrincipalAsync(_user)).ReturnsAsync(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", _user.Id) }, "test")));

			var http = new DefaultHttpContext();
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			_controller = new ConnectController(Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(), departments.Object, signIn.Object, _users.Object,
				audits.Object, sso.Object, Mock.Of<IEncryptionService>(), Mock.Of<ICacheProvider>(), Mock.Of<IUserSessionService>(),
				Mock.Of<IExternalIdentityLinkService>(), _mfaPolicy.Object, Mock.Of<IMfaEvidenceService>(),
				Mock.Of<IMfaLoginTransactionService>(), _broker.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		/// <summary>A well-formed (unsigned) id_token; the IdP validation is mocked, the replay record reads its expiry.</summary>
		private static readonly string IdToken = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(
			new System.IdentityModel.Tokens.Jwt.JwtSecurityToken("https://idp.example.test/", "resgrid-client", new[] { new Claim("sub", "external-user") },
				DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(9)));

		[Test]
		public async Task A_legacy_id_token_is_recorded_only_when_it_completes_a_sign_in()
		{
			var tracking = Resgrid.Config.SessionSecurityConfig.TrackingEnabled;
			Resgrid.Config.SessionSecurityConfig.TrackingEnabled = false;
			try
			{
				_users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(true);
				_users.Setup(m => m.VerifyTwoFactorTokenAsync(_user, It.IsAny<string>(), "123456")).ReturnsAsync(true);
				_broker.Setup(b => b.TryRecordIdTokenUseAsync(IdToken, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

				Error(await _controller.ExternalToken("oidc", IdToken, "DEPT", null, null, null, CancellationToken.None)).Should().Be("mfa_required");
				_broker.Verify(b => b.TryRecordIdTokenUseAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never,
					"an older build resends the same id_token with its TOTP code after mfa_required");

				(await _controller.ExternalToken("oidc", IdToken, "DEPT", null, null, "123456", CancellationToken.None)).Should().BeOfType<Microsoft.AspNetCore.Mvc.SignInResult>();
				_broker.Verify(b => b.TryRecordIdTokenUseAsync(IdToken,
					It.Is<DateTime>(exp => Math.Abs((exp - DateTime.UtcNow.AddMinutes(9)).TotalSeconds) < 5), It.IsAny<CancellationToken>()), Times.Once);
			}
			finally
			{
				Resgrid.Config.SessionSecurityConfig.TrackingEnabled = tracking;
			}
		}

		[Test]
		public async Task A_legacy_id_token_that_already_signed_in_is_refused()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(true);
			_users.Setup(m => m.VerifyTwoFactorTokenAsync(_user, It.IsAny<string>(), "123456")).ReturnsAsync(true);
			_broker.Setup(b => b.TryRecordIdTokenUseAsync(IdToken, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			Error(await _controller.ExternalToken("oidc", IdToken, "DEPT", null, null, "123456", CancellationToken.None)).Should().Be("invalid_grant");
		}

		private static string Error(IActionResult result) =>
			JObject.FromObject(result.Should().BeOfType<UnauthorizedObjectResult>().Subject.Value).Value<string>("error");

		[Test]
		public async Task An_unenrolled_member_of_a_require_mfa_department_is_told_to_enroll()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(false);
			_mfaPolicy.Setup(p => p.DepartmentRequiresMfaAsync(DepartmentId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			var result = await _controller.ExternalToken("oidc", "id-token", "DEPT", null, null, null, CancellationToken.None);

			Error(result).Should().Be("mfa_enrollment_required");
		}

		[Test]
		public async Task A_wrong_code_counts_toward_the_account_lockout()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(true);
			_users.Setup(m => m.VerifyTwoFactorTokenAsync(_user, It.IsAny<string>(), "000000")).ReturnsAsync(false);

			var result = await _controller.ExternalToken("oidc", "id-token", "DEPT", null, null, "000000", CancellationToken.None);

			Error(result).Should().Be("invalid_totp");
			_users.Verify(m => m.AccessFailedAsync(_user), Times.Once);
		}

		[Test]
		public async Task A_locked_out_account_gets_no_more_guesses_through_sso()
		{
			_users.Setup(m => m.GetTwoFactorEnabledAsync(_user)).ReturnsAsync(true);
			_users.Setup(m => m.IsLockedOutAsync(_user)).ReturnsAsync(true);
			_users.Setup(m => m.VerifyTwoFactorTokenAsync(_user, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

			var result = await _controller.ExternalToken("oidc", "id-token", "DEPT", null, null, "123456", CancellationToken.None);

			Error(result).Should().Be("invalid_totp");
			_users.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}
	}
}
