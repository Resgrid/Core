using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Localization;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Web.Services.Controllers.v4;
using Resgrid.Web.Services.Models.v4.Mfa;
using Resgrid.Web.Services.Models.v4.Passkeys;
using ApiClaims = Resgrid.Web.ServicesCore.Helpers.ClaimsAuthorizationHelper;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;
using TwoFactorController = Resgrid.Web.Areas.User.Controllers.TwoFactorController;
using Disable2FAViewModel = Resgrid.Web.Areas.User.Models.TwoFactor.Disable2FAViewModel;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Passkey plan Phase 1, slice 7: the v4 passkey, account-methods and step-up endpoints, and the Web rule that TOTP
	/// cannot be turned off while passkeys exist.
	/// </summary>
	[TestFixture, NonParallelizable]
	public class PasskeyApiTests
	{
		private const string UserId = "user-1";
		private const int DepartmentId = 42;
		private const string SessionId = "session-9";

		private IHttpContextAccessor _previousAccessor;

		[SetUp]
		public void SetUp() => _previousAccessor = ApiClaims._httpContextAccessor;

		[TearDown]
		public void TearDown() => ApiClaims._httpContextAccessor = _previousAccessor;

		private static IdentityUser User() => new() { Id = UserId, UserName = "user1", AuthenticationGeneration = 4 };

		private static Mock<UserManager<IdentityUser>> UserManager(IdentityUser user, bool enrolled = true, int codesLeft = 10)
		{
			var manager = new Mock<UserManager<IdentityUser>>(Mock.Of<IUserStore<IdentityUser>>(), null, null, null, null, null, null, null, null);
			manager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
			manager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(user);
			manager.Setup(m => m.GetTwoFactorEnabledAsync(user)).ReturnsAsync(enrolled);
			manager.Setup(m => m.CountRecoveryCodesAsync(user)).ReturnsAsync(codesLeft);
			manager.Setup(m => m.IsLockedOutAsync(user)).ReturnsAsync(false);
			return manager;
		}

		private static DefaultHttpContext ApiContext(bool withSession = true)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, UserId), new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString()),
					new Claim(ClaimTypes.Name, "user1")
				}, "test"))
			};
			http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
			if (withSession)
				http.Items[ProtectedGrantSessionContext.HttpItemKey] = new ProtectedGrantSessionContext
				{
					SessionId = SessionId, ClientApplication = (int)UserSessionClientApplication.Unit, AuthenticationGeneration = 4
				};
			var accessor = new Mock<IHttpContextAccessor>();
			accessor.Setup(a => a.HttpContext).Returns(http);
			ApiClaims._httpContextAccessor = accessor.Object;
			return http;
		}

		private static ProblemDetails Problem(IConvertToActionResult result) => (ProblemDetails)((ObjectResult)result.Convert()).Value;

		private static int? Status(IConvertToActionResult result) => ((ObjectResult)result.Convert()).StatusCode;

		private static UserPasskey Passkey(string id, UserSessionClientApplication client) => new()
		{
			UserPasskeyId = id, UserId = UserId, ClientApplication = (int)client, DisplayName = id, CreatedOnUtc = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc),
			RegistrationPlatform = "iOS", IsBackupEligible = true, IsBackedUp = true
		};

		// ---- Mfa/StepUpOptions and Mfa/VerifyStepUp with a passkey ---------------------------------------------------

		private static (MfaController Controller, Mock<IPasskeyService> Passkeys, Mock<IMfaEvidenceService> Evidence, Mock<UserManager<IdentityUser>> Users)
			Mfa(bool passkeysAccepted = true)
		{
			var user = User();
			var users = UserManager(user);
			var gates = new Mock<IPasskeyFeatureGates>();
			gates.SetupGet(g => g.LoginAcceptanceEnabled).Returns(passkeysAccepted);
			var policy = new MfaPolicyService(Mock.Of<IDepartmentSsoService>(), new InMemoryUserMfaStateRepository(), gates.Object);
			var passkeys = new Mock<IPasskeyService>();
			passkeys.Setup(p => p.HasActiveForClientAsync(UserId, UserSessionClientApplication.Unit, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			passkeys.Setup(p => p.BeginAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.SensitiveOperation, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = "req-1", OptionsJson = "{\"challenge\":\"abc\"}" });
			var evidence = new Mock<IMfaEvidenceService>();
			var cache = new Mock<ICacheProvider>();
			cache.Setup(c => c.IncrementAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(1);
			var controller = new MfaController(users.Object, policy, evidence.Object, cache.Object, Mock.Of<ISystemAuditsService>(), passkeys.Object,
				Mock.Of<IDepartmentSsoService>(), Mock.Of<ISsoBrokerService>(), Mock.Of<IMfaApprovalService>(), Mock.Of<IMfaActivityService>())
			{
				ControllerContext = new ControllerContext { HttpContext = ApiContext() }
			};
			return (controller, passkeys, evidence, users);
		}

		private static VerifyStepUpInput PasskeyInput(string operation = MfaStepUpOperations.AccountSecurity) => new()
		{
			Operation = operation, Method = MfaMethodNames.Passkey, RequestId = "req-1", Credential = JObject.Parse("{\"id\":\"abc\",\"type\":\"public-key\"}")
		};

		[Test]
		public async Task Step_up_options_offer_this_apps_passkey_ceremony_when_it_is_usable()
		{
			var (controller, passkeys, _, _) = Mfa();

			var data = (await controller.StepUpOptions(MfaStepUpOperations.AccountSecurity, CancellationToken.None)).Value.Data;

			data.Methods.Should().Equal(MfaMethodNames.Totp, MfaMethodNames.Passkey);
			data.Passkey.RequestId.Should().Be("req-1");
			data.Passkey.Options.ToString().Should().Be("{\"challenge\":\"abc\"}");
			passkeys.Verify(p => p.BeginAssertionAsync(It.Is<PasskeyCaller>(c => c.SessionId == SessionId && c.ClientApplication == UserSessionClientApplication.Unit
				&& c.AuthenticationGeneration == 4 && c.DepartmentId == DepartmentId), AuthenticationChallengePurpose.SensitiveOperation, It.IsAny<CancellationToken>()));

			var (off, offPasskeys, _, _) = Mfa(passkeysAccepted: false);
			var offData = (await off.StepUpOptions(MfaStepUpOperations.AccountSecurity, CancellationToken.None)).Value.Data;
			offData.Methods.Should().Equal(MfaMethodNames.Totp);
			offData.Passkey.Should().BeNull();
			offPasskeys.Verify(p => p.BeginAssertionAsync(It.IsAny<PasskeyCaller>(), It.IsAny<AuthenticationChallengePurpose>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_passkey_step_up_becomes_evidence_naming_the_passkey()
		{
			var (controller, passkeys, evidence, users) = Mfa();
			var verifiedAt = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
			passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), AuthenticationChallengePurpose.SensitiveOperation, "req-1",
					"{\"id\":\"abc\",\"type\":\"public-key\"}", It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyAssertionResult { Outcome = PasskeyOutcome.Succeeded, Passkey = Passkey("pk-1", UserSessionClientApplication.Unit), VerifiedOnUtc = verifiedAt });

			var result = await controller.VerifyStepUp(PasskeyInput(), CancellationToken.None);

			result.Value.Data.VerifiedAt.Should().Be(verifiedAt.ToString("O"));
			evidence.Verify(e => e.RecordAsync(UserId, "sid:" + SessionId, UserSessionClientApplication.Unit, MfaEvidenceKind.SecondFactor,
				MfaEvidenceMethod.Passkey, MfaEvidencePurpose.StepUp, verifiedAt, 4, DepartmentId, "passkey:pk-1", It.IsAny<CancellationToken>()), Times.Once);
			users.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task A_failed_passkey_step_up_uses_the_shared_vocabulary_and_records_nothing()
		{
			var (controller, passkeys, evidence, users) = Mfa();
			passkeys.Setup(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), It.IsAny<AuthenticationChallengePurpose>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(PasskeyAssertionResult.Of(PasskeyOutcome.VerificationFailed));

			var result = await controller.VerifyStepUp(PasskeyInput(), CancellationToken.None);

			Problem(result).Type.Should().Be("passkey_verification_failed");
			Status(result).Should().Be(StatusCodes.Status401Unauthorized);
			users.Verify(m => m.AccessFailedAsync(It.IsAny<IdentityUser>()), Times.Never);
			evidence.Verify(e => e.RecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UserSessionClientApplication>(), It.IsAny<MfaEvidenceKind>(),
				It.IsAny<MfaEvidenceMethod>(), It.IsAny<MfaEvidencePurpose>(), It.IsAny<DateTime>(), It.IsAny<long>(), It.IsAny<int?>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task A_passkey_step_up_is_refused_where_passkeys_are_not_accepted()
		{
			var (controller, passkeys, _, _) = Mfa(passkeysAccepted: false);

			Problem(await controller.VerifyStepUp(PasskeyInput(), CancellationToken.None)).Type.Should().Be("mfa_method_not_allowed");
			passkeys.Verify(p => p.CompleteAssertionAsync(It.IsAny<PasskeyCaller>(), It.IsAny<AuthenticationChallengePurpose>(), It.IsAny<string>(),
				It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		// ---- Passkeys/* ----------------------------------------------------------------------------------------------

		private static (PasskeysController Controller, Mock<IPasskeyService> Passkeys) Passkeys(bool withSession = true)
		{
			var passkeys = new Mock<IPasskeyService>();
			var controller = new PasskeysController(UserManager(User(), codesLeft: 7).Object, passkeys.Object)
			{
				ControllerContext = new ControllerContext { HttpContext = ApiContext(withSession) }
			};
			return (controller, passkeys);
		}

		[Test]
		public async Task Registration_options_pass_the_validated_session_and_the_users_factor_state()
		{
			var (controller, passkeys) = Passkeys();
			passkeys.Setup(p => p.BeginRegistrationAsync(It.IsAny<PasskeyCaller>(), true, 7, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = "req-2", OptionsJson = "{\"rp\":{}}" });

			var result = await controller.RegistrationOptions(CancellationToken.None);

			result.Value.Data.RequestId.Should().Be("req-2");
			passkeys.Verify(p => p.BeginRegistrationAsync(It.Is<PasskeyCaller>(c => c.UserId == UserId && c.UserName == "user1" && c.SessionId == SessionId
				&& c.ClientApplication == UserSessionClientApplication.Unit && c.AuditSystem == SystemAuditSystems.Api), true, 7, It.IsAny<CancellationToken>()));
		}

		[TestCase(PasskeyOutcome.StepUpRequired, "step_up_required", StatusCodes.Status401Unauthorized)]
		[TestCase(PasskeyOutcome.ReauthenticationRequired, "reauthentication_required", StatusCodes.Status401Unauthorized)]
		[TestCase(PasskeyOutcome.EnrollmentRequired, "mfa_enrollment_required", StatusCodes.Status409Conflict)]
		[TestCase(PasskeyOutcome.SessionRequired, "session_required", StatusCodes.Status409Conflict)]
		[TestCase(PasskeyOutcome.Unavailable, "passkeys_unavailable", StatusCodes.Status400BadRequest)]
		[TestCase(PasskeyOutcome.TooManyRequests, "too_many_attempts", StatusCodes.Status429TooManyRequests)]
		[TestCase(PasskeyOutcome.ServiceUnavailable, "service_unavailable", StatusCodes.Status503ServiceUnavailable)]
		public async Task Refusals_map_to_the_shared_vocabulary(PasskeyOutcome outcome, string type, int status)
		{
			var (controller, passkeys) = Passkeys();
			passkeys.Setup(p => p.BeginRegistrationAsync(It.IsAny<PasskeyCaller>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasskeyCeremonyStart.Of(outcome));

			var result = await controller.RegistrationOptions(CancellationToken.None);

			Problem(result).Type.Should().Be(type);
			Status(result).Should().Be(status);
		}

		[Test]
		public async Task Management_commands_name_the_app_and_passkey_they_act_on()
		{
			var (controller, passkeys) = Passkeys();
			passkeys.Setup(p => p.RevokeAllForClientAsync(It.IsAny<PasskeyCaller>(), UserSessionClientApplication.Command, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new PasskeyRevocationResult { Outcome = PasskeyOutcome.Succeeded, Revoked = 3, SessionsEnded = 2, CurrentSessionEnded = true });
			passkeys.Setup(p => p.RevokeAsync(It.IsAny<PasskeyCaller>(), "pk-9", It.IsAny<CancellationToken>()))
				.ReturnsAsync(PasskeyRevocationResult.Of(PasskeyOutcome.NotFound));

			var all = (await controller.RevokeAllForClient(new RevokeAllPasskeysForClientInput { Client = "ic" }, CancellationToken.None)).Value.Data;
			all.Revoked.Should().Be(3);
			all.SessionsEnded.Should().Be(2);
			all.CurrentSessionEnded.Should().BeTrue("the client must sign in again");
			Problem(await controller.RevokeAllForClient(new RevokeAllPasskeysForClientInput { Client = "api" }, CancellationToken.None)).Type.Should().Be("invalid_request");
			var missing = await controller.Revoke(new RevokePasskeyInput { PasskeyId = "pk-9" }, CancellationToken.None);
			Problem(missing).Type.Should().Be("passkey_not_found");
			Status(missing).Should().Be(StatusCodes.Status404NotFound);
		}

		[Test]
		public async Task The_inventory_labels_each_passkey_with_its_app_and_approval_only_for_responder()
		{
			var (controller, passkeys) = Passkeys();
			passkeys.Setup(p => p.GetActiveForUserAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<UserPasskey>
			{
				Passkey("pk-unit", UserSessionClientApplication.Unit), Passkey("pk-responder", UserSessionClientApplication.Responder)
			});

			var data = (await controller.List(CancellationToken.None)).Value.Data;

			data.Select(d => d.Client).Should().Equal("unit", "responder");
			data[0].ApprovalEnabled.Should().BeNull();
			data[1].ApprovalEnabled.Should().BeFalse();
			data[0].CreatedOn.Should().Be("2026-09-28T12:00:00.0000000Z");
		}

		[Test]
		public async Task Account_methods_group_passkeys_by_app_and_block_turning_totp_off_while_any_exist()
		{
			var user = User();
			var passkeys = new Mock<IPasskeyService>();
			passkeys.Setup(p => p.GetActiveForUserAsync(UserId, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new List<UserPasskey> { Passkey("pk-unit", UserSessionClientApplication.Unit) });
			passkeys.Setup(p => p.IsRegistrationAvailable(UserSessionClientApplication.Unit)).Returns(true);
			var state = new InMemoryUserMfaStateRepository();
			await state.TryConsumeTotpTimeStepAsync(UserId, 100, new DateTime(2026, 9, 27, 8, 0, 0, DateTimeKind.Utc));
			var controller = new AccountSecurityController(UserManager(user, codesLeft: 2).Object, passkeys.Object, state, Mock.Of<IUserStore<IdentityUser>>(),
				Mock.Of<IMfaEvidenceService>(), Mock.Of<IMfaPolicyService>(), Mock.Of<IUserSessionService>(), Mock.Of<IExternalIdentityLinkService>(),
				Mock.Of<ISecurityNoticeService>(), Mock.Of<ISystemAuditsService>(), Mock.Of<IUserSessionsRepository>(), Mock.Of<IMfaActivityService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IDepartmentSsoService>())
			{
				ControllerContext = new ControllerContext { HttpContext = ApiContext() }
			};

			var data = (await controller.Methods(CancellationToken.None)).Value.Data;

			data.CurrentClient.Should().Be("unit");
			data.Totp.Enrolled.Should().BeTrue();
			data.Totp.RecoveryCodesRemaining.Should().Be(2);
			data.Totp.RecoveryCodeWarning.Should().BeTrue();
			data.Totp.LastUsedOn.Should().Be("2026-09-27T08:00:00.0000000Z");
			data.Totp.CanTurnOff.Should().BeFalse("TOTP cannot be turned off while a passkey exists");
			data.PasskeyGroups.Select(g => g.Client).Should().Equal("web", "responder", "unit", "dispatch", "ic");
			data.PasskeyGroups.Single(g => g.Client == "unit").Passkeys.Should().ContainSingle().Which.PasskeyId.Should().Be("pk-unit");
			data.PasskeyGroups.Single(g => g.Client == "unit").RegistrationAvailable.Should().BeTrue();
			data.PasskeyGroups.Where(g => g.Client != "unit").Should().OnlyContain(g => g.Passkeys.Count == 0 && !g.RegistrationAvailable);
		}

		// ---- Web: TOTP cannot be turned off while passkeys exist -----------------------------------------------------

		[Test]
		public async Task Web_refuses_to_turn_off_totp_while_passkeys_exist()
		{
			var user = User();
			var users = UserManager(user);
			users.Setup(m => m.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
			var evidence = new Mock<IMfaEvidenceService>();
			evidence.Setup(e => e.HasFreshFirstFactorAsync(UserId, It.IsAny<string>(), 4, It.IsAny<TimeSpan>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(true);
			var passkeyRows = new InMemoryUserPasskeyRepository();
			passkeyRows.Rows.Add(Passkey("pk-unit", UserSessionClientApplication.Unit));
			var localizer = new Mock<IStringLocalizer<Resgrid.Localization.Areas.User.TwoFactor.TwoFactor>>();
			localizer.Setup(l => l[It.IsAny<string>()]).Returns((string key) => new LocalizedString(key, key));
			var controller = new TwoFactorController(users.Object, null, Mock.Of<ISystemAuditsService>(), UrlEncoder.Default, localizer.Object,
				Mock.Of<IUserStore<IdentityUser>>(), new InMemoryUserMfaStateRepository(), Mock.Of<IUserSessionService>(), evidence.Object,
				Mock.Of<IMfaPolicyService>(), passkeyRows, Mock.Of<ISecurityNoticeService>(), Mock.Of<IMfaActivityService>(), Mock.Of<IPasskeyService>(),
				Mock.Of<IMfaApprovalService>(), Mock.Of<ISsoBrokerService>(), Mock.Of<ISsoReturnTargetRegistry>(), Mock.Of<IDepartmentSsoService>(), Mock.Of<IDepartmentsService>(), Mock.Of<Resgrid.Model.Providers.ICacheProvider>(), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider())
			{
				ControllerContext = new ControllerContext
				{
					HttpContext = new DefaultHttpContext
					{
						User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(SessionClaimTypes.SessionId, SessionId) }, "test"))
					}
				}
			};

			var shown = (ViewResult)await controller.Disable2FA();
			((Disable2FAViewModel)shown.Model).BlockedByPasskeys.Should().BeTrue();

			var posted = (ViewResult)await controller.Disable2FA(new Disable2FAViewModel { Code = "123456" }, CancellationToken.None);
			((Disable2FAViewModel)posted.Model).BlockedByPasskeys.Should().BeTrue();
			controller.ModelState[string.Empty].Errors.Single().ErrorMessage.Should().Be("DisableBlockedByPasskeys");
			users.Verify(m => m.SetTwoFactorEnabledAsync(It.IsAny<IdentityUser>(), false), Times.Never);
			users.Verify(m => m.VerifyTwoFactorTokenAsync(It.IsAny<IdentityUser>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
		}
	}
}
